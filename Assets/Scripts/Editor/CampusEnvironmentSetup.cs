using System.IO;
using UnityEngine;
using UnityEditor;

namespace BMU.VRTour.Editor
{
    public static class CampusEnvironmentSetup
    {
        private const string GlbModelPath = "Assets/Models/Exterior/Campus_Exterior.glb";
        public const string ExteriorPrefabPath = "Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab";

        [MenuItem("BMU VR Tour/Setup Campus Exterior Prefab", false, 10)]
        public static GameObject CreateOrUpdateExteriorPrefab()
        {
            GameObject glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GlbModelPath);
            if (glbAsset == null)
            {
                Debug.LogError($"[CampusEnvironmentSetup] Could not find model at '{GlbModelPath}'!");
                return null;
            }

            // Create temporary instance to configure
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset);
            root.name = "Campus_Exterior_Environment";
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f); // Halved scale per user calibration

            // Configure all children
            ConfigureEnvironmentObject(root);

            // Ensure destination directory exists
            string dir = Path.GetDirectoryName(ExteriorPrefabPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Save as prefab
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ExteriorPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CampusEnvironmentSetup] Successfully generated Prefab at '{ExteriorPrefabPath}'!");
            return prefab;
        }

        // ------------------------------------------------------------------
        // North Block exterior (detailed building that replaces the NB massing box)
        // ------------------------------------------------------------------
        // UPDATE: now uses BML_North_Block_HD.glb, a textured 2x export like the old NB_Building.glb (building 125.2 x 50.5 x 69.4 m raw),
        // so the scene builder's 1.75 gives the same campus size. The GLB also carries site parts (NB_Ground 240 x 200 m, NB_Plaza_Tiles,
        // NB_Path) that would z-fight with the campus paving and cover the outlet plaza: the prefab deactivates them.
        // It has no NB_Main_Door node (the old walkable entrance recess). Older prefabs/GLBs (NB_Building, BML_North_Block_Realistic) stay, unused.
        private const string NorthBlockGlbPath = "Assets/Models/Exterior/BML_North_Block_HD.glb";
        public const string NorthBlockPrefabPath = "Assets/Prefabs/Environment/Prefab_Exterior_NorthBlock_HD.prefab";
        private static readonly string[] NorthBlockSiteParts = { "NB_Ground", "NB_Plaza_Tiles", "NB_Path" };

        // Only these NB_ parts get MeshColliders (walls, slabs, roof, stairs, steps). Windows, fins, chillers and
        // the door (NB_Main_Door, glass included) stay collider-free so the recess is walkable.
        private static readonly string[] NorthBlockColliderParts =
        {
            "nb_shell_hollow", "nb_carved_stairs", "nb_gallery_courtyards", "nb_cafe_e2", "nb_roof_details"
        };

        [MenuItem("BMU VR Tour/Setup North Block Exterior Prefab", false, 11)]
        public static GameObject CreateOrUpdateNorthBlockPrefab()
        {
            GameObject glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NorthBlockGlbPath);
            if (glbAsset == null)
            {
                Debug.LogError($"[CampusEnvironmentSetup] Could not find model at '{NorthBlockGlbPath}'!");
                return null;
            }

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset);
            root.name = "NorthBlock_Exterior";
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one; // scale is applied per scene instance by the builder

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                GameObject obj = mf.gameObject;
                obj.layer = 0; // never Layer 31 Teleport
                GameObjectUtility.SetStaticEditorFlags(obj,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.ContributeGI);

                if (IsNorthBlockColliderPart(obj.transform, root.transform))
                {
                    MeshCollider col = obj.GetComponent<MeshCollider>();
                    if (col == null) col = obj.AddComponent<MeshCollider>();
                    col.sharedMesh = mf.sharedMesh;
                    col.convex = false;
                }
            }

            // Site parts that come with the HD GLB (ground plane, plaza floor, path): the campus already has its own ground.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(NorthBlockSiteParts, t.name) >= 0) t.gameObject.SetActive(false);
            }

            string dir = Path.GetDirectoryName(NorthBlockPrefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, NorthBlockPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CampusEnvironmentSetup] Generated North Block prefab at '{NorthBlockPrefabPath}'.");
            return prefab;
        }

        // ------------------------------------------------------------------
        // The Gateway (admin & incubator) exterior - replaces the 'GA_GB' massing box baked into the campus GLB.
        // ------------------------------------------------------------------
        // Raw model is a 2x export (140.7 x 43.5 x 95.8 m, pivot = footprint centre at ground, front/atrium on +Z).
        private const string GatewayGlbPath = "Assets/Models/Exterior/Gateway_Exterior.glb";
        public const string GatewayPrefabPath = "Assets/Prefabs/Environment/Prefab_Exterior_Gateway.prefab";
        private const string GatewayMaterialDir = "Assets/Materials/Environment";

        // World scale 1.0 (= 0.5 campus scale x 2). The North Block / Workshop factor of 1.75 does NOT fit here: it makes
        // the Gateway 246 m wide and overlaps the Library, Maggi Point and the entry road. 1.0 matches the old GA_GB
        // massing box footprint (140.5 m wide along Z) exactly.
        // UPDATE: enlarged to 1.3 so the Gateway reads bigger next to the North Block (height 57 m vs NB 88 m, was 44 m).
        // The canopy front edge is kept fixed at x = -127 (10 m before Anchor_Gateway_Plaza); the extra size grows backwards
        // (-X, to x = -251) and sideways (Z -179..4). The Maggi Point kiosk (hand-placed) was moved left to keep a 15 m gap.
        public const float GatewayWorldScale = 1.3f;
        // Footprint centre. Canopy front edge lands 10 m beyond Anchor_Gateway_Plaza (-116.5, -87.33); x was -174.4 at scale 1.0.
        public static readonly Vector3 GatewayPosition = new Vector3(-188.77f, 0f, -87.33f);
        // Model front (+Z) must face the plaza anchor, which is on the +X side: Y rotation +90.
        public static readonly Quaternion GatewayRotation = Quaternion.Euler(0f, 90f, 0f);

        [MenuItem("BMU VR Tour/Setup Gateway Exterior Prefab", false, 12)]
        public static GameObject CreateOrUpdateGatewayPrefab()
        {
            GameObject glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GatewayGlbPath);
            if (glbAsset == null)
            {
                Debug.LogError($"[CampusEnvironmentSetup] Could not find model at '{GatewayGlbPath}'!");
                return null;
            }

            if (!Directory.Exists(GatewayMaterialDir)) Directory.CreateDirectory(GatewayMaterialDir);
            Material glassDark = GetReflectiveGlassVariant(glbAsset, "GA_GlassDark");
            Material glassCurtain = GetReflectiveGlassVariant(glbAsset, "GA_GlassCurtain");

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset);
            root.name = "Gateway";
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one; // scale is applied per scene instance

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                GameObject obj = mf.gameObject;
                obj.layer = 0; // never Layer 31 Teleport
                GameObjectUtility.SetStaticEditorFlags(obj,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.ContributeGI);

                string n = obj.name.ToLowerInvariant();
                bool isAtrium = n.StartsWith("ga_glassatrium");
                bool solid = n.StartsWith("ga_building") || n.StartsWith("ga_canopy") ||
                             n.StartsWith("ga_columns") || n.StartsWith("ga_frontcolumn");

                MeshRenderer mr = obj.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    // Walls, canopy and columns cast shadows; the transparent atrium glass does not.
                    mr.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.On
                                                 : UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = true;

                    Material[] mats = mr.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        if (mats[i].name == "GA_GlassDark" && glassDark != null) { mats[i] = glassDark; changed = true; }
                        else if (mats[i].name == "GA_GlassCurtain" && glassCurtain != null) { mats[i] = glassCurtain; changed = true; }
                    }
                    if (changed) mr.sharedMaterials = mats;
                }

                // Colliders: building, canopy, columns, five front columns. None on the glass atrium (no interior yet),
                // so the entrance doorway stays open and walkable.
                MeshCollider existing = obj.GetComponent<MeshCollider>();
                if (solid && !isAtrium)
                {
                    if (existing == null) existing = obj.AddComponent<MeshCollider>();
                    existing.sharedMesh = mf.sharedMesh;
                    existing.convex = false;
                }
                else if (existing != null)
                {
                    Object.DestroyImmediate(existing);
                }
            }

            string dir = Path.GetDirectoryName(GatewayPrefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, GatewayPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CampusEnvironmentSetup] Generated Gateway prefab at '{GatewayPrefabPath}'.");
            return prefab;
        }

        // Copies the glTFast glass material into a project asset and makes it glossy so the reflection probe shows.
        private static Material GetReflectiveGlassVariant(GameObject glb, string sourceName)
        {
            string path = $"{GatewayMaterialDir}/{sourceName}_Reflective.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Material src = null;
            foreach (Renderer r in glb.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == sourceName) { src = m; break; }
            if (src == null) { Debug.LogWarning($"[CampusEnvironmentSetup] Material '{sourceName}' not found in Gateway GLB."); return null; }

            Material copy = new Material(src) { name = sourceName + "_Reflective" };
            if (copy.HasProperty("metallicFactor")) copy.SetFloat("metallicFactor", 0.6f);
            if (copy.HasProperty("roughnessFactor")) copy.SetFloat("roughnessFactor", 0.08f);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        /// <summary>Instantiates the Gateway prefab under the campus environment, hiding the old GA_GB massing box.
        /// Used by the scene builder and by the "Place Gateway" menu item.</summary>
        public static GameObject PlaceGateway(Transform envInstance)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GatewayPrefabPath);
            if (prefab == null) prefab = CreateOrUpdateGatewayPrefab();
            if (prefab == null) return null;

            foreach (Transform t in envInstance.GetComponentsInChildren<Transform>(true))
                if (t.name == "GA_GB" && t.GetComponent<MeshFilter>() != null) t.gameObject.SetActive(false);

            GameObject gateway = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            gateway.name = "Gateway";
            gateway.transform.SetParent(envInstance, true); // same parent as the other campus buildings
            gateway.transform.position = GatewayPosition;
            gateway.transform.rotation = GatewayRotation;
            gateway.transform.localScale = Vector3.one * GatewayWorldScale / envInstance.lossyScale.x;
            return gateway;
        }

        [MenuItem("BMU VR Tour/Place Gateway In Open Exterior Scene", false, 13)]
        public static void PlaceGatewayInOpenScene()
        {
            GameObject env = GameObject.Find("Campus_Exterior_Environment");
            if (env == null) { Debug.LogError("[CampusEnvironmentSetup] Open Scene_Campus_Exterior first."); return; }
            foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
                if (t.name == "Gateway" && t.parent == env.transform) { Debug.LogWarning("[CampusEnvironmentSetup] Gateway already placed."); return; }
            PlaceGateway(env.transform);
        }

        private static bool IsNorthBlockColliderPart(Transform t, Transform root)
        {
            // glTFast may put multi-material meshes on child nodes, so check ancestors too.
            for (; t != null && t != root; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                foreach (string part in NorthBlockColliderParts)
                {
                    if (n.StartsWith(part)) return true;
                }
            }
            return false;
        }

        private static void ConfigureEnvironmentObject(GameObject root)
        {
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in meshFilters)
            {
                GameObject obj = mf.gameObject;
                obj.layer = 0; // Layer 0: Default (never Layer 31 Teleport)

                // Mark static for VR batching
                GameObjectUtility.SetStaticEditorFlags(obj,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.ContributeGI);

                string nameLower = obj.name.ToLower();

                // Add colliders to walkable ground, roads, and walls
                if (nameLower.Contains("grassblade"))
                {
                    // Visual foliage only - no collider
                    continue;
                }

                if (nameLower.Contains("road") ||
                    nameLower.Contains("path") ||
                    nameLower.Contains("lawn") ||
                    nameLower.Contains("ground") ||
                    nameLower.Contains("mud") ||
                    nameLower.Contains("soil") ||
                    nameLower.Contains("wall") ||
                    nameLower.Contains("divider") ||
                    nameLower.Contains("gate") ||
                    nameLower.Contains("building") ||
                    nameLower.Contains("bsh") ||
                    nameLower.Contains("ga_gb") ||
                    nameLower.Contains("housing") ||
                    nameLower.Contains("grill"))
                {
                    MeshCollider col = obj.GetComponent<MeshCollider>();
                    if (col == null)
                    {
                        col = obj.AddComponent<MeshCollider>();
                    }
                    col.sharedMesh = mf.sharedMesh;
                    col.convex = false;
                }
            }
        }
    }
}
