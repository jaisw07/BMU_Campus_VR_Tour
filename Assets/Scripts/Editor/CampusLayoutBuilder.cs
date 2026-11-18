using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Generates the campus ground, farmland, roads, paths, curbs and tree belts for Scene_Campus_Exterior, and hides the
    /// legacy tiled lawn / road meshes and the ~1M-triangle grass-blade squares baked into Campus_Exterior.glb.
    /// Called from BMUTourSceneBuilder.BuildExteriorScene so a scene rebuild reproduces everything.
    ///
    /// Layout (calibrated on aerial_topdown.png): image north = world -X, image east = world +Z. The North Block
    /// (about 219 x 130 m, centre (-480, -59)) and the Mechanical Workshop stay where they were; the hostels are placed
    /// relative to them (see HostelPivot constants). World units are metres.
    /// Textures, materials and meshes are generated into Assets/Textures|Materials|Meshes/Campus (committed, regenerated if missing).
    /// </summary>
    public static class CampusLayoutBuilder
    {
        const string TexDir = "Assets/Textures/Campus";
        const string MatDir = "Assets/Materials/Campus";
        const string MeshDir = "Assets/Meshes/Campus";

        // Hostel model pivots. Footprint centres: Vikram Sarabhai (-769.5, -259), Gargi (-739, -79).
        // Gaps (edge to edge): hostels - NB 87 m, Vikram - Gargi 107 m, hostels - Mechanical 99 m.
        public static readonly Vector3 VikramPivot = new Vector3(-769.5f, 0f, -270.3f);
        public static readonly Vector3 GargiPivot = new Vector3(-739.0f, 0f, -69.25f);

        public static string LastReport = "";

        // ------------------------------------------------------------------ footprints (world XZ, measured from renderer bounds)
        struct Box
        {
            public float x0, x1, z0, z1;
            public Box(float x0, float x1, float z0, float z1) { this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; }
            public bool Contains(float x, float z, float m) { return x > x0 - m && x < x1 + m && z > z0 - m && z < z1 + m; }
        }

        static readonly Box NorthBlock = new Box(-590.2f, -371.0f, -120.0f, 9.6f);
        static readonly Box Mechanical = new Box(-577.9f, -239.0f, -370.6f, -285.5f);
        static readonly Box Vikram = new Box(-862.0f, -677.0f, -296.0f, -222.0f);
        static readonly Box Gargi = new Box(-801.0f, -677.0f, -115.0f, -43.0f);
        static readonly Box[] Buildings =
        {
            NorthBlock, Mechanical, Vikram, Gargi,
            new Box(-222.3f, -126.5f, -157.7f, -17.0f),   // Gateway
            new Box(-228.0f, -72.0f, -255.0f, -185.0f),   // Library massing
            new Box(-588.0f, -144.0f, 32.0f, 182.0f),     // RMS massing
            new Box(-346.0f, -255.0f, -120.0f, -47.0f),   // MaggiPoint massing
            new Box(-776.0f, -654.0f, 108.0f, 174.0f),    // Faculty housing massing
        };
        // Central lawn between Mechanical and NB: keep it open.
        static readonly Box OpenLawn = new Box(-585f, -375f, -290f, -125f);
        // Teleport anchors on the exterior (xz): keep trees away. Keep in sync with BuildExteriorScene.
        static readonly Vector2[] KeepClear =
        {
            new Vector2(4f, 0f), new Vector2(-116.5f, -87.33f), new Vector2(-480.23f, -230f), new Vector2(-461.5f, -275f),
        };

        // ------------------------------------------------------------------ corridors (roads + paths), used by trees and validation
        struct Corridor { public Vector2 p; public float hw; }
        static List<Corridor> s_Corridors;

        // ================================================================== entry point
        public static void Build(GameObject envInstance)
        {
            var report = new System.Text.StringBuilder();
            s_Corridors = new List<Corridor>();
            EnsureFolder(TexDir); EnsureFolder(MatDir); EnsureFolder(MeshDir);

            int hidden = HideLegacyGround(envInstance);
            report.AppendLine("legacy ground/road/garden objects hidden: " + hidden);

            Materials mats = CreateMaterials();

            GameObject root = new GameObject("Campus_Ground_And_Roads");
            BuildRoads(root.transform, mats, report);

            GameObject treeRoot = new GameObject("Campus_Trees");
            var treePositions = new List<Vector2>();
            int trees = BuildTrees(treeRoot.transform, report, treePositions);
            report.AppendLine("trees: " + trees);

            BuildGround(root.transform, mats, treePositions);
            RealismRoads.Build(root.transform, report);

            LastReport = report.ToString();
            Debug.Log("[CampusLayoutBuilder] " + LastReport);
        }

        // ================================================================== legacy ground
        static readonly HashSet<string> LegacyNames = new HashSet<string>
        {
            "SidePath","MainRoadCloser","MainRoadFurther","MuddyRoad","InnerCurvedPath","MiddleSquare","ParkingLot",
            "ReceptionFrontPath","RoadAroundMaggiPoint","FrontLibLawn","GrassLawnLibFront","LibFrontLawn","LibFrontPath",
            "LibFrontPath2","FurtherInnerStraightRoad","MaggiPointPath","QCafeFrontLawn","NBFrontLawn","Plane","PondAreaRoad",
            "PondArea","NBFrontPath","InnerStraightPath","GrassLawn","NurbsPath","InnerMuddyRoadPath","TowardsGargi",
            "WorkshopFront","MiniGarden","GrassBlade_Combined","BaseMud","Tree",
        };

        static string BaseName(string n)
        {
            int dot = n.LastIndexOf('.');
            if (dot > 0)
            {
                bool digits = dot < n.Length - 1;
                for (int i = dot + 1; i < n.Length; i++) if (!char.IsDigit(n[i])) digits = false;
                if (digits) return n.Substring(0, dot);
            }
            return n;
        }

        static int HideLegacyGround(GameObject env)
        {
            int count = 0;
            foreach (Transform t in env.transform)
            {
                if (!LegacyNames.Contains(BaseName(t.name))) continue;
                if (t.GetComponent<MeshFilter>() == null && t.childCount == 0) continue;
                t.gameObject.SetActive(false);
                count++;
            }
            return count;
        }

        // ================================================================== materials (ambientCG CC0 PBR, see Assets/Art/ASSET_SOURCES.md)
        class Materials
        {
            public Material Asphalt, Path, Plaza, Curb, Dash, Farmland;
        }

        static Materials CreateMaterials()
        {
            foreach (string set in new[] { "Asphalt031", "PavingStones128", "Concrete034", "Concrete031", "Ground054" }) RealismTextures.ImportSet(set);
            var m = new Materials();
            m.Asphalt = RealismTextures.LitMaterial("M_Campus_Asphalt", "Asphalt031", new Color(0.58f, 0.58f, 0.60f), 0.85f);
            m.Path = RealismTextures.LitMaterial("M_Campus_Path", "PavingStones128", new Color(0.97f, 0.95f, 0.92f), 0.8f);
            m.Plaza = RealismTextures.LitMaterial("M_Campus_Plaza", "Concrete034", new Color(0.88f, 0.86f, 0.82f), 0.8f);
            m.Curb = RealismTextures.LitMaterial("M_Campus_Curb", "Concrete031", new Color(0.82f, 0.82f, 0.82f), 0.8f);
            m.Farmland = RealismTextures.LitMaterial("M_Campus_Farmland", "Ground054", new Color(0.92f, 0.84f, 0.70f), 0.7f);
            m.Dash = RealismTextures.PlainMaterial("M_Campus_RoadDash", new Color(0.88f, 0.88f, 0.84f), 0.2f);
            AssetDatabase.SaveAssets();
            return m;
        }

        // ================================================================== mesh helper
        class MeshData
        {
            public List<Vector3> v = new List<Vector3>();
            public List<Vector3> n = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();

            public int Vert(Vector3 p, Vector3 nrm, Vector2 u) { v.Add(p); n.Add(nrm); uv.Add(u); return v.Count - 1; }

            /// <summary>Adds a triangle wound so its front face looks toward 'want'.</summary>
            public void Tri(int a, int b, int c, Vector3 want)
            {
                Vector3 cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (Vector3.Dot(cr, want) < 0f) { int tmp = b; b = c; c = tmp; }
                t.Add(a); t.Add(b); t.Add(c);
            }

            public void Quad(int a, int b, int c, int d, Vector3 want) { Tri(a, b, c, want); Tri(c, b, d, want); }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = MeshDir + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject MakeMeshObject(string name, Transform parent, Mesh mesh, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            MarkStatic(go);
            return go;
        }

        static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            go.layer = 0;
        }

        // ================================================================== ground (Terrain) + farmland
        static void BuildGround(Transform parent, Materials mats, IList<Vector2> trees)
        {
            var corridors = new List<Vector3>();
            foreach (var c in s_Corridors) corridors.Add(new Vector3(c.p.x, c.p.y, c.hw));
            var buildings = new List<RealismTerrain.Footprint>();
            foreach (var b in Buildings) buildings.Add(new RealismTerrain.Footprint(b.x0, b.x1, b.z0, b.z1));
            RealismTerrain.Build(parent, corridors, buildings, new RealismTerrain.Footprint(OpenLawn.x0, OpenLawn.x1, OpenLawn.z0, OpenLawn.z1), trees);

            // Dry farmland everywhere beyond the terrain (east / north-east in the reference and out to the horizon).
            var dirt = GroundQuad("Campus_Farmland", -3500f, 2500f, -2500f, 2500f, -0.08f, 8f);
            MakeMeshObject("Campus_Farmland", parent, SaveMesh(dirt, "Campus_Farmland"), mats.Farmland, false);
        }

        static Mesh GroundQuad(string name, float x0, float x1, float z0, float z1, float y, float tile)
        {
            var md = new MeshData();
            Vector3 up = Vector3.up;
            int a = md.Vert(new Vector3(x0, y, z0), up, new Vector2(x0 / tile, z0 / tile));
            int b = md.Vert(new Vector3(x1, y, z0), up, new Vector2(x1 / tile, z0 / tile));
            int c = md.Vert(new Vector3(x0, y, z1), up, new Vector2(x0 / tile, z1 / tile));
            int d = md.Vert(new Vector3(x1, y, z1), up, new Vector2(x1 / tile, z1 / tile));
            md.Quad(a, b, c, d, up);
            return md.ToMesh(name);
        }

        // ================================================================== roads, paths, curbs
        static void BuildRoads(Transform parent, Materials mats, System.Text.StringBuilder report)
        {
            var asphalt = new MeshData();
            var paths = new MeshData();
            var plaza = new MeshData();
            var curbs = new MeshData();
            var dashes = new MeshData();

            // --- main asphalt road: along the EAST side of NB (z ~ 24), north-south in the aerial; starts at the front gate,
            //     passes the hostels and continues west (north in the aerial). Wider at the gate.
            Vector2[] mainPts =
            {
                new Vector2(30f, 2f), new Vector2(-20f, 2f), new Vector2(-70f, 3f), new Vector2(-120f, 12f),
                new Vector2(-170f, 23f), new Vector2(-300f, 24f), new Vector2(-960f, 24f),
            };
            float[] mainW = { 26f, 26f, 16f, 14f, 14f, 14f, 14f };
            Smooth(mainPts, mainW, 3f, out var mc, out var mw);
            AddStrip(asphalt, mc, mw, 0.010f, 4f);
            RealismRoads.MainCenter = mc; RealismRoads.MainWidth = mw; RealismRoads.Paths = new List<Vector2[]>();
            Register(mc, mw);
            AddDashes(dashes, mc, 0.022f, 3f, 6f, 0.15f, startAtX: -60f);
            AddCurbs(curbs, mc, mw, startAtX: -60f);

            // --- cross road on the north side of NB toward the east: joins the main road. Plus a paved plaza at its west end.
            Smooth(new[] { new Vector2(-620f, -45f), new Vector2(-620f, 16f) }, new[] { 10f, 10f }, 3f, out var cc, out var cw);
            AddStrip(asphalt, cc, cw, 0.014f, 4f);
            Register(cc, cw);
            AddRect(plaza, -650f, -596f, -80f, -45f, 0.018f, 4f);
            Register(RectSamples(-650f, -596f, -80f, -45f), 0f);

            // --- paved walkways, 3 m wide, light concrete
            float pathY = 0.020f; float step = 0.0005f; int idx = 0;
            // NB front door <-> Mechanical entrance across the central lawn
            AddPath(paths, new[] { new Vector2(-461.5f, -283f), new Vector2(-466f, -240f), new Vector2(-474f, -190f), new Vector2(-480f, -122.5f) }, 3f, pathY + step * idx++);
            // path along the south edge of the lawn
            AddPath(paths, new[] { new Vector2(-368f, -283f), new Vector2(-368f, -200f), new Vector2(-370f, -123f) }, 2.5f, pathY + step * idx++);
            // diagonal from NB's north end toward the hostels
            AddPath(paths, new[] { new Vector2(-598f, -124f), new Vector2(-650f, -148f), new Vector2(-700f, -168f) }, 3f, pathY + step * idx++);
            // hub -> Gargi entrance (front faces the gap, -Z side) and -> Vikram Sarabhai entrance (+Z side)
            AddPath(paths, new[] { new Vector2(-700f, -168f), new Vector2(-726f, -148f), new Vector2(-743f, -119f) }, 2.5f, pathY + step * idx++);
            AddPath(paths, new[] { new Vector2(-700f, -168f), new Vector2(-735f, -192f), new Vector2(-769.5f, -219f) }, 2.5f, pathY + step * idx++);
            // curved garden path in the belt north of NB
            AddPath(paths, new[]
            {
                new Vector2(-625f, -82f), new Vector2(-640f, -110f), new Vector2(-655f, -145f), new Vector2(-658f, -190f),
                new Vector2(-645f, -235f), new Vector2(-620f, -268f), new Vector2(-592f, -282f),
            }, 2.5f, pathY + step * idx++);
            // Gateway entrance <-> main road
            AddPath(paths, new[] { new Vector2(-124f, -87f), new Vector2(-114f, -55f), new Vector2(-106f, -4f) }, 3f, pathY + step * idx++);

            var roadsRoot = new GameObject("Roads_And_Paths");
            roadsRoot.transform.SetParent(parent, false);
            MakeMeshObject("Asphalt_Roads", roadsRoot.transform, SaveMesh(asphalt.ToMesh("Campus_Asphalt"), "Campus_Asphalt"), mats.Asphalt, false);
            MakeMeshObject("Road_CenterDashes", roadsRoot.transform, SaveMesh(dashes.ToMesh("Campus_Dashes"), "Campus_Dashes"), mats.Dash, false);
            MakeMeshObject("Road_Curbs", roadsRoot.transform, SaveMesh(curbs.ToMesh("Campus_Curbs"), "Campus_Curbs"), mats.Curb, false);
            MakeMeshObject("Walkways", roadsRoot.transform, SaveMesh(paths.ToMesh("Campus_Paths"), "Campus_Paths"), mats.Path, false);
            MakeMeshObject("Plaza", roadsRoot.transform, SaveMesh(plaza.ToMesh("Campus_Plaza"), "Campus_Plaza"), mats.Plaza, false);

            // validation: no road/path centre-line may pass through a building
            int warnings = 0;
            foreach (var s in s_Corridors)
                foreach (var b in Buildings)
                    if (b.Contains(s.p.x, s.p.y, s.hw * 0.8f)) { warnings++; Debug.LogWarning("[CampusLayoutBuilder] corridor sample " + s.p + " intersects a building footprint"); }
            report.AppendLine("road/path samples inside buildings: " + warnings);
            report.AppendLine("road/path triangles: " + ((asphalt.t.Count + paths.t.Count + plaza.t.Count + curbs.t.Count + dashes.t.Count) / 3));
        }

        static void AddPath(MeshData m, Vector2[] pts, float width, float y)
        {
            var w = new float[pts.Length];
            for (int i = 0; i < w.Length; i++) w[i] = width;
            Smooth(pts, w, 2.5f, out var c, out var ws);
            AddStrip(m, c, ws, y, 3f);
            RealismRoads.Paths.Add(pts);
            Register(c, ws);
        }

        static void Register(List<Vector2> pts, List<float> widths)
        {
            for (int i = 0; i < pts.Count; i++) s_Corridors.Add(new Corridor { p = pts[i], hw = widths[i] * 0.5f });
        }

        static void Register(List<Vector2> pts, float hw)
        {
            foreach (var p in pts) s_Corridors.Add(new Corridor { p = p, hw = hw });
        }

        static List<Vector2> RectSamples(float x0, float x1, float z0, float z1)
        {
            var l = new List<Vector2>();
            for (float x = x0; x <= x1; x += 4f) for (float z = z0; z <= z1; z += 4f) l.Add(new Vector2(x, z));
            return l;
        }

        // Catmull-Rom through the control points, resampled every ~step metres; widths are lerped.
        static void Smooth(Vector2[] p, float[] w, float step, out List<Vector2> pts, out List<float> ws)
        {
            pts = new List<Vector2>(); ws = new List<float>();
            for (int i = 0; i < p.Length - 1; i++)
            {
                Vector2 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Length - 1)];
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    Vector2 pos = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    pts.Add(pos); ws.Add(Mathf.Lerp(w[i], w[i + 1], t));
                }
            }
            pts.Add(p[p.Length - 1]); ws.Add(w[w.Length - 1]);
        }

        static Vector2 Left(List<Vector2> c, int i)
        {
            Vector2 t = i == 0 ? c[1] - c[0] : (i == c.Count - 1 ? c[i] - c[i - 1] : c[i + 1] - c[i - 1]);
            t.Normalize();
            return new Vector2(-t.y, t.x);
        }

        // Flat strip with UVs scaled by real length (v) and width (u) so the texture never stretches.
        static void AddStrip(MeshData m, List<Vector2> c, List<float> w, float y, float tile)
        {
            float s = 0f;
            int prevL = -1, prevR = -1;
            for (int i = 0; i < c.Count; i++)
            {
                if (i > 0) s += Vector2.Distance(c[i], c[i - 1]);
                Vector2 n = Left(c, i);
                float hw = w[i] * 0.5f;
                Vector2 l = c[i] + n * hw, r = c[i] - n * hw;
                int il = m.Vert(new Vector3(l.x, y, l.y), Vector3.up, new Vector2(hw / tile, s / tile));
                int ir = m.Vert(new Vector3(r.x, y, r.y), Vector3.up, new Vector2(-hw / tile, s / tile));
                if (prevL >= 0) m.Quad(prevL, prevR, il, ir, Vector3.up);
                prevL = il; prevR = ir;
            }
        }

        static void AddRect(MeshData m, float x0, float x1, float z0, float z1, float y, float tile)
        {
            int a = m.Vert(new Vector3(x0, y, z0), Vector3.up, new Vector2(x0 / tile, z0 / tile));
            int b = m.Vert(new Vector3(x1, y, z0), Vector3.up, new Vector2(x1 / tile, z0 / tile));
            int c = m.Vert(new Vector3(x0, y, z1), Vector3.up, new Vector2(x0 / tile, z1 / tile));
            int d = m.Vert(new Vector3(x1, y, z1), Vector3.up, new Vector2(x1 / tile, z1 / tile));
            m.Quad(a, b, c, d, Vector3.up);
        }

        // Raised curb / sidewalk strips (1.6 m wide, 12 cm high) on both edges of the road, from startAtX onward (-X direction).
        static void AddCurbs(MeshData m, List<Vector2> c, List<float> w, float startAtX)
        {
            const float curbW = 1.6f, curbH = 0.14f, tile = 3f;
            for (int side = -1; side <= 1; side += 2)
            {
                float s = 0f;
                int pTopIn = -1, pTopOut = -1, pInTop = -1, pInBot = -1, pOutTop = -1, pOutBot = -1;
                for (int i = 0; i < c.Count; i++)
                {
                    if (i > 0) s += Vector2.Distance(c[i], c[i - 1]);
                    if (c[i].x > startAtX) continue;
                    Vector2 n = Left(c, i) * side;
                    float hw = w[i] * 0.5f;
                    Vector2 inner = c[i] + n * hw, outer = c[i] + n * (hw + curbW);
                    Vector3 nOut3 = new Vector3(n.x, 0f, n.y);
                    int tIn = m.Vert(new Vector3(inner.x, curbH, inner.y), Vector3.up, new Vector2(0f, s / tile));
                    int tOut = m.Vert(new Vector3(outer.x, curbH, outer.y), Vector3.up, new Vector2(curbW / tile, s / tile));
                    int iTop = m.Vert(new Vector3(inner.x, curbH, inner.y), -nOut3, new Vector2(curbH / tile, s / tile));
                    int iBot = m.Vert(new Vector3(inner.x, 0.01f, inner.y), -nOut3, new Vector2(0f, s / tile));
                    int oTop = m.Vert(new Vector3(outer.x, curbH, outer.y), nOut3, new Vector2(curbH / tile, s / tile));
                    int oBot = m.Vert(new Vector3(outer.x, 0.01f, outer.y), nOut3, new Vector2(0f, s / tile));
                    if (pTopIn >= 0)
                    {
                        m.Quad(pTopIn, pTopOut, tIn, tOut, Vector3.up);
                        m.Quad(pInTop, pInBot, iTop, iBot, -nOut3);
                        m.Quad(pOutTop, pOutBot, oTop, oBot, nOut3);
                    }
                    pTopIn = tIn; pTopOut = tOut; pInTop = iTop; pInBot = iBot; pOutTop = oTop; pOutBot = oBot;
                }
            }
        }

        // Dashed centre line on the main road.
        static void AddDashes(MeshData m, List<Vector2> c, float y, float dash, float gap, float width, float startAtX)
        {
            var cum = new List<float> { 0f };
            for (int i = 1; i < c.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(c[i], c[i - 1]));
            float total = cum[cum.Count - 1];
            for (float s0 = 0f; s0 + dash < total; s0 += dash + gap)
            {
                Vector2 p0 = PointAt(c, cum, s0), p1 = PointAt(c, cum, s0 + dash);
                if (p0.x > startAtX) continue;
                Vector2 t = (p1 - p0).normalized;
                Vector2 n = new Vector2(-t.y, t.x) * width * 0.5f;
                int a = m.Vert(new Vector3(p0.x + n.x, y, p0.y + n.y), Vector3.up, Vector2.zero);
                int b = m.Vert(new Vector3(p0.x - n.x, y, p0.y - n.y), Vector3.up, Vector2.zero);
                int cc = m.Vert(new Vector3(p1.x + n.x, y, p1.y + n.y), Vector3.up, Vector2.zero);
                int d = m.Vert(new Vector3(p1.x - n.x, y, p1.y - n.y), Vector3.up, Vector2.zero);
                m.Quad(a, b, cc, d, Vector3.up);
            }
        }

        static Vector2 PointAt(List<Vector2> c, List<float> cum, float s)
        {
            for (int i = 1; i < c.Count; i++)
                if (s <= cum[i])
                {
                    float t = (s - cum[i - 1]) / Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                    return Vector2.Lerp(c[i - 1], c[i], t);
                }
            return c[c.Count - 1];
        }

        // ================================================================== trees (Polyhaven Jacaranda prefabs with LOD groups)
        struct TreeGroup
        {
            public string name; public Box area; public int count; public float spacing; public int seed;
            public TreeGroup(string name, Box area, int count, float spacing, int seed) { this.name = name; this.area = area; this.count = count; this.spacing = spacing; this.seed = seed; }
        }

        // species mix per cluster (Bloom = rare lilac-flowering tree, Dry = autumn-tinted, as in the reference photos)
        static readonly string[] VariantPool = { "Green", "Green", "Green", "Light", "Light", "Olive", "Olive", "Dry", "Bloom" };

        static int BuildTrees(Transform parent, System.Text.StringBuilder report, List<Vector2> placed)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RealismTrees.PrefabPath("Green")) == null)
            {
                Debug.LogWarning("[CampusLayoutBuilder] tree prefabs missing - run BMU VR Tour > Realism > Bake Jacaranda Trees");
                return 0;
            }
            var prefabs = new Dictionary<string, GameObject>();
            foreach (string v in RealismTrees.VariantNames) prefabs[v] = AssetDatabase.LoadAssetAtPath<GameObject>(RealismTrees.PrefabPath(v));

            var groups = new[]
            {
                new TreeGroup("Trees_South_of_Mechanical", new Box(-236f, -135f, -390f, -258f), 22, 9.5f, 11),
                new TreeGroup("Trees_Garden_Belt_NB_to_Hostels", new Box(-676f, -592f, -300f, 6f), 55, 8.5f, 12),
                new TreeGroup("Trees_Around_Hostels", new Box(-930f, -640f, -380f, -10f), 70, 9.5f, 13),
                new TreeGroup("Trees_Lawn_Edge_West", new Box(-600f, -575f, -300f, -125f), 9, 11f, 14),
                new TreeGroup("Trees_Lawn_Edge_South", new Box(-380f, -340f, -300f, -125f), 9, 11f, 15),
                new TreeGroup("Trees_East_Boundary", new Box(-880f, -150f, 175f, 205f), 28, 14f, 16),
            };
            int total = 0;
            foreach (var g in groups)
            {
                var go = new GameObject(g.name); go.transform.SetParent(parent, false);
                total += ScatterTrees(go.transform, g, prefabs, placed);
            }

            // Row of trees along the east side of NB, between the building and the main road.
            var row = new GameObject("Trees_NB_East_Row"); row.transform.SetParent(parent, false);
            var rnd = new System.Random(21);
            for (float x = -586f; x < -376f; x += 15f)
            {
                Vector2 p = new Vector2(x + (float)(rnd.NextDouble() - 0.5) * 4f, 14f + (float)(rnd.NextDouble() - 0.5) * 1.2f);
                if (!TreeAllowed(p, 2.5f, false, 2f)) continue;
                SpawnTree(row.transform, p, prefabs, rnd, 0.72f);
                placed.Add(p); total++;
            }
            return total;
        }

        static bool TreeAllowed(Vector2 p, float spacingToCorridor, bool respectLawn, float buildingMargin = 8f)
        {
            if (respectLawn && OpenLawn.Contains(p.x, p.y, 0f)) return false;
            foreach (var b in Buildings) if (b.Contains(p.x, p.y, buildingMargin)) return false;
            foreach (var a in KeepClear) if (Vector2.Distance(a, p) < 12f) return false;
            foreach (var c in s_Corridors) if (Vector2.Distance(c.p, p) < c.hw + spacingToCorridor) return false;
            return true;
        }

        static int ScatterTrees(Transform parent, TreeGroup g, Dictionary<string, GameObject> prefabs, List<Vector2> placed)
        {
            var rnd = new System.Random(g.seed);
            int made = 0;
            // dart throwing with a minimum spacing = Poisson-disk-like distribution
            for (int attempt = 0; attempt < g.count * 60 && made < g.count; attempt++)
            {
                Vector2 p = new Vector2(Mathf.Lerp(g.area.x0, g.area.x1, (float)rnd.NextDouble()), Mathf.Lerp(g.area.z0, g.area.z1, (float)rnd.NextDouble()));
                bool lawnRule = !g.name.Contains("Lawn_Edge");
                if (!TreeAllowed(p, 5f, lawnRule)) continue;
                bool tooClose = false;
                foreach (var q in placed) if (Vector2.Distance(p, q) < g.spacing) { tooClose = true; break; }
                if (tooClose) continue;
                SpawnTree(parent, p, prefabs, rnd, 0.78f);
                placed.Add(p); made++;
            }
            return made;
        }

        static void SpawnTree(Transform parent, Vector2 p, Dictionary<string, GameObject> prefabs, System.Random rnd, float baseScale)
        {
            string variant = VariantPool[rnd.Next(VariantPool.Length)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[variant]);
            go.transform.SetParent(parent, false);
            float s = baseScale * (0.8f + (float)rnd.NextDouble() * 0.4f);          // real height about 12-18 m (campus trees are 3-4 storeys tall in the aerials)
            go.transform.position = new Vector3(p.x, 0f, p.y);
            go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            go.transform.localScale = new Vector3(s * (0.92f + (float)rnd.NextDouble() * 0.16f), s, s * (0.92f + (float)rnd.NextDouble() * 0.16f));
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        // ================================================================== misc
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
