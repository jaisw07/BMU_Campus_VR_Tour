using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Road wear and street furniture: URP Decal Projectors (cracks, repair patches, tyre wear, edge dirt) along the main road,
    /// and simple lamp posts, bollards, benches and low walls. Decal textures and furniture meshes are generated in-project.
    /// Decals need the Decal renderer feature on the active URP renderer (added by RealismAtmosphere.SetupRenderer).
    /// </summary>
    public static class RealismRoads
    {
        const string DecalDir = "Assets/Art/Textures/Decals";
        static System.Random rng;
        static float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        // Centre line + widths of the main road (set by CampusLayoutBuilder.BuildRoads).
        public static List<Vector2> MainCenter; public static List<float> MainWidth;
        // Walkway polylines (set by CampusLayoutBuilder.BuildRoads) for lamp posts and benches.
        public static List<Vector2[]> Paths = new List<Vector2[]>();

        // ------------------------------------------------------------------ decal textures (RGBA, alpha = coverage)
        static void Line(Color[] px, int w, Vector2 a, Vector2 b, float width, float alpha)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 1.5f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                int r = Mathf.CeilToInt(width + 1f);
                for (int y = Mathf.Max(0, (int)p.y - r); y <= Mathf.Min(w - 1, (int)p.y + r); y++)
                    for (int x = Mathf.Max(0, (int)p.x - r); x <= Mathf.Min(w - 1, (int)p.x + r); x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), p);
                        float cov = Mathf.Clamp01(width - d + 0.5f) * alpha;
                        if (cov > px[y * w + x].a) px[y * w + x] = new Color(0.05f, 0.05f, 0.05f, cov);
                    }
            }
        }

        static void Crack(Color[] px, int w, Vector2 start, float angle, float len, float width, int depth)
        {
            Vector2 p = start; float a = angle;
            int segs = Mathf.Max(3, (int)(len / 14f));
            for (int i = 0; i < segs; i++)
            {
                a += R(-0.5f, 0.5f);
                Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(9f, 18f);
                Line(px, w, p, q, width * (1f - i / (float)segs * 0.6f), 0.85f);
                if (depth > 0 && rng.NextDouble() < 0.35) Crack(px, w, q, a + R(-1.1f, 1.1f), len * 0.45f, width * 0.7f, depth - 1);
                p = q;
            }
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 15; h *= 0x85ebca6bu; h ^= h >> 13; h *= 0xc2b2ae35u; h ^= h >> 16;
                return (h & 0xffffffu) / 16777215f;
            }
        }

        static float VNoise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), fx), Mathf.Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), fx), fy);
        }

        static Texture2D MakeDecal(string name, int w, int h, Func<int, int, Color> fn)
        {
            string path = DecalDir + "/" + name + ".png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = new Color[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = fn(x, y);
                t.SetPixels(px);
                File.WriteAllBytes(path, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material DecalMaterial(string name, Texture2D tex)
        {
            string path = DecalDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find("Shader Graphs/Decal");
            if (sh == null) { Debug.LogWarning("[RealismRoads] 'Shader Graphs/Decal' not found - decals skipped"); return null; }
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            foreach (string prop in new[] { "Base_Map", "_BaseMap", "_MainTex" }) if (mat.HasProperty(prop)) mat.SetTexture(prop, tex);
            if (mat.HasProperty("Normal_Blend")) mat.SetFloat("Normal_Blend", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ build
        public static void Build(Transform parent, System.Text.StringBuilder report)
        {
            rng = new System.Random(5);
            RealismTrees.EnsureFolder(DecalDir);
            var root = new GameObject("Road_Decals_And_Furniture"); root.transform.SetParent(parent, false);
            int decals = BuildDecals(root.transform);
            int props = BuildFurniture(root.transform);
            report.AppendLine("decals: " + decals + ", street furniture pieces: " + props);
        }

        static int BuildDecals(Transform parent)
        {
            if (MainCenter == null || MainCenter.Count < 2) return 0;
            const int n = 512;
            var crackTex = MakeDecal("Decal_Cracks", n, n, (x, y) => Color.clear);
            // cracks need procedural line drawing: regenerate with a buffer
            string crackPath = DecalDir + "/Decal_Cracks.png";
            AssetDatabase.DeleteAsset(crackPath);
            {
                var px = new Color[n * n];
                for (int i = 0; i < 3; i++) Crack(px, n, new Vector2(R(60, 450), R(60, 450)), R(0, 6.28f), R(120, 260), R(1.1f, 2f), 2);
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false); t.SetPixels(px);
                File.WriteAllBytes(crackPath, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(crackPath, ImportAssetOptions.ForceSynchronousImport);
                var ti = AssetImporter.GetAtPath(crackPath) as TextureImporter; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
                crackTex = AssetDatabase.LoadAssetAtPath<Texture2D>(crackPath);
            }
            var patchTex = MakeDecal("Decal_Patch", n, n, (x, y) =>
            {
                // rough-edged repair patch: darker, smoother asphalt
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float wob = (VNoise(u * 18f, v * 18f, 3) - 0.5f) * 0.08f;
                float a = Mathf.Clamp01((edge + wob - 0.06f) * 40f);
                float g = 0.07f + VNoise(u * 60f, v * 60f, 9) * 0.05f;
                return new Color(g, g, g, a * 0.75f);
            });
            var tyreTex = MakeDecal("Decal_TyreWear", 128, 512, (x, y) =>
            {
                // two worn wheel tracks along the length (v)
                float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 512f;
                float track = Mathf.Exp(-Mathf.Pow((u - 0.28f) / 0.09f, 2f)) + Mathf.Exp(-Mathf.Pow((u - 0.72f) / 0.09f, 2f));
                float a = track * (0.25f + 0.35f * VNoise(u * 12f, v * 40f, 4)) * Mathf.Clamp01(Mathf.Min(v, 1f - v) * 12f);
                return new Color(0.03f, 0.03f, 0.03f, Mathf.Clamp01(a));
            });
            var dirtTex = MakeDecal("Decal_EdgeDirt", 512, 128, (x, y) =>
            {
                // sandy dust gathered along the kerb: strongest at v = 0 (kerb side), fading out
                float u = (x + 0.5f) / 512f, v = (y + 0.5f) / 128f;
                float a = Mathf.Pow(1f - v, 1.8f) * (0.35f + 0.65f * VNoise(u * 45f, v * 6f, 6)) * Mathf.Clamp01(Mathf.Min(u, 1f - u) * 12f);
                return new Color(0.50f, 0.42f, 0.30f, Mathf.Clamp01(a * 0.7f));
            });
            Material mCrack = DecalMaterial("MD_Cracks", crackTex), mPatch = DecalMaterial("MD_Patch", patchTex),
                     mTyre = DecalMaterial("MD_TyreWear", tyreTex), mDirt = DecalMaterial("MD_EdgeDirt", dirtTex);
            if (mCrack == null) return 0;

            // arc-length parameterisation of the main road (only the stretch past the gate)
            var cum = new List<float> { 0f };
            for (int i = 1; i < MainCenter.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(MainCenter[i], MainCenter[i - 1]));
            Func<float, Vector2> at = s => { for (int i = 1; i < MainCenter.Count; i++) if (s <= cum[i]) return Vector2.Lerp(MainCenter[i - 1], MainCenter[i], (s - cum[i - 1]) / Mathf.Max(1e-3f, cum[i] - cum[i - 1])); return MainCenter[MainCenter.Count - 1]; };
            Func<float, float> widthAt = s => { for (int i = 1; i < MainCenter.Count; i++) if (s <= cum[i]) return Mathf.Lerp(MainWidth[i - 1], MainWidth[i], (s - cum[i - 1]) / Mathf.Max(1e-3f, cum[i] - cum[i - 1])); return MainWidth[MainWidth.Count - 1]; };
            float total = cum[cum.Count - 1];
            int count = 0;

            // tyre wear: long strips following the road
            for (float s = 60f; s < total - 30f; s += 32f)
            {
                Vector2 a = at(s), b = at(s + 16f), c = (a + b) * 0.5f; Vector2 d = (b - a).normalized;
                Place(parent, mTyre, new Vector3(c.x, 0.1f, c.y), Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, new Vector3(3.4f, 34f, 0.6f), "Decal_TyreWear", 90f);
                count++;
            }
            // edge dust along both kerbs
            for (float s = 60f; s < total - 30f; s += 20f)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 a = at(s), b = at(s + 10f), c = (a + b) * 0.5f; Vector2 d = (b - a).normalized; Vector2 nrm = new Vector2(-d.y, d.x) * side;
                    float hw = widthAt(s) * 0.5f;
                    Vector2 p = c + nrm * (hw - 0.55f);
                    // long axis along the road, thin axis across; v = 0 at the kerb edge -> rotate so +y points toward the kerb
                    float yaw = Mathf.Atan2(nrm.x, nrm.y) * Mathf.Rad2Deg;
                    Place(parent, mDirt, new Vector3(p.x, 0.1f, p.y), yaw + 180f, new Vector3(22f, 1.2f, 0.6f), "Decal_EdgeDust", 70f);
                    count++;
                }
            // cracks and repair patches at random places on the carriageway
            for (int i = 0; i < 70; i++)
            {
                float s = R(55f, total - 20f); Vector2 c = at(s); float hw = widthAt(s) * 0.5f;
                Vector2 a = at(s), b = at(s + 2f); Vector2 d = (b - a).normalized; Vector2 nrm = new Vector2(-d.y, d.x);
                Vector2 p = c + nrm * R(-hw + 1f, hw - 1f);
                Place(parent, mCrack, new Vector3(p.x, 0.1f, p.y), R(0f, 360f), Vector3.one * R(2.5f, 5f) + new Vector3(0, 0, 0.5f), "Decal_Cracks", 60f);
                count++;
            }
            for (int i = 0; i < 24; i++)
            {
                float s = R(60f, total - 20f); Vector2 c = at(s); float hw = widthAt(s) * 0.5f;
                Vector2 a = at(s), b = at(s + 2f); Vector2 d = (b - a).normalized; Vector2 nrm = new Vector2(-d.y, d.x);
                Vector2 p = c + nrm * R(-hw + 1.5f, hw - 1.5f);
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                Place(parent, mPatch, new Vector3(p.x, 0.1f, p.y), yaw, new Vector3(R(1.2f, 2.6f), R(1.8f, 4.5f), 0.6f), "Decal_Patch", 80f);
                count++;
            }
            return count;
        }

        static void Place(Transform parent, Material mat, Vector3 pos, float yaw, Vector3 size, string name, float drawDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, yaw, 0f);   // projects straight down; local up = road direction
            var d = go.AddComponent<DecalProjector>();
            d.material = mat;
            d.size = new Vector3(size.x, size.y, size.z + 0.4f);
            d.pivot = new Vector3(0f, 0f, 0.1f);
            d.drawDistance = drawDistance;
            d.fadeScale = 0.9f;
            d.scaleMode = DecalScaleMode.InheritFromHierarchy;
        }

        // ------------------------------------------------------------------ street furniture
        class MB
        {
            public List<Vector3> v = new List<Vector3>(); public List<Vector3> n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(nrm);
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up); uv.Add(Vector2.one);
                Vector3 cr = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(cr, nrm) < 0f) { t.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 }); } else { t.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 }); }
            }
            public void Box(Vector3 c, Vector3 s, float yaw, float yOffset = 0f)
            {
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 h = s * 0.5f;
                Func<float, float, float, Vector3> P = (x, y, z) => c + q * new Vector3(x * h.x, y * h.y + yOffset, z * h.z);
                Vector3[] f = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                for (int i = 0; i < 6; i++)
                {
                    Vector3 nrm = f[i]; Vector3 u, w;
                    if (Mathf.Abs(nrm.y) > 0.5f) { u = Vector3.right; w = Vector3.forward; } else if (Mathf.Abs(nrm.x) > 0.5f) { u = Vector3.forward; w = Vector3.up; } else { u = Vector3.right; w = Vector3.up; }
                    Vector3 a = P(nrm.x + (-u.x - w.x), nrm.y + (-u.y - w.y), nrm.z + (-u.z - w.z));
                    Vector3 b = P(nrm.x + (u.x - w.x), nrm.y + (u.y - w.y), nrm.z + (u.z - w.z));
                    Vector3 cc = P(nrm.x + (-u.x + w.x), nrm.y + (-u.y + w.y), nrm.z + (-u.z + w.z));
                    Vector3 d = P(nrm.x + (u.x + w.x), nrm.y + (u.y + w.y), nrm.z + (u.z + w.z));
                    Quad(a, b, cc, d, q * nrm);
                }
            }
            public void Cyl(Vector3 baseCenter, float r0, float r1, float h, int sides)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Quad(baseCenter + d0 * r0, baseCenter + d1 * r0, baseCenter + d0 * r1 + Vector3.up * h, baseCenter + d1 * r1 + Vector3.up * h, (d0 + d1).normalized);
                }
                // cap
                Vector3 top = baseCenter + Vector3.up * h;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    int k = v.Count; v.Add(top); v.Add(top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r1); v.Add(top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r1);
                    for (int q = 0; q < 3; q++) { n.Add(Vector3.up); uv.Add(Vector2.zero); }
                    t.AddRange(new[] { k, k + 2, k + 1 });
                }
            }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
                return m;
            }
        }

        static GameObject Emit(Transform parent, string name, MB mb, Material mat, string meshName)
        {
            string path = "Assets/Meshes/Campus/" + meshName + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            var mesh = mb.ToMesh(meshName); AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
            var col = go.AddComponent<MeshCollider>(); col.sharedMesh = mesh;
            return go;
        }

        static int BuildFurniture(Transform parent)
        {
            var metal = new MB(); var concrete = new MB(); var wood = new MB();
            int pieces = 0;

            // lamp posts: tall double-arm poles along the main road, short single-arm lamps along the walkways
            if (MainCenter != null)
            {
                var cum = new List<float> { 0f };
                for (int i = 1; i < MainCenter.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(MainCenter[i], MainCenter[i - 1]));
                for (float s = 75f; s < cum[cum.Count - 1] - 20f; s += 42f)
                {
                    int i = 1; while (i < cum.Count - 1 && cum[i] < s) i++;
                    Vector2 c = Vector2.Lerp(MainCenter[i - 1], MainCenter[i], (s - cum[i - 1]) / Mathf.Max(1e-3f, cum[i] - cum[i - 1]));
                    Vector2 d = (MainCenter[i] - MainCenter[i - 1]).normalized; Vector2 nrm = new Vector2(-d.y, d.x);
                    float hw = MainWidth[i] * 0.5f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 p = c + nrm * side * (hw + 2.6f);
                        Lamp(metal, new Vector3(p.x, 0f, p.y), 7.5f, Mathf.Atan2(-nrm.x * side, -nrm.y * side) * Mathf.Rad2Deg, true);
                        pieces++;
                    }
                }
            }
            foreach (var path in Paths)
            {
                float acc = 20f;
                for (int i = 1; i < path.Length; i++)
                {
                    float len = Vector2.Distance(path[i - 1], path[i]);
                    for (float s = acc; s < len; s += 38f)
                    {
                        Vector2 d = (path[i] - path[i - 1]).normalized, nrm = new Vector2(-d.y, d.x);
                        Vector2 p = Vector2.Lerp(path[i - 1], path[i], s / len) + nrm * 2.4f;
                        if (Overlaps(p)) continue;
                        Lamp(metal, new Vector3(p.x, 0f, p.y), 4.5f, Mathf.Atan2(-nrm.x, -nrm.y) * Mathf.Rad2Deg, false);
                        pieces++; acc = 0f;
                    }
                }
            }

            // bollards along the plaza edge (x -650..-596, z = -42)
            for (float x = -648f; x <= -598f; x += 4.5f) { concrete.Cyl(new Vector3(x, 0f, -42.5f), 0.14f, 0.12f, 0.85f, 10); concrete.Cyl(new Vector3(x, 0.85f, -42.5f), 0.12f, 0.1f, 0.04f, 10); pieces++; }

            // low plaza walls (0.5 m) on the lawn side of the plaza
            concrete.Box(new Vector3(-623f, 0f, -81f), new Vector3(54f, 0.5f, 0.35f), 90f, 0.25f); pieces++;

            // benches next to path junctions
            Vector2[] benches = { new Vector2(-476f, -150f), new Vector2(-484f, -150f), new Vector2(-476f, -205f), new Vector2(-694f, -176f), new Vector2(-706f, -158f), new Vector2(-632f, -118f), new Vector2(-370f, -240f), new Vector2(-366f, -170f) };
            for (int i = 0; i < benches.Length; i++) { Bench(wood, metal, new Vector3(benches[i].x, 0f, benches[i].y), (i % 2 == 0) ? 0f : 180f); pieces++; }

            var metalMat = RealismTextures.PlainMaterial("M_Campus_Metal", new Color(0.16f, 0.17f, 0.18f), 0.55f, 0.7f);
            var concMat = RealismTextures.PlainMaterial("M_Campus_PropConcrete", new Color(0.72f, 0.72f, 0.70f), 0.15f, 0f);
            var woodMat = RealismTextures.PlainMaterial("M_Campus_Wood", new Color(0.36f, 0.22f, 0.12f), 0.25f, 0f);
            Emit(parent, "Lamp_Posts_And_Bench_Frames", metal, metalMat, "Campus_Metal_Props");
            Emit(parent, "Bollards_And_Walls", concrete, concMat, "Campus_Concrete_Props");
            Emit(parent, "Bench_Seats", wood, woodMat, "Campus_Wood_Props");
            return pieces;
        }

        static bool Overlaps(Vector2 p)
        {
            // keep path lamps out of the lawn building footprints (approximate, same list as the layout builder)
            return p.x > -590f && p.x < -371f && p.y > -120f && p.y < 10f;
        }

        static void Lamp(MB m, Vector3 p, float h, float yaw, bool doubleArm)
        {
            m.Cyl(p, 0.16f, 0.11f, 0.5f, 8);
            m.Cyl(p + Vector3.up * 0.5f, 0.09f, 0.055f, h - 0.5f, 8);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 top = p + Vector3.up * h;
            Action<float> arm = dir =>
            {
                Vector3 side = q * new Vector3(0f, 0f, dir);
                m.Box(top + side * 0.9f + Vector3.up * -0.1f, new Vector3(0.07f, 0.07f, 1.8f), yaw, 0f);
                m.Box(top + side * 1.75f + Vector3.up * -0.2f, new Vector3(0.28f, 0.1f, 0.7f), yaw, 0f);
            };
            arm(1f);
            if (doubleArm) arm(-1f);
        }

        static void Bench(MB wood, MB metal, Vector3 p, float yaw)
        {
            wood.Box(p + Vector3.up * 0.45f, new Vector3(1.7f, 0.06f, 0.45f), yaw, 0f);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            wood.Box(p + q * new Vector3(0f, 0.72f, -0.2f), new Vector3(1.7f, 0.4f, 0.05f), yaw, 0f);
            metal.Box(p + q * new Vector3(-0.7f, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.4f), yaw, 0f);
            metal.Box(p + q * new Vector3(0.7f, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.4f), yaw, 0f);
        }
    }
}
