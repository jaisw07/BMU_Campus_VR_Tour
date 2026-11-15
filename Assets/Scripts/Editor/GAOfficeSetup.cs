using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Gateway Building office ("GA_Office"): Assets/Models/Interior/GA_Office.glb, a 2x export used at exactly 0.5x
    /// (16.9 x 3.9 x 27.2 m). Installed into Scene_Gateway_Interiors under the shared "Gateway_Interior" root, next to GA_Lab and
    /// GA_Auditorium. The GO_* children are never renamed, moved or rescaled. Everything the setup adds lives under the GA_Office
    /// root, inside a "GAOffice_Setup" child that cancels the root's 0.5 scale so those objects are authored directly in metres.
    /// Room frame: origin = footprint centre at floor level. The lobby is at -Z (z -11.7..-7.2, entrance doors in the wall at
    /// z -11.5), the open office at z -7.2..13.4 with glass cabins along both side walls (x = +-4 and +-6).
    /// </summary>
    public static class GAOfficeSetup
    {
        private const string GlbPath = "Assets/Models/Interior/GA_Office.glb";
        private const string MaterialDir = "Assets/Materials/GA_Office";
        public const string RootName = "GA_Office";

        private const float RootScale = 0.5f; // the GLB is a 2x export; uniform only

        // No placement was supplied. Pods sit at x = 0 and 60, GA_Lab at 120, GA_Auditorium at 180 (60 m slots): next slot.
        public static readonly Vector3 OfficePosition = new Vector3(240f, 0f, 0f);
        public const float OfficeYRotation = 0f;

        private static readonly Vector3 ExpectedSize = new Vector3(16.9f, 3.9f, 27.2f); // X, height, Z after the 0.5x scale

        private const float LightKelvin = 4000f;
        private static readonly Vector2 PanelAreaSize = new Vector2(0.6f, 0.6f);
        private static float PanelIntensity => 3.1416f / (PanelAreaSize.x * PanelAreaSize.y) * 3f; // same per-fixture output as GA_Lab / NB-105

        private static readonly string[] GlassNames = { "GO_window_glass", "GO_glass_clear", "GO_glass_frost" };

        [MenuItem("BMU VR Tour/Add GA Office To Gateway Interiors Scene", false, 22)]
        public static void AddToGatewayScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(GALabSetup.GatewayScenePath, OpenSceneMode.Single);
            StringBuilder log = new StringBuilder();
            InstallInGatewayScene(log);
            EditorSceneManager.SaveScene(scene, GALabSetup.GatewayScenePath);
            GALabSetup.ComputeOcclusion(scene, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[GAOfficeSetup] Done.\n" + log);
        }

        /// <summary>Re-installs GA_Office into the open Gateway interiors scene (replaces an existing one). Does not save.</summary>
        public static GameObject InstallInGatewayScene(StringBuilder log)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject interior = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == GALabSetup.InteriorRootName) interior = go;
            if (interior == null) interior = new GameObject(GALabSetup.InteriorRootName);

            for (int i = interior.transform.childCount - 1; i >= 0; i--)
                if (interior.transform.GetChild(i).name == RootName) Object.DestroyImmediate(interior.transform.GetChild(i).gameObject);

            GameObject root = Install(interior.transform, OfficePosition, OfficeYRotation, log);
            EditorSceneManager.MarkSceneDirty(scene);
            return root;
        }

        private static void EnsureLightmapUVs()
        {
            AssetImporter imp = AssetImporter.GetAtPath(GlbPath);
            if (imp == null) return;
            SerializedObject so = new SerializedObject(imp);
            SerializedProperty p = so.FindProperty("editorImportSettings.generateSecondaryUVSet");
            if (p != null && !p.boolValue)
            {
                p.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                imp.SaveAndReimport();
            }
        }

        public static GameObject Install(Transform parent, Vector3 localPosition, float yRotation, StringBuilder log)
        {
            EnsureLightmapUVs();
            GameObject glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (glb == null) { Debug.LogError($"[GAOfficeSetup] Could not load '{GlbPath}'."); return null; }

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            root.name = RootName;
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            root.transform.localScale = Vector3.one * RootScale;

            GameObject world = new GameObject("GAOffice_Setup");
            world.transform.SetParent(root.transform, false);
            world.transform.localScale = Vector3.one / RootScale; // net scale 1: colliders, lights and the player are in real metres

            ConfigureRenderers(root, log);
            ConfigureMaterials(root, log);
            AddColliders(root, log);
            Physics.SyncTransforms();
            AddLights(root, world.transform, log);
            AddProbesAndCulling(world.transform, log);
            Transform anchor = AddEntranceAnchor(world.transform, log);
            GALabSetup.AddPlayer(world.transform, anchor, "GAOffice_Player", "Gateway Office", UnityEngine.InputSystem.Key.Digit6);
            // The tour card "Program Office" lands the rig INSIDE the open office (just past the lobby's inner door), not in the lobby:
            // from the lobby the wall and the glass doors block the teleport ray, so the office could not be entered. The desktop test
            // player and hotkey 6 still start at the lobby entrance.
            Transform inside = AddInsideAnchor(world.transform, log);
            GALabSetup.AddRoomPod(world.transform, inside, "RoomPod_ProgramOffice", "gw-office", "Program Office");
            GALabSetup.SetTeleportRayIgnoreLayer(root, log, "GO_Cubicles", "GO_Chairs", "GO_Cabin_Furniture", "GO_Cabins_Glass", "GO_Lobby", "GO_Ceiling");
            GALabSetup.AddTeleportAnchors(world.transform, "GAOffice_TeleportAnchors", "GO_Room_Shell", new[]
            {
                // lobby -> inner door -> open office
                new GALabSetup.TeleportSpec("TP_Office_Lobby",     0.55f,  -9.0f, 0f),
                new GALabSetup.TeleportSpec("TP_Office_Entrance",  0.55f,  -5.8f, 0f),
                new GALabSetup.TeleportSpec("TP_Office_North",     0.0f,   -3.5f, 0f),
                new GALabSetup.TeleportSpec("TP_Office_Centre",    0.0f,    2.9f, 0f),
                new GALabSetup.TeleportSpec("TP_Office_South",     0.0f,   10.5f, 180f),
                // one anchor inside each of the 8 glass cabins (4 west, 4 east)
                new GALabSetup.TeleportSpec("TP_Cabin_W1",        -5.0f,   -2.1f, 270f),
                new GALabSetup.TeleportSpec("TP_Cabin_W2",        -5.0f,    1.4f, 270f),
                new GALabSetup.TeleportSpec("TP_Cabin_W3",        -5.0f,    4.9f, 270f),
                new GALabSetup.TeleportSpec("TP_Cabin_W4",        -5.0f,    8.4f, 270f),
                new GALabSetup.TeleportSpec("TP_Cabin_E1",         5.0f,   -2.1f, 90f),
                new GALabSetup.TeleportSpec("TP_Cabin_E2",         5.0f,    1.3f, 90f),
                new GALabSetup.TeleportSpec("TP_Cabin_E3",         5.0f,    4.9f, 90f),
                new GALabSetup.TeleportSpec("TP_Cabin_E4",         5.0f,    8.4f, 90f),
            }, log);
            GALabSetup.EnsureGatewayOutOfBounds();
            Report(root, log);
            return root;
        }

        // --- Static flags / GI -----------------------------------------------------------------------------------
        private static readonly string[] FurnitureMeshes = { "GO_Cubicles", "GO_Chairs", "GO_Cabin_Furniture", "GO_Lobby", "GO_Cabins_Glass" };

        private static void ConfigureRenderers(GameObject root, StringBuilder log)
        {
            int lightmapped = 0, probeLit = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool transparent = false;
                foreach (Material m in r.sharedMaterials) if (m != null && m.renderQueue >= 3000) transparent = true;
                bool furniture = System.Array.IndexOf(FurnitureMeshes, r.name) >= 0;

                StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                // Opaque architecture occludes; glass meshes must not, and the flat ceiling is left out (in the auditorium the same kind of
                // zero-thickness ceiling made baked occlusion data hide the ceiling and fixtures from inside the room).
                if (!transparent && r.name != "GO_Ceiling" && r.name != "GO_Lights") flags |= StaticEditorFlags.OccluderStatic;
                if (!furniture) flags |= StaticEditorFlags.ContributeGI; // architecture is lightmapped; furniture is probe-lit
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                r.receiveGI = furniture ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
                r.gameObject.layer = 0; // never the Teleport layer
                if (furniture) probeLit++; else lightmapped++;
            }
            log?.AppendLine($"static: all renderers static; lightmapped={lightmapped} (architecture), probe-lit={probeLit} (furniture, cabin glass) (lighting NOT baked)");
        }

        // --- Materials: imported ones are kept; only the chair materials get a GPU-instanced project copy ---------
        private static void ConfigureMaterials(GameObject root, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/Materials", "GA_Office");

            var copies = new Dictionary<Material, Material>();
            int instanced = 0;
            MeshRenderer chairs = root.transform.Find("GO_Chairs").GetComponent<MeshRenderer>();
            Material[] mats = chairs.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material src = mats[i];
                if (src == null) continue;
                if (!copies.TryGetValue(src, out Material c))
                {
                    string path = $"{MaterialDir}/{src.name}_GPUI.mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
                    c = new Material(src) { name = src.name + "_GPUI", enableInstancing = true }; // same shader, same textures
                    AssetDatabase.CreateAsset(c, path);
                    copies[src] = c;
                }
                mats[i] = c;
                instanced++;
            }
            chairs.sharedMaterials = mats;

            var seen = new HashSet<Material>();
            int missing = 0, pink = 0;
            var glassReport = new StringBuilder();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) { missing++; log?.AppendLine("MISSING material on " + r.name); continue; }
                    if (!seen.Add(m)) continue;
                    if (m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalError")) { pink++; log?.AppendLine("PINK material " + m.name); }
                    if (System.Array.IndexOf(GlassNames, m.name) >= 0)
                    {
                        bool ok = m.renderQueue >= 3000 && m.GetFloat("_Surface") > 0.5f && m.GetFloat("_ZWrite") < 0.5f;
                        glassReport.Append($"{m.name}:{(ok ? "transparent/no-zwrite" : "NOT TRANSPARENT")} ");
                    }
                }
            log?.AppendLine($"materials: distinct={seen.Count}, missing={missing}, pink/broken shader={pink}; GPU-instanced chair material slots={instanced} ({copies.Count} copies)");
            log?.AppendLine("glass: " + glassReport);
        }

        // --- Colliders -------------------------------------------------------------------------------------------
        private static void AddColliders(GameObject root, StringBuilder log)
        {
            float k = 1f / RootScale; // mesh-local units are 2x room metres
            int leavesRemoved = 0;
            foreach (string n in new[] { "GO_Room_Shell", "GO_Ceiling", "GO_Doors", "GO_Windows", "GO_Lobby" })
            {
                Transform t = root.transform.Find(n);
                MeshCollider mc = t.gameObject.AddComponent<MeshCollider>();
                Mesh render = t.GetComponent<MeshFilter>().sharedMesh;
                // Both glass doorways (lobby entrance and lobby -> office) are modelled with one leaf swung open at an angle, which fills
                // most of the 1.1 m opening and leaves a 0.1-0.5 m gap: not walkable. Their collision mesh is a copy of the render mesh
                // without those two open leaves (render mesh untouched; closed leaves, frames and glass stay solid).
                mc.sharedMesh = n == "GO_Doors" ? BuildDoorsCollisionMesh(render, k, out leavesRemoved) : render;
                mc.convex = false;
            }
            log?.AppendLine($"doorways: GO_Doors collision mesh excludes {leavesRemoved} swung-open leaf pieces (entrance + lobby/office doors) so both openings are walkable");

            // Cabin glass: solid for the player (the renderer stays alpha-blended). One thin box per glass column (the clear / frost
            // panes stacked in one bay are merged vertically only, so door openings stay open; the closed wooden door leaves and the
            // aluminium frames get no collider, so the cabin doors are walkable). Panes starting above 2 m (transoms) are skipped.
            Mesh glassMesh = root.transform.Find("GO_Cabins_Glass").GetComponent<MeshFilter>().sharedMesh;
            Material[] gm = root.transform.Find("GO_Cabins_Glass").GetComponent<MeshRenderer>().sharedMaterials;
            var glassSubs = new List<int>();
            for (int i = 0; i < gm.Length; i++) if (gm[i] != null && (gm[i].name == "GO_glass_clear" || gm[i].name == "GO_glass_frost")) glassSubs.Add(i);
            var columns = new Dictionary<string, Bounds>();
            foreach (Bounds b in GALabSetup.FindIslands(glassMesh, glassSubs.ToArray()))
            {
                Vector3 s = b.size / k;
                bool thinX = s.x < s.z;
                float plane = thinX ? b.center.x : b.center.z;
                float lo = thinX ? b.min.z : b.min.x, hi = thinX ? b.max.z : b.max.x;
                string key = (thinX ? "x" : "z") + Round(plane / k, 0.05f) + "|" + Round(lo / k, 0.05f) + "|" + Round(hi / k, 0.05f);
                if (columns.TryGetValue(key, out Bounds u)) { u.Encapsulate(b); columns[key] = u; } else columns[key] = b;
            }
            var glassBoxes = new List<Bounds>();
            foreach (Bounds b in columns.Values)
                if (b.min.y / k < 2.0f) glassBoxes.Add(Thicken(b, 0.08f * k));
            int glass = GALabSetup.AddBoxes(root, "GO_Cabins_Glass", glassBoxes, k);

            // Cubicles: one box per partition panel (grouped by plane so the comb of dividers is not filled in) and one per desk set.
            Mesh cubMesh = root.transform.Find("GO_Cubicles").GetComponent<MeshFilter>().sharedMesh;
            Material[] cm = root.transform.Find("GO_Cubicles").GetComponent<MeshRenderer>().sharedMaterials;
            var panelSubs = new List<int>(); var deskSubs = new List<int>();
            for (int i = 0; i < cm.Length; i++)
            {
                string n = cm[i] != null ? cm[i].name : "";
                if (n == "GO_panel_trim" || n == "GO_panel_lami" || n == "GO_panel_fabric" || n == "GO_alu") panelSubs.Add(i);
                else if (n == "GO_desk_white" || n == "GO_black") deskSubs.Add(i);
            }
            var panelGroups = new Dictionary<string, List<Bounds>>();
            var panelBoxes = new List<Bounds>();
            foreach (Bounds b in GALabSetup.FindIslands(cubMesh, panelSubs.ToArray()))
            {
                Vector3 s = b.size / k;
                float longSide = Mathf.Max(s.x, s.z), thin = Mathf.Min(s.x, s.z);
                if (longSide < 0.3f || s.y < 0.3f) continue; // posts, trims, screws
                if (thin > 0.15f) { panelBoxes.Add(b); continue; }
                bool thinX = s.x < s.z;
                string key = (thinX ? "x" : "z") + Round((thinX ? b.center.x : b.center.z) / k, 0.1f);
                if (!panelGroups.TryGetValue(key, out List<Bounds> list)) panelGroups[key] = list = new List<Bounds>();
                list.Add(b);
            }
            foreach (List<Bounds> list in panelGroups.Values)
                foreach (Bounds b in GALabSetup.Merge(list, 0.1f * k, new Vector2(12f, 12f) * k)) panelBoxes.Add(Thicken(b, 0.06f * k));
            int panels = GALabSetup.AddBoxes(root, "GO_Cubicles", panelBoxes, k);
            int desks = GALabSetup.AddBoxes(root, "GO_Cubicles",
                GALabSetup.Merge(GALabSetup.FindIslands(cubMesh, deskSubs.ToArray()), 0.1f * k, new Vector2(2.4f, 2.4f) * k), k);

            // Chairs: one box per chair (castors, seat and back merge; neighbours stay separate).
            Mesh chairMesh = root.transform.Find("GO_Chairs").GetComponent<MeshFilter>().sharedMesh;
            int chairs = GALabSetup.AddBoxes(root, "GO_Chairs",
                GALabSetup.Merge(GALabSetup.FindIslands(chairMesh, GALabSetup.All(chairMesh)), 0.08f * k, new Vector2(1.0f, 1.0f) * k), k);

            // Cabin furniture: desks, sofas, cabinets, planters (leaves, books and paper are clutter and get nothing).
            Mesh furMesh = root.transform.Find("GO_Cabin_Furniture").GetComponent<MeshFilter>().sharedMesh;
            Material[] fm = root.transform.Find("GO_Cabin_Furniture").GetComponent<MeshRenderer>().sharedMaterials;
            var furSubs = new List<int>();
            for (int i = 0; i < fm.Length; i++)
            {
                string n = fm[i] != null ? fm[i].name : "";
                if (n == "GO_chrome" || n == "GO_desk_white" || n == "GO_sofa_yellow" || n == "GO_wood_cherry" || n == "GO_pot_blue" || n == "GO_soil" || n == "GO_pot_white" || n == "GO_steel") furSubs.Add(i);
            }
            int furniture = GALabSetup.AddBoxes(root, "GO_Cabin_Furniture",
                GALabSetup.Merge(GALabSetup.FindIslands(furMesh, furSubs.ToArray()), 0.05f * k, new Vector2(2.4f, 2.4f) * k), k);

            log?.AppendLine($"colliders: MeshCollider(non-convex)=5 (shell, ceiling, doors, windows, lobby); BoxCollider cabin-glass columns={glass}, cubicle partition panels={panels}, desk sets={desks}, chairs={chairs}, cabin furniture={furniture}");
        }

        // Swing zones of the two open door leaves, in room metres (island centres; the leaves hang inside the wall line).
        private static bool InOpenLeafZone(Vector3 c) =>
            (c.x > 0.1f && c.x < 1.2f && c.z > -11.45f && c.z < -10.7f) ||   // lobby entrance, right leaf
            (c.x > 0.1f && c.x < 1.2f && c.z > -6.95f && c.z < -6.5f);       // lobby -> office door, right leaf

        private const string CollisionDir = "Assets/Models/Interior/GA_Office_Collision";

        private static Mesh BuildDoorsCollisionMesh(Mesh src, float k, out int removedIslands)
        {
            Vector3[] v = src.vertices;
            var map = new Dictionary<long, int>();
            int[] rep = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long key = ((long)Mathf.RoundToInt(v[i].x * 500f) * 73856093L) ^ ((long)Mathf.RoundToInt(v[i].y * 500f) * 19349663L) ^
                           ((long)Mathf.RoundToInt(v[i].z * 500f) * 83492791L);
                if (map.TryGetValue(key, out int j)) rep[i] = j; else { map[key] = i; rep[i] = i; }
            }
            var kept = new List<int>();
            var removed = new HashSet<long>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] parent = new int[v.Length];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                int[] idx = src.GetTriangles(s);
                for (int i = 0; i < idx.Length; i += 3)
                {
                    int a = Find(rep[idx[i]]);
                    parent[Find(rep[idx[i + 1]])] = a;
                    parent[Find(rep[idx[i + 2]])] = Find(a);
                }
                var bounds = new Dictionary<int, Bounds>();
                for (int i = 0; i < idx.Length; i++)
                {
                    int r = Find(rep[idx[i]]);
                    if (bounds.TryGetValue(r, out Bounds b)) { b.Encapsulate(v[idx[i]]); bounds[r] = b; } else bounds[r] = new Bounds(v[idx[i]], Vector3.zero);
                }
                for (int i = 0; i < idx.Length; i += 3)
                {
                    int r = Find(rep[idx[i]]);
                    if (InOpenLeafZone(bounds[r].center / k)) { removed.Add((long)s * 1000000L + r); continue; }
                    kept.Add(idx[i]); kept.Add(idx[i + 1]); kept.Add(idx[i + 2]);
                }
            }
            removedIslands = removed.Count;

            Mesh m = new Mesh { name = "GO_Doors_Collision", indexFormat = src.indexFormat };
            m.vertices = v;
            m.triangles = kept.ToArray();
            m.RecalculateBounds();
            if (!AssetDatabase.IsValidFolder(CollisionDir)) AssetDatabase.CreateFolder("Assets/Models/Interior", "GA_Office_Collision");
            string path = CollisionDir + "/GO_Doors_Collision.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static float Round(float v, float step) => Mathf.Round(v / step) * step;

        private static Bounds Thicken(Bounds b, float minThickness)
        {
            Vector3 s = b.size;
            if (s.x < minThickness) s.x = minThickness;
            if (s.z < minThickness) s.z = minThickness;
            b.size = s;
            return b;
        }

        // --- Lights ----------------------------------------------------------------------------------------------
        // One soft Rectangle area light under each of the 20 ceiling LED panels (the panels are 3-3.6 m apart), Baked (URP renders
        // Rectangle lights only through a bake, so they stay dark until the room is baked), plus two baked directional window lights.
        // Realtime lights: 0.
        private static void AddLights(GameObject root, Transform world, StringBuilder log)
        {
            float k = 1f / RootScale;
            Color warm = Mathf.CorrelatedColorTemperatureToRGB(LightKelvin);
            GameObject lightsRoot = new GameObject("GAOffice_Lights");
            lightsRoot.transform.SetParent(world, false);

            Transform lt = root.transform.Find("GO_Lights");
            Mesh lm = lt.GetComponent<MeshFilter>().sharedMesh;
            Material[] mats = lt.GetComponent<MeshRenderer>().sharedMaterials;
            int ledSub = -1;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "GO_led") ledSub = i;
            int n = 0;
            if (ledSub >= 0)
            {
                var panels = GALabSetup.FindIslands(lm, new[] { ledSub });
                panels.Sort((a, b) => a.center.z != b.center.z ? a.center.z.CompareTo(b.center.z) : a.center.x.CompareTo(b.center.x));
                foreach (Bounds b in panels)
                {
                    Vector3 c = b.center / k;
                    GameObject go = new GameObject($"PanelLight_{n + 1:00}");
                    go.transform.SetParent(lightsRoot.transform, false);
                    go.transform.localPosition = new Vector3(c.x, c.y - 0.03f, c.z); // 2.69 m under the low ceiling, 2.89 m under the raised bay
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Rectangle;
                    l.areaSize = PanelAreaSize;
                    l.useColorTemperature = true;
                    l.colorTemperature = LightKelvin;
                    l.color = warm;
                    l.intensity = PanelIntensity;
                    l.range = 8f;
                    l.shadows = LightShadows.Soft;
                    l.lightmapBakeType = LightmapBakeType.Baked;
                    n++;
                }
            }

            // Windows are in both side walls (x = +-8.15), so one soft baked directional per side, travelling inward and slightly down.
            int suns = 0;
            foreach (var side in new[] { ("East", -90f), ("West", 90f) })
            {
                GameObject sun = new GameObject("WindowLight_" + side.Item1);
                sun.transform.SetParent(lightsRoot.transform, false);
                sun.transform.localRotation = Quaternion.Euler(35f, side.Item2, 0f);
                Light d = sun.AddComponent<Light>();
                d.type = LightType.Directional;
                d.useColorTemperature = true;
                d.colorTemperature = 6000f;
                d.color = Mathf.CorrelatedColorTemperatureToRGB(6000f);
                d.intensity = 0.5f;
                d.shadows = LightShadows.Soft;
                d.lightmapBakeType = LightmapBakeType.Baked;
                suns++;
            }
            log?.AppendLine($"lights: {n} baked Rectangle area lights under the ceiling LED panels (4000 K, 0.6x0.6 m, intensity {PanelIntensity:F1}) + {suns} baked directional window lights = {n + suns}; realtime/mixed = 0");
        }

        private static void AddProbesAndCulling(Transform world, StringBuilder log)
        {
            // Reflection probes: the open office and the lobby (baked mode, not baked).
            MakeProbe(world, "GAOffice_ReflectionProbe_Office", new Vector3(0f, 1.5f, 3.13f), new Vector3(16.4f, 3.2f, 20.4f));
            MakeProbe(world, "GAOffice_ReflectionProbe_Lobby", new Vector3(0f, 1.5f, -9.43f), new Vector3(8.4f, 3.2f, 4.4f));

            // Furniture is probe-lit, so the rooms need light probes once the lighting is baked.
            GameObject lpg = new GameObject("GAOffice_LightProbes");
            lpg.transform.SetParent(world, false);
            var pts = new List<Vector3>();
            foreach (float x in new[] { -6f, -3f, 0f, 3f, 6f })
                foreach (float z in new[] { -5f, -1f, 3f, 7f, 11f })
                    pts.Add(new Vector3(x, 1.2f, z));
            foreach (float x in new[] { -2.5f, 0f, 2.5f })
                foreach (float z in new[] { -10.5f, -8.5f })
                    pts.Add(new Vector3(x, 1.2f, z));
            lpg.AddComponent<LightProbeGroup>().probePositions = pts.ToArray();

            // Occlusion culling volumes for the cabins and the lobby.
            GameObject cabins = new GameObject("GAOffice_OcclusionArea_Cabins");
            cabins.transform.SetParent(world, false);
            cabins.transform.localPosition = new Vector3(0f, 1.5f, 2.5f);
            cabins.AddComponent<OcclusionArea>().size = new Vector3(12.6f, 3.4f, 18.0f); // x +-6.3, z -6.5..11.5: all cabin glass
            GameObject lobby = new GameObject("GAOffice_OcclusionArea_Lobby");
            lobby.transform.SetParent(world, false);
            lobby.transform.localPosition = new Vector3(0f, 1.5f, -9.43f);
            lobby.AddComponent<OcclusionArea>().size = new Vector3(8.8f, 3.4f, 4.8f);
            log?.AppendLine($"probes/culling: 2 reflection probes (open office, lobby; baked mode, not baked), {pts.Count} light probes, 2 occlusion areas (cabins, lobby)");
        }

        private static void MakeProbe(Transform world, string name, Vector3 pos, Vector3 size)
        {
            GameObject po = new GameObject(name);
            po.transform.SetParent(world, false);
            po.transform.localPosition = pos;
            ReflectionProbe p = po.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Baked;
            p.boxProjection = true;
            p.resolution = 128;
            p.size = size;
        }

        // --- Lobby entrance anchor ---------------------------------------------------------------------------------
        private static Transform AddEntranceAnchor(Transform world, StringBuilder log)
        {
            // The glass entrance doors are in the lobby's front wall (z = -11.53); the doorway is the 1 m gap at x ~ 0..1.1 (one
            // leaf closed, one swung open). Spawn 1.2 m inside it and look down the lobby toward the office.
            Vector3 want = world.TransformPoint(new Vector3(0.55f, 0f, -10.3f));

            GameObject anchorsRoot = new GameObject("GAOffice_Anchors");
            anchorsRoot.transform.SetParent(world, false);
            GameObject anchorObj = new GameObject("Anchor_GAOffice_Lobby");
            anchorObj.transform.SetParent(anchorsRoot.transform, false);

            Vector3 spawn = FindClearSpot(want);
            Vector3 facing = world.TransformPoint(new Vector3(0.55f, 0f, 0f)) - spawn; // straight through the lobby and the inner door
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = world.TransformDirection(Vector3.forward);
            anchorObj.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing.normalized, Vector3.up));
            log?.AppendLine($"lobby anchor: spawn {spawn:F2} facing {facing.normalized:F2}");
            return anchorObj.transform;
        }

        private static Transform AddInsideAnchor(Transform world, StringBuilder log)
        {
            Transform group = world.Find("GAOffice_Anchors");
            GameObject a = new GameObject("Anchor_GAOffice_Inside");
            a.transform.SetParent(group != null ? group : world, false);
            Vector3 spawn = FindClearSpot(world.TransformPoint(new Vector3(0.55f, 0f, -5.8f))); // 1.2 m past the inner door, in the open north part
            Vector3 facing = world.TransformPoint(new Vector3(0.55f, 0f, 6f)) - spawn;          // down the office, toward the cubicles and cabins
            facing.y = 0f;
            a.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing.normalized, Vector3.up));
            log?.AppendLine($"office inside anchor (tour card spawn): {spawn:F2} facing {facing.normalized:F2}");
            return a.transform;
        }

        private static Vector3 FindClearSpot(Vector3 want)
        {
            Vector3 best = want; float bestD = float.MaxValue;
            for (float dx = -1.5f; dx <= 1.5f; dx += 0.1f)
                for (float dz = -1.5f; dz <= 1.5f; dz += 0.1f)
                {
                    Vector3 p = want + new Vector3(dx, 0f, dz);
                    float floorY = float.NaN; // top-most upward-facing shell surface below 1 m
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 2.5f, p.z), Vector3.down, 6f))
                        if (h.collider.name == "GO_Room_Shell" && h.normal.y > 0.5f && h.point.y < 1f && (float.IsNaN(floorY) || h.point.y > floorY)) floorY = h.point.y;
                    if (float.IsNaN(floorY)) continue;
                    Vector3 c = new Vector3(p.x, floorY + 0.03f, p.z);
                    if (Physics.CheckCapsule(c + Vector3.up * 0.4f, c + Vector3.up * 1.4f, 0.4f)) continue; // keep a 0.8 m clear width
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = c; }
                }
            return best;
        }

        // --- Report ----------------------------------------------------------------------------------------------
        private static void Report(GameObject root, StringBuilder log)
        {
            if (log == null) return;
            Bounds total = default; bool has = false; long tris = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!has) { total = r.bounds; has = true; } else total.Encapsulate(r.bounds);
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += mf.sharedMesh.GetIndexCount(i) / 3;
            }
            Vector3 s = total.size;
            Vector3 dev = new Vector3((s.x / ExpectedSize.x - 1f) * 100f, (s.y / ExpectedSize.y - 1f) * 100f, (s.z / ExpectedSize.z - 1f) * 100f);
            bool off = Mathf.Abs(dev.x) > 5f || Mathf.Abs(dev.y) > 5f || Mathf.Abs(dev.z) > 5f;
            log.AppendLine($"scale: root {root.transform.localScale} | bounds {s.x:F2} x {s.y:F2} x {s.z:F2} m (expected ~{ExpectedSize.x} x {ExpectedSize.y} x {ExpectedSize.z}; deviation {dev.x:+0.0;-0.0}% / {dev.y:+0.0;-0.0}% / {dev.z:+0.0;-0.0}%)" +
                           (off ? "  ** OFF BY MORE THAN 5% - NOT RESCALED **" : "  OK"));
            log.AppendLine($"bounds centre {total.center:F2}, min y {total.min.y:F2}, max y {total.max.y:F2}; triangles={tris}");
            log.AppendLine($"colliders total: Mesh={root.GetComponentsInChildren<MeshCollider>(true).Length}, Box={root.GetComponentsInChildren<BoxCollider>(true).Length}; " +
                           $"lights={root.GetComponentsInChildren<Light>(true).Length}");
        }
    }
}
