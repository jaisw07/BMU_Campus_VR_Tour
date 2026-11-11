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
