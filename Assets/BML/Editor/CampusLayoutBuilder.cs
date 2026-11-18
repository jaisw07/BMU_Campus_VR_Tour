using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BML.CampusLayout
{
    /// <summary>
    /// Builds the BML campus (ground, roads, footpaths, grass, paved areas, trees, buildings) from
    /// Assets/BML/Layout/campus_layout.json. Menu: BML/Build Campus. Safe to re-run: the BML_Campus root is rebuilt.
    /// World: x = east, z = north, y = up, metres, origin = GA Building centre.
    /// </summary>
    public static class CampusLayoutBuilder
    {
        const string JsonPath = "Assets/BML/Layout/campus_layout.json";
        const string ScenePath = "Assets/BML/Campus/BML_Campus.unity";
        const string MatDir = "Assets/BML/Campus/Materials";

        static readonly StringBuilder Report = new StringBuilder();
        static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        struct Footprint { public Vector2 c; public float yawRad; public float lng, shr; }
        struct Strip { public List<Vector2> pts; public float halfWidth; }

        static readonly List<Footprint> Footprints = new List<Footprint>();
        static readonly List<Strip> Roads = new List<Strip>();
        static readonly List<Strip> Paths = new List<Strip>();
        static readonly List<List<Vector2>> PavedPolys = new List<List<Vector2>>();
        static readonly List<List<Vector2>> ContextPolys = new List<List<Vector2>>();

        [MenuItem("BML/Build Campus")]
        public static void Build()
        {
            Report.Clear(); MatCache.Clear(); Footprints.Clear(); Roads.Clear(); Paths.Clear(); PavedPolys.Clear(); ContextPolys.Clear();
            if (!File.Exists(JsonPath)) { Debug.LogError("Missing " + JsonPath); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(MatDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var data = JObject.Parse(File.ReadAllText(JsonPath));

            var root = new GameObject("BML_Campus").transform;
            Transform Child(string n) { var t = new GameObject(n).transform; t.SetParent(root, false); return t; }
            Transform ground = Child("Ground"), roads = Child("Roads"), foot = Child("Footpaths"), grass = Child("Grass"),
                      paved = Child("Paved"), trees = Child("Trees"), blds = Child("Buildings"), ctx = Child("ContextBuildings");

            // Pre-read geometry used for exclusion tests
            foreach (var r in data["roads"]) Roads.Add(new Strip { pts = Pts(r["pts"]), halfWidth = (float)r["width"] * 0.5f });
            foreach (var r in data["footpaths"]) Paths.Add(new Strip { pts = Pts(r["pts"]), halfWidth = (float)r["width"] * 0.5f });
            foreach (var p in data["paved"]) PavedPolys.Add(Pts(p["poly"]));
            foreach (var p in data["context_buildings"]) ContextPolys.Add(Pts(p["poly"]));

            BuildGround(ground);
            foreach (var r in data["roads"]) BuildRoad(roads, r);
            foreach (var r in data["footpaths"]) BuildFootpath(foot, r);
            foreach (var g in data["grass"]) BuildPoly(grass, (string)g["name"], Pts(g["poly"]), 0.01f, GrassMat((string)g["name"]));
            foreach (var p in data["paved"])
            {
                string n = (string)p["name"];
                bool dark = n.Contains("bus parking") || n.Contains("Solar car park");
                BuildPoly(paved, n, Pts(p["poly"]), 0.02f, Mat("Paved" + (dark ? "Dark" : ""), dark ? "#B8AC95" : "#CFC3AB", 0.15f));
            }
            foreach (var b in data["buildings"]) BuildBuilding(blds, b);
            foreach (var p in data["context_buildings"]) BuildContext(ctx, p);
            BuildTrees(trees, data["trees"]);
            BuildLighting();
            BuildPlayer();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            int tris = 0, meshes = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) { tris += mf.sharedMesh.triangles.Length / 3; meshes++; }
            Report.AppendLine($"\nCounts: roads {((JArray)data["roads"]).Count}, footpaths {((JArray)data["footpaths"]).Count}, grass {((JArray)data["grass"]).Count}, paved {((JArray)data["paved"]).Count}, tree areas {((JArray)data["trees"]).Count}, context blocks {((JArray)data["context_buildings"]).Count}");
            Report.AppendLine($"Mesh filters {meshes}, total triangles {tris}");
            Debug.Log("[BML] Build report:\n" + Report);
            File.WriteAllText("Assets/BML/Campus/build_report.txt", Report.ToString());
        }

        // ------------------------------------------------------------------ helpers
        static List<Vector2> Pts(JToken arr)
        {
            var l = new List<Vector2>();
            foreach (var p in arr) l.Add(new Vector2((float)p[0], (float)p[1]));
            return l;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static Material Mat(string name, string hex, float smooth)
        {
            if (MatCache.TryGetValue(name, out var m)) return m;
            string path = $"{MatDir}/BML_{name}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.color = Hex(hex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Hex(hex));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            MatCache[name] = m;
            return m;
        }

        static Material GrassMat(string n)
        {
            switch (n)
            {
                case "Playground": return Mat("Playground", "#8FB05A", 0.05f);
                case "Tennis courts": return Mat("Tennis", "#4F9AC8", 0.2f);
                case "Sandy ground (dry)": return Mat("Sand", "#D8C58A", 0.05f);
                case "Terrace garden (formal)": return Mat("Terrace", "#6FA85A", 0.05f);
                default: return Mat("Lawn", "#8FC46A", 0.05f);
            }
        }

        // Mesh builder that always emits upward-facing triangles
        class MB
        {
            public List<Vector3> v = new List<Vector3>(); public List<int> t = new List<int>(); public List<Vector2> uv = new List<Vector2>();
            public int V(float x, float y, float z) { v.Add(new Vector3(x, y, z)); uv.Add(new Vector2(x * 0.1f, z * 0.1f)); return v.Count - 1; }
            public void Tri(int a, int b, int c)
            {
                var cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (cr.y >= 0) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
            }
            public Mesh ToMesh(string n)
            {
                var m = new Mesh { name = n, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        static GameObject MeshObj(Transform parent, string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        // Joined strip: quad per segment + round join (fan) at each vertex
        static void AddRibbon(MB mb, List<Vector2> pts, float hw, float y)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], d = (b - a);
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize(); Vector2 n = new Vector2(-d.y, d.x) * hw;
                int i0 = mb.V(a.x + n.x, y, a.y + n.y), i1 = mb.V(a.x - n.x, y, a.y - n.y),
                    i2 = mb.V(b.x - n.x, y, b.y - n.y), i3 = mb.V(b.x + n.x, y, b.y + n.y);
                mb.Tri(i0, i1, i2); mb.Tri(i0, i2, i3);
            }
            const int seg = 10;
            foreach (var p in pts)
            {
                int c = mb.V(p.x, y, p.y), prev = -1, first = -1;
                for (int s = 0; s <= seg; s++)
                {
                    float ang = s * Mathf.PI * 2f / seg;
                    int k = mb.V(p.x + Mathf.Cos(ang) * hw, y, p.y + Mathf.Sin(ang) * hw);
                    if (prev >= 0) mb.Tri(c, prev, k); else first = k;
                    prev = k;
                }
            }
        }

        static void AddDashes(MB mb, List<Vector2> pts, float y)
        {
            const float dash = 3f, gap = 3f, hw = 0.075f;
            float carry = 0f; bool on = true;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1]; float len = Vector2.Distance(a, b);
                if (len < 1e-4f) continue;
                Vector2 d = (b - a) / len, n = new Vector2(-d.y, d.x) * hw;
                float pos = 0f;
                while (pos < len)
                {
                    float rem = (on ? dash : gap) - carry, step = Mathf.Min(rem, len - pos);
                    if (on)
                    {
                        Vector2 s = a + d * pos, e = a + d * (pos + step);
                        int i0 = mb.V(s.x + n.x, y, s.y + n.y), i1 = mb.V(s.x - n.x, y, s.y - n.y), i2 = mb.V(e.x - n.x, y, e.y - n.y), i3 = mb.V(e.x + n.x, y, e.y + n.y);
                        mb.Tri(i0, i1, i2); mb.Tri(i0, i2, i3);
                    }
                    pos += step; carry += step;
                    if (carry >= (on ? dash : gap) - 1e-4f) { on = !on; carry = 0f; }
                }
            }
        }

        static void BuildGround(Transform parent)
        {
            var mb = new MB(); float x0 = -270f, x1 = 150f, z0 = -120f, z1 = 120f;
            int a = mb.V(x0, 0, z0), b = mb.V(x0, 0, z1), c = mb.V(x1, 0, z1), d = mb.V(x1, 0, z0);
            mb.Tri(a, b, c); mb.Tri(a, c, d);
            MeshObj(parent, "Ground", mb.ToMesh("Ground"), Mat("Ground", "#D9CBA0", 0.05f), true);
        }

        static void BuildRoad(Transform parent, JToken r)
        {
            string n = (string)r["name"]; bool ctx = (bool)r["context"]; float w = (float)r["width"];
            var pts = Pts(r["pts"]);
            var mb = new MB(); AddRibbon(mb, pts, w * 0.5f, 0.03f);
            var go = MeshObj(parent, (ctx ? "Context_" : "") + n, mb.ToMesh(n), Mat("Asphalt", "#4A4A4A", 0.2f), false);
            if (w >= 5f)
            {
                var lb = new MB(); AddDashes(lb, pts, 0.035f);
                MeshObj(go.transform, "CentreLine", lb.ToMesh(n + "_line"), Mat("RoadLine", "#FFFFFF", 0.1f), false);
            }
        }

        static void BuildFootpath(Transform parent, JToken r)
        {
            var mb = new MB(); AddRibbon(mb, Pts(r["pts"]), (float)r["width"] * 0.5f, 0.04f);
            MeshObj(parent, (string)r["name"], mb.ToMesh((string)r["name"]), Mat("Footpath", "#EFE4CC", 0.1f), false);
        }

        // ------------------------------------------------------------------ polygons
        static float Area(List<Vector2> p)
        {
            float a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        static List<int> EarClip(List<Vector2> poly)
        {
            var idx = new List<int>(); for (int i = 0; i < poly.Count; i++) idx.Add(i);
            if (Area(poly) < 0) idx.Reverse(); // make CCW
            var tris = new List<int>(); int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ip = idx[(i + idx.Count - 1) % idx.Count], ic = idx[i], inx = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ip], b = poly[ic], c = poly[inx];
                    if (Sign(a, b, c) >= 0 && ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) <= 0) continue; // reflex
                    if (((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) <= 0) continue;
                    bool ok = true;
                    foreach (int j in idx) { if (j == ip || j == ic || j == inx) continue; if (InTri(poly[j], a, b, c)) { ok = false; break; } }
                    if (!ok) continue;
                    tris.Add(ip); tris.Add(ic); tris.Add(inx); idx.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        static void BuildPoly(Transform parent, string name, List<Vector2> poly, float y, Material mat)
        {
            var mb = new MB(); foreach (var p in poly) mb.V(p.x, y, p.y);
            var tris = EarClip(poly);
            for (int i = 0; i < tris.Count; i += 3) mb.Tri(tris[i], tris[i + 1], tris[i + 2]);
            MeshObj(parent, name, mb.ToMesh(name), mat, false);
        }

        static void BuildContext(Transform parent, JToken p)
        {
            var poly = Pts(p["poly"]); string name = "Context_" + (string)p["name"];
            const float h = 14f;
            var tris = EarClip(poly);
            var mesh = new Mesh { name = name };
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var q in poly) v.Add(new Vector3(q.x, h, q.y));
            for (int i = 0; i < tris.Count; i += 3) { t.Add(tris[i]); t.Add(tris[i + 2]); t.Add(tris[i + 1]); } // roof facing up (CCW in x,z => flip)
            var roofCheck = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]); if (roofCheck.y < 0) for (int i = 0; i < t.Count; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            bool ccw = Area(poly) > 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                int k = v.Count; v.Add(new Vector3(a.x, 0, a.y)); v.Add(new Vector3(b.x, 0, b.y)); v.Add(new Vector3(b.x, h, b.y)); v.Add(new Vector3(a.x, h, a.y));
                if (ccw) { t.AddRange(new[] { k, k + 3, k + 2, k, k + 2, k + 1 }); } else { t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 }); }
            }
            mesh.indexFormat = IndexFormat.UInt32; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            MeshObj(parent, name, mesh, Mat("Context", "#BDBDBD", 0.1f), true);
        }

        // ------------------------------------------------------------------ buildings
        static GameObject FindModel(string file)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            foreach (var g in AssetDatabase.FindAssets(stem))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileName(p).Equals(file, StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static void BuildBuilding(Transform parent, JToken b)
        {
            string id = (string)b["id"], name = (string)b["name"], file = b["file"].Type == JTokenType.Null ? null : (string)b["file"];
            var c = new Vector2((float)b["centre"][0], (float)b["centre"][1]);
            float lng = (float)b["long_short_m"][0], shr = (float)b["long_short_m"][1], tDeg = (float)b["long_axis_deg"];
            string rootName = $"BML_{id}_{name.Replace(' ', '_')}";
            var wrapper = new GameObject(rootName).transform; wrapper.SetParent(parent, false);
            wrapper.position = new Vector3(c.x, 0, c.y);

            GameObject model = file != null ? FindModel(file) : null;
            bool longIsX = true; float f = 1f; string note = "";
            if (model != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model); inst.transform.SetParent(wrapper, false);
                inst.transform.localScale = Vector3.one * 0.5f;
                var rs = inst.GetComponentsInChildren<Renderer>();
                var bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
                inst.transform.position -= new Vector3(bounds.center.x - wrapper.position.x, bounds.min.y, bounds.center.z - wrapper.position.z);
                float sx = bounds.size.x, sz = bounds.size.z;
                longIsX = sx >= sz; float w = Mathf.Max(sx, sz), d = Mathf.Min(sx, sz);
                f = Mathf.Min(lng / w, shr / d);
                float clamped = Mathf.Clamp(f, 0.5f, 2.0f);
                if (!Mathf.Approximately(clamped, f)) note = $" WARNING fit {f:F2} clamped to {clamped:F2}";
                f = clamped;
                wrapper.localScale = Vector3.one * f;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.gameObject.name.ToLower().Contains("sign")) continue;
                    if (mf.GetComponent<MeshCollider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                Report.AppendLine($"{rootName}: model {file} raw {w:F1}x{d:F1} m (after 0.5), footprint {lng:F1}x{shr:F1}, f={f:F3}{note}");
            }
            else
            {
                bool ga = id == "1"; float h = ga ? 22f : 12f;
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = ga ? "PLACEHOLDER_GA" : "PLACEHOLDER_Mechanical";
                box.transform.SetParent(wrapper, false); box.transform.localScale = new Vector3(lng, h, shr); box.transform.localPosition = new Vector3(0, h * 0.5f, 0);
                box.GetComponent<MeshRenderer>().sharedMaterial = Mat("Placeholder", "#E4DFD3", 0.1f);
                var lbl = new GameObject("Label"); lbl.transform.SetParent(wrapper, false); lbl.transform.localPosition = new Vector3(0, h + 5f, 0);
                var tm = lbl.AddComponent<TextMesh>(); tm.text = name + " (placeholder)"; tm.fontSize = 64; tm.characterSize = 0.4f; tm.anchor = TextAnchor.MiddleCenter; tm.color = Color.black;
                lbl.AddComponent<BillboardLabel>();
                if (file != null) Report.AppendLine($"WARNING {rootName}: model {file} not found, using placeholder");
                else Report.AppendLine($"{rootName}: placeholder {lng:F1}x{shr:F1}x{h:F0} m");
            }

            // yaw: long axis along (cos t, sin t) in (x,z); pick the 180-degree flip whose front faces the nearest road
            float yaw = longIsX ? -tDeg : 90f - tDeg;
            Vector2 target = NearestRoadPoint(c);
            Vector3 toRoad = new Vector3(target.x - c.x, 0, target.y - c.y).normalized;
            float best = float.MinValue, bestYaw = yaw;
            foreach (float cand in new[] { yaw, yaw + 180f })
            {
                var q = Quaternion.Euler(0, cand, 0);
                Vector3 front = longIsX ? q * Vector3.forward : q * Vector3.right;
                float s = Vector3.Dot(front, toRoad); if (s > best) { best = s; bestYaw = cand; }
            }
            wrapper.rotation = Quaternion.Euler(0, Mathf.Repeat(bestYaw, 360f), 0);
            Footprints.Add(new Footprint { c = c, yawRad = tDeg * Mathf.Deg2Rad, lng = lng, shr = shr });

            var rend = wrapper.GetComponentsInChildren<Renderer>();
            string bnd = "";
            if (rend.Length > 0) { var bb = rend[0].bounds; foreach (var r in rend) bb.Encapsulate(r.bounds); bnd = $" bounds centre {bb.center:F1} size {bb.size:F1}"; }
            Report.AppendLine($"   yaw={wrapper.eulerAngles.y:F1} (flip by +180 to turn the front){bnd}");
        }

        static Vector2 NearestRoadPoint(Vector2 p)
        {
            float best = float.MaxValue; Vector2 bp = p;
            foreach (var r in Roads)
                for (int i = 0; i < r.pts.Count - 1; i++)
                {
                    var q = ClosestOnSeg(p, r.pts[i], r.pts[i + 1]); float d = (q - p).sqrMagnitude;
                    if (d < best) { best = d; bp = q; }
                }
            return bp;
        }

        static Vector2 ClosestOnSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude; if (l < 1e-8f) return a;
            return a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / l);
        }

        static float DistToStrip(Vector2 p, Strip s)
        {
            float best = float.MaxValue;
            for (int i = 0; i < s.pts.Count - 1; i++) best = Mathf.Min(best, Vector2.Distance(p, ClosestOnSeg(p, s.pts[i], s.pts[i + 1])));
            return best;
        }

        static bool InPoly(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        static bool InFootprint(Vector2 p, float margin)
        {
            foreach (var f in Footprints)
            {
                Vector2 d = p - f.c; float cs = Mathf.Cos(f.yawRad), sn = Mathf.Sin(f.yawRad);
                float u = d.x * cs + d.y * sn, v = -d.x * sn + d.y * cs; // along long axis, along short axis
                if (Mathf.Abs(u) < f.lng * 0.5f + margin && Mathf.Abs(v) < f.shr * 0.5f + margin) return true;
            }
            return false;
        }

        static bool Blocked(Vector2 p)
        {
            if (InFootprint(p, 2f)) return true;
            foreach (var r in Roads) if (DistToStrip(p, r) < r.halfWidth + 1.5f) return true;
            foreach (var r in Paths) if (DistToStrip(p, r) < r.halfWidth + 1f) return true;
            foreach (var pv in PavedPolys) if (InPoly(p, pv)) return true;
            foreach (var cp in ContextPolys) if (InPoly(p, cp)) return true;
            return false;
        }

        // ------------------------------------------------------------------ trees
        class TreeBatch { public List<CombineInstance> trunk = new List<CombineInstance>(); public List<CombineInstance>[] canopy = { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() }; public int count; }

        static void BuildTrees(Transform parent, JToken areas)
        {
            var rng = new System.Random(42);
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            var mats = new[] { Mat("TreeTrunk", "#5B4A36", 0.1f), Mat("TreeA", "#2F7D32", 0.1f), Mat("TreeB", "#378638", 0.1f), Mat("TreeC", "#3F8F3A", 0.1f) };
            int total = 0;
            foreach (var area in areas)
            {
                string name = (string)area["name"]; var poly = Pts(area["poly"]);
                float ar = Mathf.Abs(Area(poly)), per = 0; for (int i = 0; i < poly.Count; i++) per += Vector2.Distance(poly[i], poly[(i + 1) % poly.Count]);
                bool narrow = (2f * ar / per) < 8f; float step = narrow ? 4f : 6f, jit = narrow ? 0.4f : 0.35f;
                float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
                foreach (var p in poly) { minx = Mathf.Min(minx, p.x); maxx = Mathf.Max(maxx, p.x); minz = Mathf.Min(minz, p.y); maxz = Mathf.Max(maxz, p.y); }
                var batch = new TreeBatch();
                for (float x = minx + step * 0.5f; x < maxx; x += step)
                    for (float z = minz + step * 0.5f; z < maxz; z += step)
                    {
                        var p = new Vector2(x + ((float)rng.NextDouble() - 0.5f) * 2f * step * jit, z + ((float)rng.NextDouble() - 0.5f) * 2f * step * jit);
                        if (!InPoly(p, poly) || Blocked(p)) continue;
                        AddTree(batch, rng, p, sphere, cyl); batch.count++;
                    }
                if (batch.count == 0) continue;
                total += batch.count;
                var mesh = new Mesh { name = "Trees_" + name, indexFormat = IndexFormat.UInt32 };
                var parts = new List<Mesh>(); var sub = new List<Material>();
                void Part(List<CombineInstance> l, Material m) { if (l.Count == 0) return; var pm = new Mesh { indexFormat = IndexFormat.UInt32 }; pm.CombineMeshes(l.ToArray(), true, true); parts.Add(pm); sub.Add(m); }
                Part(batch.trunk, mats[0]); for (int i = 0; i < 3; i++) Part(batch.canopy[i], mats[i + 1]);
                var ci = new CombineInstance[parts.Count]; for (int i = 0; i < parts.Count; i++) ci[i] = new CombineInstance { mesh = parts[i], transform = Matrix4x4.identity };
                mesh.CombineMeshes(ci, false, false);
                var go = new GameObject("Trees_" + name); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = sub.ToArray();
                foreach (var pm in parts) UnityEngine.Object.DestroyImmediate(pm);
            }
            Report.AppendLine($"Trees placed: {total} (seed 42)");
        }

        static void AddTree(TreeBatch b, System.Random rng, Vector2 p, Mesh sphere, Mesh cyl)
        {
            float s = Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble()), yaw = (float)rng.NextDouble() * 360f;
            int variant = rng.Next(4), col = rng.Next(3);
            float trunkH = (variant == 3 ? 3.5f : 2.5f) * s;
            Matrix4x4 root = Matrix4x4.TRS(new Vector3(p.x, 0, p.y), Quaternion.Euler(0, yaw, 0), Vector3.one * s);
            b.trunk.Add(new CombineInstance { mesh = cyl, transform = root * Matrix4x4.TRS(new Vector3(0, trunkH * 0.5f / s, 0), Quaternion.identity, new Vector3(0.35f, trunkH * 0.5f / s, 0.35f)) });
            void Blob(Vector3 pos, Vector3 sc) => b.canopy[col].Add(new CombineInstance { mesh = sphere, transform = root * Matrix4x4.TRS(pos, Quaternion.identity, sc) });
            float baseY = trunkH / s;
            switch (variant)
            {
                case 0: Blob(new Vector3(0, baseY + 1.8f, 0), new Vector3(4.2f, 3.4f, 4.2f)); break;
                case 1: Blob(new Vector3(0, baseY + 1.5f, 0), new Vector3(4.6f, 3.0f, 4.6f)); Blob(new Vector3(0.9f, baseY + 2.6f, 0.4f), new Vector3(3.0f, 2.6f, 3.0f)); break;
                case 2: Blob(new Vector3(0, baseY + 1.6f, 0), new Vector3(4.0f, 3.2f, 4.0f)); Blob(new Vector3(-1f, baseY + 2.4f, -0.5f), new Vector3(3.2f, 2.8f, 3.2f)); Blob(new Vector3(0.8f, baseY + 2.9f, 0.7f), new Vector3(2.6f, 2.4f, 2.6f)); break;
                default: Blob(new Vector3(0, baseY + 2.4f, 0), new Vector3(3.2f, 4.6f, 3.2f)); Blob(new Vector3(0, baseY + 1.2f, 0), new Vector3(4.0f, 2.6f, 4.0f)); break;
            }
        }

        // ------------------------------------------------------------------ lighting & player
        static void BuildLighting()
        {
            var lg = new GameObject("Sun"); var l = lg.AddComponent<Light>();
            l.type = LightType.Directional; l.color = new Color(1f, 0.93f, 0.8f); l.intensity = 1.3f; l.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            string skyPath = MatDir + "/BML_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
            RenderSettings.skybox = sky; RenderSettings.sun = l; RenderSettings.ambientMode = AmbientMode.Skybox;
            var pr = new GameObject("ReflectionProbe"); pr.transform.position = new Vector3(-60, 20, 0);
            var rp = pr.AddComponent<ReflectionProbe>(); rp.mode = ReflectionProbeMode.Baked; rp.size = new Vector3(420, 40, 240);
        }

        static void BuildPlayer()
        {
            var p = new GameObject("Player"); p.transform.position = new Vector3(-59f, 0.05f, 5f);
            var cc = p.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0);
            var cam = new GameObject("Main Camera"); cam.tag = "MainCamera"; cam.transform.SetParent(p.transform, false); cam.transform.localPosition = new Vector3(0, 1.7f, 0);
            var c = cam.AddComponent<Camera>(); c.farClipPlane = 800f; cam.AddComponent<AudioListener>();
            p.AddComponent<CampusWalker>();
        }
    }
}
