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
        // Real-size model exported at 2x (like the campus GLB), with the recessed double door (NB_Main_Door).
        // The prefab keeps the raw 2x size; the scene builder applies the campus scale (0.5 x 3.5 = 1.75).
        private const string NorthBlockGlbPath = "Assets/Models/Exterior/NB_Building.glb";
        public const string NorthBlockPrefabPath = "Assets/Prefabs/Environment/Prefab_Exterior_NorthBlock.prefab";

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

            string dir = Path.GetDirectoryName(NorthBlockPrefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, NorthBlockPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CampusEnvironmentSetup] Generated North Block prefab at '{NorthBlockPrefabPath}'.");
            return prefab;
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
