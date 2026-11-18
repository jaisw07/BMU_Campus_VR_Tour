using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Replaces the flat-coloured Blender lawns of Scene_Campus_Exterior (NBFrontLawn, LibFrontLawn, ...) with realistic
    /// PBR grass (Assets/Art/Textures/Ground/Grass004..007) and plants Jacaranda trees on and around them.
    /// Buildings, roads, anchors and every other object are left alone. Idempotent: re-running rebuilds the trees.
    /// Menu: BMU VR Tour / Replace Old Lawns With Realistic Grass.
    /// </summary>
    public static class RealisticLawns
    {
        const string EnvName = "Campus_Exterior_Environment";
        const string TreeRoot = "Campus_Trees";
        const string MatDir = "Assets/Materials/Environment";
        const string MeshDir = "Assets/Generated/RealisticLawns";
        const float TileSize = 7f;            // world units per texture repeat
        const float TreeSpacing = 30f;        // world units between candidate tree spots
        const float TreeKeep = 0.6f;          // fraction of candidate spots that get a tree
        const float BuildingMargin = 14f;     // keep canopies off buildings
        const float AnchorClearance = 30f;    // keep the teleport anchors open
        const float RoadClearance = 8f;
        const int MaxTrees = 70;              // Quest budget guard
        const int Seed = 4242;

        static readonly string[] LawnNames =
        {
            "NBFrontLawn", "LibFrontLawn", "FrontLibLawn", "GrassLawnLibFront", "QCafeFrontLawn", "GrassLawn",
            "PondArea", "PondArea.001",   // these carried the flat Blender materials Grass.003 / Grass.004
        };
        // Grass004 is intentionally not used (it looked wrong); Grass005-007 are the PBR sets that are.
        static readonly string[] GrassSets = { "Grass005", "Grass006", "Grass007" };
        static readonly Color[] Tints =
        {
            new Color(1.00f, 1.00f, 0.95f), new Color(0.90f, 0.97f, 0.88f), new Color(1.00f, 0.98f, 0.90f),
        };

        [MenuItem("BMU VR Tour/Replace Old Lawns With Realistic Grass", false, 15)]
        public static void Run()
        {
            Transform env = FindEnv();
            if (env == null) { Debug.LogError("[RealisticLawns] " + EnvName + " not found. Open Scene_Campus_Exterior first."); return; }
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(MeshDir);

            var lawns = new List<Transform>();
            for (int i = 0; i < LawnNames.Length; i++)
            {
                Transform t = env.Find(LawnNames[i]);
                if (t == null || t.GetComponent<MeshFilter>() == null) { Debug.LogWarning("[RealisticLawns] Lawn not found: " + LawnNames[i]); continue; }
                lawns.Add(t);
                ReskinLawn(t, i % GrassSets.Length);
            }

            Physics.SyncTransforms();
            int trees = PlantTrees(env, lawns);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[RealisticLawns] Re-skinned {lawns.Count} lawn(s), planted {trees} Jacaranda tree(s). Save the scene (Ctrl+S).");
        }

        static Transform FindEnv()
        {
            foreach (GameObject r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name == EnvName) return r.transform;
            return null;
        }

        // ------------------------------------------------------------------ grass
        static void ReskinLawn(Transform lawn, int setIndex)
        {
            var mf = lawn.GetComponent<MeshFilter>();
            var mr = lawn.GetComponent<MeshRenderer>();

            // Copy the mesh once, with UVs from world XZ so the grass has a constant real-world scale on every lawn.
            string meshPath = $"{MeshDir}/{lawn.name}_Grass.asset";
            Mesh src = mf.sharedMesh;
            if (src.name.EndsWith("_Grass")) { string p = AssetDatabase.GetAssetPath(src); if (!string.IsNullOrEmpty(p)) meshPath = p; }
            Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (m == null) { m = Object.Instantiate(src); m.name = lawn.name + "_Grass"; AssetDatabase.CreateAsset(m, meshPath); }
            else if (src != m) { EditorUtility.CopySerialized(Object.Instantiate(src), m); m.name = lawn.name + "_Grass"; }

            Vector3[] v = m.vertices;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = lawn.TransformPoint(v[i]);
                uv[i] = new Vector2(w.x / TileSize, w.z / TileSize);
            }
            m.uv = uv;
            m.RecalculateTangents();
            EditorUtility.SetDirty(m);
            mf.sharedMesh = m;
            var mc = lawn.GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = m;

            mr.sharedMaterial = GrassMaterial(setIndex);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        static Material GrassMaterial(int setIndex)
        {
            string set = GrassSets[setIndex];
            string path = $"{MatDir}/M_RealGrass_{set}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            string dir = $"Assets/Art/Textures/Ground/{set}/{set}_2K-JPG_";
            Texture2D color = LoadTex(dir + "Color.jpg", false);
            Texture2D normal = LoadTex(dir + "NormalGL.jpg", true);
            Texture2D ao = LoadTex(dir + "AmbientOcclusion.jpg", false);

            mat.SetTexture("_BaseMap", color);
            mat.SetColor("_BaseColor", Tints[setIndex]);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 0.8f); mat.EnableKeyword("_NORMALMAP"); }
            if (ao != null) { mat.SetTexture("_OcclusionMap", ao); mat.SetFloat("_OcclusionStrength", 0.7f); }
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D LoadTex(string path, bool normalMap)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return null;
            bool changed = false;
            if (normalMap && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; changed = true; }
            if (imp.maxTextureSize > 1024) { imp.maxTextureSize = 1024; changed = true; }   // 2K x 4 sets is too heavy for Quest
            if (imp.anisoLevel < 8) { imp.anisoLevel = 8; changed = true; }
            if (!imp.mipmapEnabled) { imp.mipmapEnabled = true; changed = true; }
            if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (changed) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ trees
        static int PlantTrees(Transform env, List<Transform> lawns)
        {
            GameObject old = GameObject.Find(TreeRoot);
            if (old != null) Object.DestroyImmediate(old);

            var prefabs = new List<GameObject>();
            foreach (string n in new[] { "Green", "Light", "Olive", "Bloom", "Dry" })
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Trees/Tree_Jacaranda_{n}.prefab");
                if (pf != null) prefabs.Add(pf);
            }
            if (prefabs.Count == 0) { Debug.LogError("[RealisticLawns] No Jacaranda prefabs in Assets/Prefabs/Trees. Merge the realistic-campus branch assets first."); return 0; }

            // Keep-out zones: buildings (everything that is not environment ground/lawn/road) and teleport anchors.
            var avoid = new List<Bounds>();
            foreach (GameObject r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (r.name == EnvName)
                {
                    foreach (Transform c in r.transform)
                    {
                        string n = c.name.ToLower();
                        bool structure = n.Contains("building") || n.Contains("gateway") || n.Contains("workshop") || n.Contains("bsh") || n.Contains("ga_gb")
                                      || n.Contains("housing") || n.Contains("wall") || n.Contains("gate") || n.Contains("grill") || n.Contains("divider");
                        if (structure && c.gameObject.activeInHierarchy) AddBounds(avoid, c, BuildingMargin);
                    }
                }
                else if (r.name.StartsWith("BML_") || r.name == "NorthBlock_Exterior" || r.name == "Hostels")
                    AddBounds(avoid, r.transform, BuildingMargin);
            }
            var anchors = new List<Vector3>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name.StartsWith("Anchor_")) anchors.Add(t.position);

            // Roads/paths = env children with those names: keep canopies off them.
            var roads = new List<Bounds>();
            foreach (Transform c in env)
            {
                string n = c.name.ToLower();
                if ((n.Contains("road") || n.Contains("path")) && c.gameObject.activeInHierarchy) AddBounds(roads, c, RoadClearance);
            }

            var root = new GameObject(TreeRoot).transform;
            var rng = new System.Random(Seed);
            int count = 0;
            foreach (Transform lawn in lawns)
            {
                Bounds b = lawn.GetComponent<MeshRenderer>().bounds;
                var col = lawn.GetComponent<Collider>();
                for (float x = b.min.x + TreeSpacing * 0.5f; x < b.max.x; x += TreeSpacing)
                    for (float z = b.min.z + TreeSpacing * 0.5f; z < b.max.z; z += TreeSpacing)
                    {
                        if (count >= MaxTrees) break;
                        if (rng.NextDouble() > TreeKeep) continue;
                        float px = x + (float)(rng.NextDouble() - 0.5) * TreeSpacing * 0.6f;
                        float pz = z + (float)(rng.NextDouble() - 0.5) * TreeSpacing * 0.6f;
                        // must sit on this lawn
                        if (col == null || !Physics.Raycast(new Vector3(px, 200f, pz), Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore) || hit.collider != col) continue;
                        Vector3 p = hit.point;
                        if (Blocked(p, avoid, roads, anchors)) continue;

                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[rng.Next(prefabs.Count)]);
                        inst.transform.SetParent(root, false);
                        inst.transform.position = p;
                        inst.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                        inst.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.25f, (float)rng.NextDouble());
                        GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
                        count++;
                    }
            }
            return count;
        }

        static void AddBounds(List<Bounds> list, Transform t, float margin)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            b.Expand(new Vector3(margin * 2f, 1000f, margin * 2f));
            list.Add(b);
        }

        static bool Blocked(Vector3 p, List<Bounds> avoid, List<Bounds> roads, List<Vector3> anchors)
        {
            foreach (Bounds b in avoid) if (b.Contains(new Vector3(p.x, b.center.y, p.z))) return true;
            foreach (Bounds b in roads) if (b.Contains(new Vector3(p.x, b.center.y, p.z))) return true;
            foreach (Vector3 a in anchors) if (new Vector2(p.x - a.x, p.z - a.z).magnitude < AnchorClearance) return true;
            return false;
        }
    }
}
