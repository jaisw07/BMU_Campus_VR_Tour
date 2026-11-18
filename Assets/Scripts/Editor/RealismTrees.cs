using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Turns the Polyhaven Jacaranda (CC0, ~3.6M triangles raw) into scene-ready trees:
    ///  - baked LOD0..LOD2 meshes (leaf-island thinning with size compensation, twig thinning, trunk vertex welding),
    ///  - a billboard LOD3 captured from the model,
    ///  - PBR materials (URP Lit bark, BMU/Foliage leaves with wind + translucency, tint variants),
    ///  - prefabs with LODGroup, a trunk-only collider and shadow casting.
    /// The raw glTF/bin (208 MB) is only needed to re-bake; the baked assets are what the scene uses.
    /// </summary>
    public static class RealismTrees
    {
        public const string Dir = "Assets/Models/Trees/Jacaranda";
        public const string PrefabDir = "Assets/Prefabs/Trees";
        const string Src = Dir + "/jacaranda_tree_2k.gltf";
        const string Tx = Dir + "/textures/jacaranda_tree_";

        public static readonly string[] VariantNames = { "Green", "Light", "Olive", "Dry", "Bloom" };

        // -------------------------------------------------------------- textures
        static Texture2D LoadRaw(string path, bool linear = false)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        public static void BuildTextures()
        {
            // Leaf albedo: RGB from the diffuse map, alpha from the separate alpha map.
            string leafOut = Dir + "/Jacaranda_Leaves_Albedo.png";
            if (!File.Exists(leafOut))
            {
                var d = LoadRaw(Tx + "leaves_diff_2k.jpg");
                var a = LoadRaw(Tx + "leaves_alpha_2k.png", true);
                var px = d.GetPixels32();
                var ap = a.GetPixels32();
                if (ap.Length != px.Length) Debug.LogWarning("[RealismTrees] leaf alpha size differs from diffuse");
                for (int i = 0; i < px.Length && i < ap.Length; i++) px[i].a = ap[i].r;
                d.SetPixels32(px);
                File.WriteAllBytes(leafOut, d.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(d); UnityEngine.Object.DestroyImmediate(a);
                AssetDatabase.ImportAsset(leafOut, ImportAssetOptions.ForceSynchronousImport);
            }
            SetImporter(leafOut, false, false, true);
            SetImporter(Tx + "leaves_nor_gl_2k.jpg", true, false, false);
            SetImporter(Tx + "branches_diff_2k.jpg", false, false, false);
            SetImporter(Tx + "branches_nor_gl_2k.jpg", true, false, false);
            SetImporter(Tx + "trunk_diff_2k.jpg", false, false, false);
            SetImporter(Tx + "trunk_nor_gl_2k.jpg", true, false, false);
            EnsureMask("branches");
            EnsureMask("trunk");
        }

        // glTF "arm" = R ambient occlusion, G roughness, B metallic  ->  mask: R metal, G AO, B 0.5, A smoothness
        static void EnsureMask(string part)
        {
            string outPath = Dir + "/Jacaranda_" + part + "_Mask.png";
            if (!File.Exists(outPath))
            {
                var arm = LoadRaw(Tx + part + "_arm_2k.jpg", true);
                const int n = 1024;
                var mask = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        Color c = arm.GetPixelBilinear((x + 0.5f) / n, (y + 0.5f) / n);
                        px[y * n + x] = new Color32((byte)(c.b * 255f), (byte)(c.r * 255f), 128, (byte)((1f - c.g) * 255f));
                    }
                mask.SetPixels32(px);
                File.WriteAllBytes(outPath, mask.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(mask); UnityEngine.Object.DestroyImmediate(arm);
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);
            }
            SetImporter(outPath, false, true, false);
        }

        static void SetImporter(string path, bool normal, bool linearData, bool alphaCutout)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !linearData;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            if (alphaCutout)
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.45f;
                ti.wrapMode = TextureWrapMode.Clamp;
            }
            ti.SaveAndReimport();
        }

        // -------------------------------------------------------------- LOD meshes
        struct LodSpec
        {
            public float leafKeep, twigKeep, trunkWeld;   // trunkWeld = vertex cell size in model metres (0 = keep)
            public LodSpec(float l, float t, float w) { leafKeep = l; twigKeep = t; trunkWeld = w; }
        }

        static float Hash01(int a, int seed)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(seed * 19349663);
                h ^= h >> 15; h *= 0x85ebca6bu; h ^= h >> 13; h *= 0xc2b2ae35u; h ^= h >> 16;
                return (h & 0xffffffu) / 16777215f;
            }
        }

        public static void BuildLodMeshes()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Src);
            if (go == null) { Debug.LogError("[RealismTrees] " + Src + " not imported (download it, see ASSET_SOURCES.md)"); return; }
            Mesh src = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            var lods = new[] { new LodSpec(0.22f, 0.30f, 0.02f), new LodSpec(0.05f, 0.06f, 0.12f), new LodSpec(0.012f, 0.0f, 0.50f) };
            var pos = src.vertices; var nrm = src.normals; var tan = src.tangents; var uv = src.uv;
            var subs = new[] { src.GetTriangles(0), src.GetTriangles(1), src.GetTriangles(2) };
            var islandRoot = new int[3][];
            for (int s = 0; s < 3; s++) islandRoot[s] = IslandRoots(subs[s], pos.Length);

            for (int l = 0; l < lods.Length; l++)
            {
                var o = new MeshAccum();
                // branches: keep big limbs, thin small twigs
                o.AddIslands(pos, nrm, tan, uv, subs[0], islandRoot[0], i => 0, true, lods[l].twigKeep, 1f, 300, 11 + l);
                // trunk: weld
                if (lods[l].trunkWeld > 0f) o.AddWelded(pos, nrm, tan, uv, subs[1], lods[l].trunkWeld);
                else o.AddIslands(pos, nrm, tan, uv, subs[1], islandRoot[1], i => 0, true, 1f, 1f, 0, 5);
                // leaves: keep a fraction of leaf islands, enlarged to keep canopy coverage
                float keep = lods[l].leafKeep;
                o.AddIslands(pos, nrm, tan, uv, subs[2], islandRoot[2], i => 0, false, keep, 1f / Mathf.Sqrt(keep), 0, 23 + l);
                Mesh m = o.ToMesh("Jacaranda_LOD" + l, src.bounds);
                string path = Dir + "/Jacaranda_LOD" + l + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.High);
                AssetDatabase.CreateAsset(m, path);
                Debug.Log("[RealismTrees] LOD" + l + ": " + m.vertexCount + " verts, " + (m.triangles.Length / 3) + " tris (branches " + m.GetIndexCount(0) / 3 + ", trunk " + m.GetIndexCount(1) / 3 + ", leaves " + m.GetIndexCount(2) / 3 + ")");
            }
            AssetDatabase.SaveAssets();
        }

        static int[] IslandRoots(int[] idx, int vertexCount)
        {
            var parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            for (int i = 0; i < idx.Length; i += 3)
            {
                int a = find(idx[i]), b = find(idx[i + 1]), c = find(idx[i + 2]);
                parent[b] = a; parent[find(c)] = find(a);
            }
            var root = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) root[i] = find(i);
            return root;
        }

        class MeshAccum
        {
            public List<Vector3> p = new List<Vector3>(); public List<Vector3> n = new List<Vector3>();
            public List<Vector4> t = new List<Vector4>(); public List<Vector2> u = new List<Vector2>();
            public List<int>[] tris = { new List<int>(), new List<int>(), new List<int>() };
            int current = -1;

            public void AddIslands(Vector3[] pos, Vector3[] nrm, Vector4[] tan, Vector2[] uv, int[] idx, int[] root,
                                   Func<int, int> unused, bool keepBigAlways, float keep, float scale, int bigTris, int seed)
            {
                current++;
                var remap = new Dictionary<int, int>();
                // per-island triangle counts and centroids (only needed when scaling)
                var count = new Dictionary<int, int>();
                for (int i = 0; i < idx.Length; i += 3) { int r = root[idx[i]]; count.TryGetValue(r, out int c); count[r] = c + 1; }
                Dictionary<int, Vector3> centroid = null;
                if (scale != 1f)
                {
                    centroid = new Dictionary<int, Vector3>(); var verts = new Dictionary<int, HashSet<int>>();
                    for (int i = 0; i < idx.Length; i++)
                    {
                        int r = root[idx[i]];
                        if (!verts.TryGetValue(r, out var hs)) { hs = new HashSet<int>(); verts[r] = hs; }
                        hs.Add(idx[i]);
                    }
                    foreach (var kv in verts) { Vector3 sum = Vector3.zero; foreach (int v in kv.Value) sum += pos[v]; centroid[kv.Key] = sum / kv.Value.Count; }
                }
                for (int i = 0; i < idx.Length; i += 3)
                {
                    int r = root[idx[i]];
                    bool keepIt = (bigTris > 0 && count[r] >= bigTris) || keep >= 1f || Hash01(r, seed) < keep;
                    if (!keepIt) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = idx[i + k];
                        // vertices are shared inside an island only, so the remap key is the vertex index
                        if (!remap.TryGetValue(v, out int nv))
                        {
                            nv = p.Count; remap[v] = nv;
                            Vector3 pp = pos[v];
                            if (centroid != null) pp = centroid[r] + (pp - centroid[r]) * scale;
                            p.Add(pp); n.Add(nrm[v]); t.Add(tan[v]); u.Add(uv[v]);
                        }
                        tris[current].Add(nv);
                    }
                }
            }

            public void AddWelded(Vector3[] pos, Vector3[] nrm, Vector4[] tan, Vector2[] uv, int[] idx, float cell)
            {
                current++;
                var cellMap = new Dictionary<long, int>();
                var remap = new Dictionary<int, int>();
                var sumN = new List<Vector3>(); var cnt = new List<int>(); var sumP = new List<Vector3>();
                int first = p.Count;
                for (int i = 0; i < idx.Length; i += 3)
                {
                    var nv = new int[3];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = idx[i + k];
                        if (!remap.TryGetValue(v, out int mapped))
                        {
                            Vector3 q = pos[v] / cell;
                            // include uv in the key so texture seams are preserved
                            long key = ((long)Mathf.RoundToInt(q.x) * 73856093L) ^ ((long)Mathf.RoundToInt(q.y) * 19349663L) ^ ((long)Mathf.RoundToInt(q.z) * 83492791L)
                                       ^ ((long)Mathf.RoundToInt(uv[v].x * 64f) * 1000003L) ^ ((long)Mathf.RoundToInt(uv[v].y * 64f) * 998244353L);
                            if (!cellMap.TryGetValue(key, out mapped))
                            {
                                mapped = p.Count; cellMap[key] = mapped;
                                p.Add(pos[v]); n.Add(nrm[v]); t.Add(tan[v]); u.Add(uv[v]);
                                sumP.Add(Vector3.zero); sumN.Add(Vector3.zero); cnt.Add(0);
                            }
                            remap[v] = mapped;
                            int li = mapped - first;
                            sumP[li] += pos[v]; sumN[li] += nrm[v]; cnt[li]++;
                        }
                        nv[k] = mapped;
                    }
                    if (nv[0] != nv[1] && nv[1] != nv[2] && nv[0] != nv[2]) { tris[current].Add(nv[0]); tris[current].Add(nv[1]); tris[current].Add(nv[2]); }
                }
                for (int i = 0; i < cnt.Count; i++) { p[first + i] = sumP[i] / Mathf.Max(1, cnt[i]); n[first + i] = sumN[i].normalized; }
            }

            public Mesh ToMesh(string name, Bounds bounds)
            {
                var m = new Mesh { name = name, indexFormat = p.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(p); m.SetNormals(n); m.SetTangents(t); m.SetUVs(0, u);
                m.subMeshCount = 3;
                for (int s = 0; s < 3; s++) m.SetTriangles(tris[s], s, false);
                m.bounds = bounds;
                return m;
            }
        }

        // -------------------------------------------------------------- materials
        public static Material BarkMaterial(string part)
        {
            string path = Dir + "/Materials/Jacaranda_" + part + ".mat";
            EnsureFolder(Dir + "/Materials");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tx + part + "_diff_2k.jpg"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tx + part + "_nor_gl_2k.jpg"));
            mat.EnableKeyword("_NORMALMAP");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Jacaranda_" + part + "_Mask.png");
            mat.SetTexture("_MetallicGlossMap", mask); mat.SetTexture("_OcclusionMap", mask);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_Cull", 0f);                 // thin twigs: render both sides
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material LeafMaterial(string variant)
        {
            string path = Dir + "/Materials/Jacaranda_Leaves_" + variant + ".mat";
            EnsureFolder(Dir + "/Materials");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("BMU/Foliage")); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Shader.Find("BMU/Foliage");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Jacaranda_Leaves_Albedo.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tx + "leaves_nor_gl_2k.jpg"));
            Color tint; Color trans;
            switch (variant)
            {
                case "Light": tint = new Color(1.12f, 1.2f, 0.85f); trans = new Color(0.6f, 0.8f, 0.2f); break;
                case "Olive": tint = new Color(0.95f, 0.95f, 0.6f); trans = new Color(0.6f, 0.65f, 0.15f); break;
                case "Dry": tint = new Color(1.25f, 1.05f, 0.5f); trans = new Color(0.8f, 0.6f, 0.1f); break;
                case "Bloom": tint = new Color(1.1f, 0.85f, 1.25f); trans = new Color(0.7f, 0.4f, 0.8f); break;
                default: tint = new Color(0.92f, 1.0f, 0.82f); trans = new Color(0.5f, 0.75f, 0.2f); break;
            }
            mat.SetColor("_BaseColor", tint); mat.SetColor("_TranslucencyColor", trans);
            mat.SetFloat("_Cutoff", 0.45f);
            mat.SetFloat("_WindStrength", 0.16f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // -------------------------------------------------------------- billboard LOD
        /// <summary>Captures two alpha-cut views (front, side) of LOD1 for the crossed-card billboard LOD.</summary>
        public static void BuildBillboards()
        {
            EnsureFolder(Dir + "/Billboards");
            var lod = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/Jacaranda_LOD1.asset");
            if (lod == null) { Debug.LogError("[RealismTrees] bake LOD meshes first"); return; }
            var root = new GameObject("TmpBillboardCapture");
            root.transform.position = new Vector3(0f, -5000f, 0f);
            var mf = root.AddComponent<MeshFilter>(); mf.sharedMesh = lod;
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { BarkMaterial("branches"), BarkMaterial("trunk"), LeafMaterial("Green") };
            var lightGo = new GameObject("TmpBillboardLight"); var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f; light.color = new Color(1f, 0.98f, 0.92f); light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Bounds b = lod.bounds;
            float size = Mathf.Max(b.size.x, b.size.z, b.size.y) * 1.02f;
            Vector3 center = root.transform.position + new Vector3(b.center.x, b.center.y, b.center.z);
            for (int view = 0; view < 2; view++)
            {
                var camGo = new GameObject("TmpBillboardCam"); var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true; cam.orthographicSize = size * 0.5f; cam.aspect = 1f;
                cam.nearClipPlane = 1f; cam.farClipPlane = 200f; cam.clearFlags = CameraClearFlags.SolidColor;
                Vector3 dir = view == 0 ? Vector3.back : Vector3.left;
                camGo.transform.position = center - dir * 60f; camGo.transform.rotation = Quaternion.LookRotation(dir);
                const int res = 512;
                Texture2D onBlack = Capture(cam, res, Color.black), onWhite = Capture(cam, res, Color.white);
                var result = new Texture2D(res, res, TextureFormat.RGBA32, false);
                var pb = onBlack.GetPixels32(); var pw = onWhite.GetPixels32(); var po = new Color32[pb.Length];
                for (int i = 0; i < pb.Length; i++)
                {
                    // alpha from the difference between a black and a white background render
                    float a = 1f - (pw[i].r - pb[i].r) / 255f;
                    a = Mathf.Clamp01(a);
                    if (a < 0.02f) { po[i] = new Color32(0, 0, 0, 0); continue; }
                    po[i] = new Color32((byte)Mathf.Min(255f, pb[i].r / a), (byte)Mathf.Min(255f, pb[i].g / a), (byte)Mathf.Min(255f, pb[i].b / a), (byte)(a * 255f));
                }
                result.SetPixels32(po);
                string path = Dir + "/Billboards/Jacaranda_Billboard_" + (view == 0 ? "Front" : "Side") + ".png";
                File.WriteAllBytes(path, result.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(result); UnityEngine.Object.DestroyImmediate(onBlack); UnityEngine.Object.DestroyImmediate(onWhite);
                UnityEngine.Object.DestroyImmediate(camGo);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                SetImporter(path, false, false, true);
            }
            UnityEngine.Object.DestroyImmediate(lightGo); UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[RealismTrees] billboards captured");
        }

        static Texture2D Capture(Camera cam, int res, Color bg)
        {
            cam.backgroundColor = bg;
            var rt = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(res, res, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, res, res), 0, 0); t.Apply();
            RenderTexture.active = null; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt);
            return t;
        }

        // -------------------------------------------------------------- prefabs
        static Mesh BillboardMesh(float width, float height)
        {
            // two crossed quads, pivot at the base centre
            var m = new Mesh { name = "Jacaranda_BillboardCross" };
            float hw = width * 0.5f;
            m.vertices = new[]
            {
                new Vector3(-hw, 0, 0), new Vector3(hw, 0, 0), new Vector3(-hw, height, 0), new Vector3(hw, height, 0),
                new Vector3(0, 0, -hw), new Vector3(0, 0, hw), new Vector3(0, height, -hw), new Vector3(0, height, hw),
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var n = new Vector3[8]; for (int i = 0; i < 8; i++) n[i] = Vector3.up; // soft top-down shading, no hard facing
            m.normals = n;
            m.subMeshCount = 2;
            m.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            m.SetTriangles(new[] { 4, 6, 5, 6, 7, 5 }, 1);
            var tan = new Vector4[8]; for (int i = 0; i < 8; i++) tan[i] = new Vector4(1, 0, 0, 1);
            m.tangents = tan;
            m.RecalculateBounds();
            return m;
        }

        public static Material BillboardMaterial(string view, string variant)
        {
            EnsureFolder(Dir + "/Materials");
            string path = Dir + "/Materials/Jacaranda_Billboard_" + view + "_" + variant + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("BMU/Foliage")); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Shader.Find("BMU/Foliage");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Billboards/Jacaranda_Billboard_" + view + ".png"));
            var leaf = LeafMaterial(variant);
            mat.SetColor("_BaseColor", Color.Lerp(Color.white, leaf.GetColor("_BaseColor"), 0.8f));
            mat.SetFloat("_Cutoff", 0.35f); mat.SetFloat("_Translucency", 0.2f); mat.SetFloat("_WindStrength", 0.1f);
            mat.SetFloat("_BumpScale", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static string PrefabPath(string variant) { return PrefabDir + "/Tree_Jacaranda_" + variant + ".prefab"; }

        public static void BuildPrefabs()
        {
            EnsureFolder(PrefabDir);
            var lods = new Mesh[3];
            for (int i = 0; i < 3; i++) lods[i] = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/Jacaranda_LOD" + i + ".asset");
            if (lods[0] == null) { Debug.LogError("[RealismTrees] bake LOD meshes first"); return; }
            string bbPath = Dir + "/Billboards/Jacaranda_BillboardCross.asset";
            var bbMesh = AssetDatabase.LoadAssetAtPath<Mesh>(bbPath);
            if (bbMesh == null) { bbMesh = BillboardMesh(24f, 22f); AssetDatabase.CreateAsset(bbMesh, bbPath); }
            Material branches = BarkMaterial("branches"), trunk = BarkMaterial("trunk");

            foreach (string variant in VariantNames)
            {
                var root = new GameObject("Tree_Jacaranda_" + variant);
                var rends = new Renderer[4];
                for (int l = 0; l < 4; l++)
                {
                    var child = new GameObject("LOD" + l); child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = l < 3 ? lods[l] : bbMesh;
                    var mr = child.AddComponent<MeshRenderer>();
                    if (l < 3) mr.sharedMaterials = new[] { branches, trunk, LeafMaterial(variant) };
                    else mr.sharedMaterials = new[] { BillboardMaterial("Front", variant), BillboardMaterial("Side", variant) };
                    mr.shadowCastingMode = l < 3 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    mr.receiveShadows = l < 3;
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    rends[l] = mr;
                }
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.35f, new[] { rends[0] }), new LOD(0.14f, new[] { rends[1] }),
                    new LOD(0.05f, new[] { rends[2] }), new LOD(0.012f, new[] { rends[3] }),
                });
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();
                // trunk-only collider (model units: trunk radius ~0.45 m, straight for ~3 m)
                var col = root.AddComponent<CapsuleCollider>();
                col.direction = 1; col.radius = 0.5f; col.height = 3.5f; col.center = new Vector3(0f, 1.75f, 0f);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(variant));
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[RealismTrees] prefabs built in " + PrefabDir);
        }

        [MenuItem("BMU VR Tour/Realism/Bake Jacaranda Trees (textures, LODs, prefabs)")]
        public static void BakeAll()
        {
            BuildTextures();
            BuildLodMeshes();
            BuildBillboards();
            BuildPrefabs();
        }
    }
}
