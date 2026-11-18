using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Re-lays out the campus exterior from the final campus map (Assets/maps/BML_Final_Campus_Map_v4.json):
    ///  - generates roads, footpaths, lawns, paved areas, sandy ground, tennis courts, trees and grey context blocks
    ///    as a handful of batched meshes under a root object "Campus_Layout";
    ///  - hides the old Blender ground/roads/trees that sit in Campus_Exterior_Environment (the building models stay);
    ///  - moves the building models, the food kiosks and the exterior teleport anchors to the map positions.
    ///
    /// Map coordinates: GA building centre = (0,0), X east, Y north, metres; Unity x = X, z = Y.
    /// The existing building models are far bigger than real size (campus-size factor), so map metres are multiplied by
    /// <see cref="MapToWorld"/>. A building is only ever scaled DOWN, and only when it would not fit its map footprint.
    ///
    /// Run on the open Scene_Campus_Exterior: menu "BMU VR Tour/Apply Campus Map Layout To Open Exterior Scene".
    /// It is idempotent (it deletes and regenerates "Campus_Layout"). BMUTourSceneBuilder.BuildExteriorScene also calls it.
    /// </summary>
    public static class CampusLayoutBuilder
    {
        // ------------------------------------------------------------------ tunables
        public const float MapToWorld = 4f;              // world metres per map metre
        private const string RootName = "Campus_Layout";
        private const string MeshDir = "Assets/Generated/CampusLayout";
        private const string MaterialDir = "Assets/Materials/Environment";
        private const float SurfaceY = 0.30f;            // walkable height: anchors stand here, the ground collider's top is here
        private const bool IncludeContextBlocks = true;  // grey massing for the map's "other buildings"
        private const float ContextBlockHeight = 32f;    // world metres
        private const float TreeSpacing = 30f;           // world metres between trees inside a tree zone
        private const int TreeSeed = 20260;

        // layer heights (decals stacked slightly apart so they never z-fight)
        private const float YGround = -0.15f, YPaved = 0.06f, YGrass = 0.12f, YTennis = 0.18f,
                            YPath = 0.24f, YRoad = SurfaceY - 0.02f, YMarking = SurfaceY + 0.01f;

        // ------------------------------------------------------------------ building table
        private class Building
        {
            public string ObjectName;
            public string MapKey;
            public float FrontAngle;      // math angle (0 = east, 90 = north) of the direction the front faces
            public float Facade, Depth;   // map metres: along the front face / perpendicular to it
            public float RawW, RawD;      // model footprint before scaling (local X / local Z), 0 = do not rescale
            public float ScaleCap;        // current campus scale, never exceeded
            public float YawOffset;       // extra yaw if the model's front is not local +Z
            public bool KeepScale => RawW <= 0f;
        }

        private static readonly Building[] Buildings =
        {
            new Building { ObjectName = "Gateway",                MapKey = "1 GA Building",          FrontAngle = -10.8f, Facade = 57f,   Depth = 56f,   RawW = 140.7f, RawD = 95.8f, ScaleCap = 1.3f },
            new Building { ObjectName = "NorthBlock_Exterior",    MapKey = "7 NB Building",          FrontAngle = -92.6f, Facade = 63.5f, Depth = 30.4f, RawW = 125.2f, RawD = 69.4f, ScaleCap = 1.75f },
            new Building { ObjectName = "BMU_Workshop",           MapKey = "3 Mechanical",           FrontAngle = 88.6f,  Facade = 66.8f, Depth = 29.1f, RawW = 193.1f, RawD = 48.6f, ScaleCap = 1.75f },
            new Building { ObjectName = "VikramSarabhai_Hostel",  MapKey = "5 Vikram Sarabhai Hostel", FrontAngle = 91.2f, Facade = 39f,  Depth = 29.1f, RawW = 105.8f, RawD = 42.7f, ScaleCap = 1.75f },
            new Building { ObjectName = "Gargi_Hostel",           MapKey = "6 Gargi Hostel",         FrontAngle = -90f,   Facade = 39.9f, Depth = 29.5f, RawW = 70.8f,  RawD = 40.9f, ScaleCap = 2.625f },
            // Hand-placed wrappers (scale is hand-tuned in the scene, so it is left alone).
            new Building { ObjectName = "BML_BhagatSingh_Hostel", MapKey = "4 Bhagat Singh Hostel",  FrontAngle = -1.1f,  Facade = 28.3f, Depth = 41.3f },
            new Building { ObjectName = "BML_Library_Medical",    MapKey = "2 Library",              FrontAngle = 81.4f,  Facade = 64.7f, Depth = 30.2f },
        };

        // Roots named BML_* that are not in the table above are the food kiosks. They are not on the map, so they are
        // moved as one group onto the diamond-paver plaza.
        private const string KioskPrefix = "BML_";
        private const string KioskPlazaKey = "Diamond-paver plaza";

        // ------------------------------------------------------------------ menu
        [MenuItem("BMU VR Tour/Apply Campus Map Layout To Open Exterior Scene", false, 14)]
        public static void ApplyMenu()
        {
            if (!Apply()) return;
            Debug.Log("[CampusLayout] Done. Save the scene (Ctrl+S) to keep it.");
        }

        // ------------------------------------------------------------------ data
        private class Road { public string Name; public float Width; public List<Vector2> Pts; }
        private class MapData
        {
            public Dictionary<string, JObject> BuildingObjs = new Dictionary<string, JObject>();
            public List<Road> Roads = new List<Road>();
            public List<Road> Paths = new List<Road>();
            public Dictionary<string, List<Vector2>> Grass = new Dictionary<string, List<Vector2>>();
            public Dictionary<string, List<Vector2>> Trees = new Dictionary<string, List<Vector2>>();
            public Dictionary<string, List<Vector2>> Paved = new Dictionary<string, List<Vector2>>();
            public Dictionary<string, List<Vector2>> Others = new Dictionary<string, List<Vector2>>();
            public List<List<Vector2>> BuildingPolys = new List<List<Vector2>>();
        }

        private static Vector2 W(Vector2 map) { return map * MapToWorld; }

        private static List<Vector2> ReadPts(JToken arr)
        {
            var l = new List<Vector2>();
            foreach (JToken p in arr) l.Add(new Vector2((float)p[0], (float)p[1]));
            return l;
        }

        private static MapData LoadMap()
        {
            string[] candidates =
            {
                Application.dataPath + "/maps/BML_Final_Campus_Map_v4.json",
                Application.dataPath + "/Maps/BML_Final_Campus_Map_v4.json",
            };
            string path = null;
            foreach (string c in candidates) if (File.Exists(c)) { path = c; break; }
            if (path == null) { Debug.LogError("[CampusLayout] BML_Final_Campus_Map_v4.json not found under Assets/maps."); return null; }

            JObject root = JObject.Parse(File.ReadAllText(path));
            var d = new MapData();

            foreach (var kv in (JObject)root["buildings"])
            {
                d.BuildingObjs[kv.Key] = (JObject)kv.Value;
                d.BuildingPolys.Add(ReadPts(kv.Value["corners_m"]));
            }
            foreach (var kv in (JObject)root["roads"])
                d.Roads.Add(new Road { Name = kv.Key, Width = (float)kv.Value["width_m"], Pts = ReadPts(kv.Value["control_points"]) });
            foreach (var kv in (JObject)root["footpaths"])
                d.Paths.Add(new Road { Name = kv.Key, Width = (float)kv.Value["width_m"], Pts = ReadPts(kv.Value["points"]) });
            foreach (var kv in (JObject)root["grass"]) d.Grass[kv.Key] = ReadPts(kv.Value);
            foreach (var kv in (JObject)root["trees"]) d.Trees[kv.Key] = ReadPts(kv.Value);
            foreach (var kv in (JObject)root["paved"]) d.Paved[kv.Key] = ReadPts(kv.Value);
            foreach (var kv in (JObject)root["other_buildings"])
            {
                d.Others[kv.Key] = ReadPts(kv.Value);
                d.BuildingPolys.Add(ReadPts(kv.Value));
            }
            return d;
        }

        // ------------------------------------------------------------------ main entry
        public static bool Apply()
        {
            MapData map = LoadMap();
            if (map == null) return false;

            var scene = EditorSceneManager.GetActiveScene();
            Transform env = FindByName("Campus_Exterior_Environment");

            // Fresh start: regenerate the layout root.
            GameObject old = GameObject.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);

            // 1. Old Blender ground, roads, lawns, trees, walls: hide (buildings that were added under it stay).
            HideOldEnvironment(env);

            // 2. Move buildings, kiosks, anchors.
            var exclusions = new List<Vector3>();   // (x, z, radius) kept clear of trees
            PlaceBuildings(map, exclusions);
            MoveKiosks(map, exclusions);
            PlaceAnchors(map, exclusions);

            // 3. Generated ground layer.
            GameObject root = new GameObject(RootName);
            Directory.CreateDirectory(MeshDir);
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
            BuildGround(map, root.transform);
            BuildSurfaces(map, root.transform);
            BuildRoads(map, root.transform);
            BuildTrees(map, root.transform, exclusions);
            if (IncludeContextBlocks) BuildContextBlocks(map, root.transform);

            // 4. Bounds-dependent helpers.
            FixBounds(map);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        // ------------------------------------------------------------------ helpers: scene
        private static Transform FindByName(string name)
        {
            foreach (GameObject r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }

        private static void HideOldEnvironment(Transform env)
        {
            if (env == null) { Debug.LogWarning("[CampusLayout] Campus_Exterior_Environment not found; nothing to hide."); return; }
            var keep = new HashSet<string>();
            foreach (Building b in Buildings) keep.Add(b.ObjectName);
            int hidden = 0;
            foreach (Transform child in env)
            {
                if (keep.Contains(child.name)) continue;
                if (child.gameObject.activeSelf) { child.gameObject.SetActive(false); hidden++; }
            }
            Debug.Log($"[CampusLayout] Hid {hidden} old environment object(s) under {env.name}.");
        }

        private static float Yaw(Vector2 dirXZ) { return Mathf.Atan2(dirXZ.x, dirXZ.y) * Mathf.Rad2Deg; }
        private static Vector2 Dir(float mathAngleDeg)
        {
            float a = mathAngleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private static Vector2 Centre(JObject b) { return new Vector2((float)b["centre_m"][0], (float)b["centre_m"][1]); }

        private static readonly Dictionary<string, Vector3> BuildingCentreWorld = new Dictionary<string, Vector3>();
        private static readonly Dictionary<string, float> BuildingHalfDepthWorld = new Dictionary<string, float>();
        private static readonly Dictionary<string, Vector2> BuildingFront = new Dictionary<string, Vector2>();

        private static void PlaceBuildings(MapData map, List<Vector3> exclusions)
        {
            BuildingCentreWorld.Clear(); BuildingHalfDepthWorld.Clear(); BuildingFront.Clear();
            Vector3 gatewayOldPos = Vector3.zero; Quaternion gatewayOldRot = Quaternion.identity; bool haveOld = false;

            foreach (Building b in Buildings)
            {
                Transform t = FindByName(b.ObjectName);
                Vector2 c = W(Centre(map.BuildingObjs[b.MapKey]));
                Vector2 front = Dir(b.FrontAngle);
                BuildingFront[b.ObjectName] = front;

                if (t == null) { Debug.LogWarning($"[CampusLayout] '{b.ObjectName}' not found in the open scene; skipped."); continue; }

                if (b.ObjectName == "Gateway") { gatewayOldPos = t.position; gatewayOldRot = t.rotation; haveOld = true; }

                float worldScale = 1f;
                if (!b.KeepScale)
                {
                    float fit = 0.95f * MapToWorld * Mathf.Min(b.Facade / b.RawW, b.Depth / b.RawD);
                    worldScale = Mathf.Min(b.ScaleCap, fit);
                    float parentScale = t.parent != null ? t.parent.lossyScale.x : 1f;
                    t.localScale = Vector3.one * (worldScale / parentScale);
                }

                t.SetPositionAndRotation(new Vector3(c.x, t.position.y, c.y), Quaternion.Euler(0f, Yaw(front) + b.YawOffset, 0f));

                float halfDepth = b.KeepScale ? b.Depth * MapToWorld * 0.5f : b.RawD * worldScale * 0.5f;
                BuildingCentreWorld[b.ObjectName] = new Vector3(c.x, 0f, c.y);
                BuildingHalfDepthWorld[b.ObjectName] = halfDepth;
                Debug.Log($"[CampusLayout] {b.ObjectName}: centre ({c.x:0},{c.y:0}) yaw {Yaw(front):0} scale {(b.KeepScale ? "unchanged" : worldScale.ToString("0.00"))}");
            }

            // Remember how far the Gateway moved so the kiosk group can follow if the plaza is missing.
            if (!haveOld) Debug.LogWarning("[CampusLayout] Gateway not found.");
        }

        private static void MoveKiosks(MapData map, List<Vector3> exclusions)
        {
            var table = new HashSet<string>();
            foreach (Building b in Buildings) table.Add(b.ObjectName);

            var kiosks = new List<Transform>();
            foreach (GameObject r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name.StartsWith(KioskPrefix) && !table.Contains(r.name)) kiosks.Add(r.transform);
            if (kiosks.Count == 0) return;

            Vector3 centroid = Vector3.zero;
            foreach (Transform k in kiosks) centroid += k.position;
            centroid /= kiosks.Count;

            Vector2 plaza = Vector2.zero;
            List<Vector2> poly = map.Paved[KioskPlazaKey];
            foreach (Vector2 p in poly) plaza += p;
            plaza = W(plaza / poly.Count);

            Vector3 delta = new Vector3(plaza.x - centroid.x, 0f, plaza.y - centroid.z);
            foreach (Transform k in kiosks)
            {
                k.position += delta;
                exclusions.Add(new Vector3(k.position.x, k.position.z, 25f));
            }
            Debug.Log($"[CampusLayout] Moved {kiosks.Count} kiosk(s) to the diamond-paver plaza.");
        }

        private static void SetAnchor(string name, Vector3 pos, Vector3 lookDirXZ, List<Vector3> exclusions)
        {
            Transform a = FindByName(name);
            if (a == null) { Debug.LogWarning($"[CampusLayout] Anchor '{name}' not found; skipped."); return; }
            a.SetPositionAndRotation(pos, Quaternion.Euler(0f, Yaw(new Vector2(lookDirXZ.x, lookDirXZ.z)), 0f));
            exclusions.Add(new Vector3(pos.x, pos.z, 14f));
            Debug.Log($"[CampusLayout] {name} -> ({pos.x:0}, {pos.z:0})");
        }

        private static void PlaceAnchors(MapData map, List<Vector3> exclusions)
        {
            // Gateway: 10 m beyond the canopy, looking at the front.
            if (BuildingCentreWorld.ContainsKey("Gateway"))
                AnchorInFront("Anchor_Gateway_Plaza", "Gateway", 10f, exclusions);
            // North Block: far enough back for the whole facade.
            if (BuildingCentreWorld.ContainsKey("NorthBlock_Exterior"))
                AnchorInFront("Anchor_NorthBlock_Plaza", "NorthBlock_Exterior", 80f, exclusions);
            // Workshop.
            if (BuildingCentreWorld.ContainsKey("BMU_Workshop"))
                AnchorInFront("Anchor_Workshop_Plaza", "BMU_Workshop", 40f, exclusions);

            // Front gate: just east of the paver plaza, looking west at the Gateway.
            List<Vector2> plaza = map.Paved[KioskPlazaKey];
            float maxX = float.MinValue, cy = 0f;
            foreach (Vector2 p in plaza) { maxX = Mathf.Max(maxX, p.x); cy += p.y; }
            cy /= plaza.Count;
            Vector2 gate = W(new Vector2(maxX + 4f, cy));
            SetAnchor("Anchor_FrontGate", new Vector3(gate.x, SurfaceY, gate.y), Vector3.left, exclusions);
        }

        private static void AnchorInFront(string anchor, string building, float gap, List<Vector3> exclusions)
        {
            Vector3 c = BuildingCentreWorld[building];
            Vector2 f = BuildingFront[building];
            float dist = BuildingHalfDepthWorld[building] + gap;
            Vector3 pos = new Vector3(c.x + f.x * dist, SurfaceY, c.z + f.y * dist);
            SetAnchor(anchor, pos, new Vector3(-f.x, 0f, -f.y), exclusions);
        }

        private static Bounds MapBoundsWorld(MapData map)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            Action<List<Vector2>> grow = pts => { foreach (Vector2 p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); } };
            foreach (var v in map.Grass.Values) grow(v);
            foreach (var v in map.Trees.Values) grow(v);
            foreach (var v in map.Paved.Values) grow(v);
            foreach (var v in map.Others.Values) grow(v);
            foreach (var v in map.BuildingPolys) grow(v);
            foreach (Road r in map.Roads) grow(r.Pts);
            Vector2 c = W((min + max) * 0.5f), s = W(max - min);
            return new Bounds(new Vector3(c.x, 0f, c.y), new Vector3(s.x, 0f, s.y));
        }

        private static void FixBounds(MapData map)
        {
            Bounds b = MapBoundsWorld(map);
            Transform floor = FindByName("OutOfBounds_ResetFloor");
            if (floor != null)
            {
                floor.position = new Vector3(b.center.x, -5f, b.center.z);
                BoxCollider col = floor.GetComponent<BoxCollider>();
                if (col != null) col.size = new Vector3(b.size.x + 400f, 1f, b.size.z + 400f);
            }
            Transform probe = FindByName("Campus_Exterior_ReflectionProbe");
            if (probe != null)
            {
                probe.position = Vector3.zero;
                ReflectionProbe rp = probe.GetComponent<ReflectionProbe>();
                if (rp != null) { rp.center = new Vector3(b.center.x, 50f, b.center.z); rp.size = new Vector3(b.size.x + 200f, 200f, b.size.z + 200f); }
            }
        }

        // ------------------------------------------------------------------ materials / assets
        private static Material Mat(string name, Color color, float smoothness = 0.05f, bool doubleSided = false)
        {
            string path = $"{MaterialDir}/M_Layout_{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (s == null) s = Shader.Find("Standard");
                m = new Material(s);
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject SaveMeshObject(string name, Transform parent, MeshData data, params Material[] mats)
        {
            Mesh mesh = data.ToMesh(name);
            string path = $"{MeshDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = 0;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        // ------------------------------------------------------------------ mesh builder
        private class MeshData
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<List<int>> Sub = new List<List<int>>();

            public MeshData(int submeshes = 1) { for (int i = 0; i < submeshes; i++) Sub.Add(new List<int>()); }
            public int Add(Vector3 p) { V.Add(p); UV.Add(new Vector2(p.x * 0.02f, p.z * 0.02f)); return V.Count - 1; }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, int sub = 0)
            {
                int i = Add(a); Add(b); Add(c);
                Sub[sub].Add(i); Sub[sub].Add(i + 1); Sub[sub].Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sub = 0) { Tri(a, b, c, sub); Tri(a, c, d, sub); }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(V);
                m.SetUVs(0, UV);
                m.subMeshCount = Sub.Count;
                for (int i = 0; i < Sub.Count; i++) m.SetTriangles(Sub[i], i);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        // ---- geometry primitives (clockwise winding in the XZ plane seen from above = front face up)
        private static float Cross(Vector2 a, Vector2 b, Vector2 c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

        private static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { Vector2 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>Ear-clipping; returns CLOCKWISE triangles (indices into the returned clockwise point list).</summary>
        private static List<Vector2> Triangulate(List<Vector2> input, out List<int> tris)
        {
            var pts = new List<Vector2>(input);
            if (SignedArea(pts) > 0f) pts.Reverse();   // make clockwise
            tris = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++) idx.Add(i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = pts[ia], b = pts[ib], c = pts[ic];
                    if (Cross(a, b, c) >= 0f) continue;   // reflex or degenerate for a clockwise polygon
                    bool ear = true;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (InTri(pts[j], a, b, c)) { ear = false; break; }
                    }
                    if (!ear) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // degenerate polygon: bail out rather than loop forever
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return pts;
        }

        private static void AddPolygon(MeshData m, List<Vector2> mapPoly, float y, int sub = 0)
        {
            List<Vector2> world = mapPoly.ConvertAll(W);
            List<int> tris;
            List<Vector2> pts = Triangulate(world, out tris);
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 a = pts[tris[i]], b = pts[tris[i + 1]], c = pts[tris[i + 2]];
                m.Tri(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), new Vector3(c.x, y, c.y), sub);
            }
        }

        private static void AddDisc(MeshData m, Vector2 c, float r, float y, int sub = 0)
        {
            const int N = 12;
            for (int i = 0; i < N; i++)
            {
                float a0 = -i * 2f * Mathf.PI / N, a1 = -(i + 1) * 2f * Mathf.PI / N;
                m.Tri(new Vector3(c.x, y, c.y),
                      new Vector3(c.x + Mathf.Cos(a0) * r, y, c.y + Mathf.Sin(a0) * r),
                      new Vector3(c.x + Mathf.Cos(a1) * r, y, c.y + Mathf.Sin(a1) * r), sub);
            }
        }

        private static void AddStrip(MeshData m, List<Vector2> mapPts, float widthWorld, float y, bool roundJoints, int sub = 0)
        {
            float h = widthWorld * 0.5f;
            for (int i = 0; i + 1 < mapPts.Count; i++)
            {
                Vector2 p0 = W(mapPts[i]), p1 = W(mapPts[i + 1]);
                Vector2 d = p1 - p0;
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize();
                Vector2 n = new Vector2(-d.y, d.x) * h;
                m.Quad(new Vector3(p0.x + n.x, y, p0.y + n.y), new Vector3(p1.x + n.x, y, p1.y + n.y),
                       new Vector3(p1.x - n.x, y, p1.y - n.y), new Vector3(p0.x - n.x, y, p0.y - n.y), sub);
            }
            if (!roundJoints) return;
            for (int i = 0; i < mapPts.Count; i++) AddDisc(m, W(mapPts[i]), h, y, sub);
        }

        private static void AddDashes(MeshData m, List<Vector2> mapPts, float dashLen, float gapLen, float widthWorld, float y)
        {
            bool on = true; float remaining = dashLen;
            for (int i = 0; i + 1 < mapPts.Count; i++)
            {
                Vector2 p0 = W(mapPts[i]), p1 = W(mapPts[i + 1]);
                float len = Vector2.Distance(p0, p1);
                if (len < 1e-3f) continue;
                Vector2 d = (p1 - p0) / len;
                Vector2 n = new Vector2(-d.y, d.x) * widthWorld * 0.5f;
                float pos = 0f;
                while (pos < len)
                {
                    float step = Mathf.Min(remaining, len - pos);
                    if (on && step > 0.5f)
                    {
                        Vector2 a = p0 + d * pos, b = p0 + d * (pos + step);
                        m.Quad(new Vector3(a.x + n.x, y, a.y + n.y), new Vector3(b.x + n.x, y, b.y + n.y),
                               new Vector3(b.x - n.x, y, b.y - n.y), new Vector3(a.x - n.x, y, a.y - n.y));
                    }
                    pos += step; remaining -= step;
                    if (remaining <= 1e-4f) { on = !on; remaining = on ? dashLen : gapLen; }
                }
            }
        }

        // ------------------------------------------------------------------ layers
        private static void BuildGround(MapData map, Transform parent)
        {
            Bounds b = MapBoundsWorld(map);
            float margin = 200f;
            float x0 = b.min.x - margin, x1 = b.max.x + margin, z0 = b.min.z - margin, z1 = b.max.z + margin;

            var m = new MeshData();
            m.Quad(new Vector3(x0, YGround, z1), new Vector3(x1, YGround, z1), new Vector3(x1, YGround, z0), new Vector3(x0, YGround, z0));
            GameObject g = SaveMeshObject("Ground", parent, m, Mat("Ground", new Color(0.85f, 0.79f, 0.63f)));
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Single collider whose top face is the walkable surface (everything above it is flat decal).
            var col = new GameObject("Ground_Collider");
            col.transform.SetParent(parent, false);
            col.layer = 0;
            col.transform.position = new Vector3((x0 + x1) * 0.5f, SurfaceY - 0.5f, (z0 + z1) * 0.5f);
            var bc = col.AddComponent<BoxCollider>();
            bc.size = new Vector3(x1 - x0, 1f, z1 - z0);
            GameObjectUtility.SetStaticEditorFlags(col, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        private static void BuildSurfaces(MapData map, Transform parent)
        {
            // Paved + sandy ground
            var paved = new MeshData(2);
            foreach (var kv in map.Paved) AddPolygon(paved, kv.Value, YPaved, 0);
            if (map.Grass.ContainsKey("Sandy ground (dry)")) AddPolygon(paved, map.Grass["Sandy ground (dry)"], YPaved, 1);
            SaveMeshObject("Paved_And_Sand", parent, paved, Mat("Paved", new Color(0.74f, 0.69f, 0.60f)), Mat("Sand", new Color(0.84f, 0.72f, 0.45f)));

            // Lawns (everything in "grass" except sand and tennis)
            var grass = new MeshData();
            foreach (var kv in map.Grass)
            {
                if (kv.Key.StartsWith("Sandy") || kv.Key.StartsWith("Tennis")) continue;
                AddPolygon(grass, kv.Value, YGrass);
            }
            SaveMeshObject("Lawns", parent, grass, Mat("Grass", new Color(0.36f, 0.62f, 0.24f)));

            // Tennis courts
            var tennis = new MeshData();
            foreach (var kv in map.Grass) if (kv.Key.StartsWith("Tennis")) AddPolygon(tennis, kv.Value, YTennis);
            SaveMeshObject("Tennis_Courts", parent, tennis, Mat("Tennis", new Color(0.20f, 0.50f, 0.74f)));

            // Footpaths
            var paths = new MeshData();
            foreach (Road p in map.Paths) AddStrip(paths, p.Pts, p.Width * MapToWorld, YPath, true);
            SaveMeshObject("Footpaths", parent, paths, Mat("Footpath", new Color(0.92f, 0.88f, 0.78f)));
        }

        private static void BuildRoads(MapData map, Transform parent)
        {
            var asphalt = new MeshData();
            var marks = new MeshData();
            foreach (Road r in map.Roads)
            {
                AddStrip(asphalt, r.Pts, r.Width * MapToWorld, YRoad, true);
                bool context = r.Name.Contains("context");
                if (r.Width >= 5f || context)
                    AddDashes(marks, r.Pts, 3f * MapToWorld, 2.5f * MapToWorld, 0.25f * MapToWorld, YMarking);
            }
            SaveMeshObject("Roads", parent, asphalt, Mat("Asphalt", new Color(0.23f, 0.23f, 0.25f), 0.12f));
            SaveMeshObject("Road_Markings", parent, marks, Mat("RoadMarking", new Color(0.93f, 0.93f, 0.9f)));
        }

        // ------------------------------------------------------------------ trees
        private static bool InPoly(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static void BuildTrees(MapData map, Transform parent, List<Vector3> exclusions)
        {
            // Building polygons (world), grown a little so canopies keep off the walls.
            var blocked = new List<List<Vector2>>();
            foreach (List<Vector2> poly in map.BuildingPolys)
            {
                Vector2 c = Vector2.zero;
                foreach (Vector2 p in poly) c += p;
                c /= poly.Count;
                blocked.Add(poly.ConvertAll(p => W(c + (p - c) * 1.12f)));
            }

            var rng = new System.Random(TreeSeed);
            Func<float> r01 = () => (float)rng.NextDouble();
            var cells = new Dictionary<Vector2Int, MeshData>();
            float cell = 100f * MapToWorld;
            int count = 0;

            foreach (var zone in map.Trees)
            {
                List<Vector2> poly = zone.Value.ConvertAll(W);
                Vector2 min = poly[0], max = poly[0];
                foreach (Vector2 p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }

                for (float x = min.x; x <= max.x; x += TreeSpacing)
                    for (float z = min.y; z <= max.y; z += TreeSpacing)
                    {
                        var p = new Vector2(x + (r01() - 0.5f) * TreeSpacing * 0.7f, z + (r01() - 0.5f) * TreeSpacing * 0.7f);
                        float scale = 0.8f + r01() * 0.45f;
                        float yaw = r01() * 360f;
                        if (!InPoly(p, poly)) continue;
                        if (IsBlocked(p, scale, map, blocked, exclusions)) continue;

                        var key = new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));
                        if (!cells.TryGetValue(key, out MeshData md)) { md = new MeshData(2); cells[key] = md; }
                        AddTree(md, new Vector3(p.x, SurfaceY - 0.1f, p.y), scale, yaw);
                        count++;
                    }
            }

            Material trunk = Mat("TreeTrunk", new Color(0.30f, 0.20f, 0.12f));
            Material leaves = Mat("TreeLeaves", new Color(0.15f, 0.42f, 0.17f));
            var group = new GameObject("Trees");
            group.transform.SetParent(parent, false);
            foreach (var kv in cells)
                SaveMeshObject($"Trees_{kv.Key.x}_{kv.Key.y}".Replace("-", "n"), group.transform, kv.Value, trunk, leaves);
            Debug.Log($"[CampusLayout] Scattered {count} trees in {cells.Count} batch(es).");
        }

        private static bool IsBlocked(Vector2 p, float scale, MapData map, List<List<Vector2>> blocked, List<Vector3> exclusions)
        {
            foreach (List<Vector2> poly in blocked) if (InPoly(p, poly)) return true;
            foreach (Vector3 e in exclusions) if (Vector2.Distance(p, new Vector2(e.x, e.y)) < e.z) return true;

            float canopy = 9f * scale;
            foreach (Road r in map.Roads)
            {
                float limit = r.Width * MapToWorld * 0.5f + canopy * 0.6f;
                for (int i = 0; i + 1 < r.Pts.Count; i++)
                    if (DistToSegment(p, W(r.Pts[i]), W(r.Pts[i + 1])) < limit) return true;
            }
            foreach (Road r in map.Paths)
            {
                float limit = r.Width * MapToWorld * 0.5f + canopy * 0.4f;
                for (int i = 0; i + 1 < r.Pts.Count; i++)
                    if (DistToSegment(p, W(r.Pts[i]), W(r.Pts[i + 1])) < limit) return true;
            }
            foreach (var kv in map.Paved) if (InPoly(p, kv.Value.ConvertAll(W))) return true;
            return false;
        }

        // Low-poly tree: 6-sided trunk (submesh 0) and a faceted icosahedron canopy (submesh 1).
        private static readonly Vector3[] IcoV = BuildIcoVerts();
        private static readonly int[] IcoF =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };

        private static Vector3[] BuildIcoVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new[]
            {
                new Vector3(-1,  t, 0), new Vector3(1,  t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1,  t), new Vector3(0, 1,  t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3( t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Length; i++) v[i].Normalize();
            return v;
        }

        private static void AddTree(MeshData m, Vector3 basePos, float scale, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float trunkR = 0.9f * scale, trunkH = 11f * scale;
            const int N = 6;
            for (int i = 0; i < N; i++)
            {
                float a0 = -i * 2f * Mathf.PI / N, a1 = -(i + 1) * 2f * Mathf.PI / N;
                Vector3 b0 = basePos + new Vector3(Mathf.Cos(a0) * trunkR, 0f, Mathf.Sin(a0) * trunkR);
                Vector3 b1 = basePos + new Vector3(Mathf.Cos(a1) * trunkR, 0f, Mathf.Sin(a1) * trunkR);
                // Outward faces: reversed relative to the clockwise-from-above ring.
                m.Quad(b1, b1 + Vector3.up * trunkH, b0 + Vector3.up * trunkH, b0, 0);
            }

            Vector3 centre = basePos + Vector3.up * (trunkH + 4f * scale);
            Vector3 radii = new Vector3(10f, 8.5f, 10f) * scale;
            for (int f = 0; f < IcoF.Length; f += 3)
            {
                Vector3 a = centre + rot * Vector3.Scale(IcoV[IcoF[f]], radii);
                Vector3 b = centre + rot * Vector3.Scale(IcoV[IcoF[f + 1]], radii);
                Vector3 c = centre + rot * Vector3.Scale(IcoV[IcoF[f + 2]], radii);
                m.Tri(a, b, c, 1);   // this face list already winds the way Unity expects (outward normal = cross(b-a, c-a))
            }
        }

        // ------------------------------------------------------------------ context blocks
        private static void BuildContextBlocks(MapData map, Transform parent)
        {
            var m = new MeshData();
            foreach (var kv in map.Others)
            {
                List<Vector2> world = kv.Value.ConvertAll(W);
                List<int> tris;
                List<Vector2> pts = Triangulate(world, out tris);

                float top = ContextBlockHeight + SurfaceY;
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector2 a = pts[tris[i]], b = pts[tris[i + 1]], c = pts[tris[i + 2]];
                    m.Tri(new Vector3(a.x, top, a.y), new Vector3(b.x, top, b.y), new Vector3(c.x, top, c.y));
                }
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
                    Vector3 a0 = new Vector3(a.x, SurfaceY, a.y), b0 = new Vector3(b.x, SurfaceY, b.y);
                    Vector3 a1 = new Vector3(a.x, top, a.y), b1 = new Vector3(b.x, top, b.y);
                    m.Quad(b0, b1, a1, a0);
                }
            }
            SaveMeshObject("Context_Buildings", parent, m, Mat("ContextBlock", new Color(0.74f, 0.74f, 0.72f), 0.1f, true));
        }
    }
}
