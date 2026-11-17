using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace BMU.VRTour.Editor
{
    public static class CampusSkyboxSetup
    {
        public const string SkyboxTexturePath = "Assets/Textures/Environment/skybox_exterior_panorama.jpg";
        public const string SkyboxMaterialPath = "Assets/Materials/Environment/M_Campus_Exterior_Skybox.mat";
        public const string ExteriorScenePath = "Assets/Scenes/Scene_Campus_Exterior.unity";

        [MenuItem("BMU VR Tour/Configure and Apply Exterior Skybox", false, 13)]
        public static void SetupAndApplyExteriorSkybox()
        {
            Material skyboxMat = CreateOrUpdateSkyboxMaterial();
            if (skyboxMat == null)
            {
                Debug.LogError("[CampusSkyboxSetup] Failed to create or load skybox material.");
                return;
            }

            ApplySkyboxToExteriorScene(skyboxMat);
        }

        public static Material CreateOrUpdateSkyboxMaterial()
        {
            // Configure Texture Importer
            TextureImporter importer = AssetImporter.GetAtPath(SkyboxTexturePath) as TextureImporter;
            if (importer != null)
            {
                bool dirty = false;
                if (importer.wrapModeU != TextureWrapMode.Repeat)
                {
                    importer.wrapModeU = TextureWrapMode.Repeat;
                    dirty = true;
                }
                if (importer.wrapModeV != TextureWrapMode.Clamp)
                {
                    importer.wrapModeV = TextureWrapMode.Clamp;
                    dirty = true;
                }
                if (importer.maxTextureSize < 2048)
                {
                    importer.maxTextureSize = 2048;
                    dirty = true;
                }
                if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
                {
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    dirty = true;
                }

                if (dirty)
                {
                    importer.SaveAndReimport();
                }
            }

            Texture2D skyboxTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyboxTexturePath);
            if (skyboxTex == null)
            {
                Debug.LogError($"[CampusSkyboxSetup] Could not find skybox texture at '{SkyboxTexturePath}'!");
                return null;
            }

            // Ensure destination material directory exists
            string dir = Path.GetDirectoryName(SkyboxMaterialPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Find Panoramic skybox shader
            Shader panoramicShader = Shader.Find("Skybox/Panoramic");
            if (panoramicShader == null)
            {
                Debug.LogError("[CampusSkyboxSetup] 'Skybox/Panoramic' shader not found!");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (mat == null)
            {
                mat = new Material(panoramicShader);
                AssetDatabase.CreateAsset(mat, SkyboxMaterialPath);
            }
            else
            {
                mat.shader = panoramicShader;
            }

            mat.SetTexture("_MainTex", skyboxTex);
            mat.SetFloat("_Mapping", 1.0f);    // 1 = Latitude Longitude Layout (Equirectangular)
            mat.SetFloat("_ImageType", 0.0f);  // 0 = 360 Degrees
            mat.SetFloat("_MirrorOnBack", 0.0f);
            mat.SetFloat("_Exposure", 1.15f);
            mat.SetFloat("_Rotation", 0.0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CampusSkyboxSetup] Skybox material successfully saved at '{SkyboxMaterialPath}'.");
            return mat;
        }

        public static void ApplySkyboxToExteriorScene(Material skyboxMat = null)
        {
            if (skyboxMat == null)
            {
                skyboxMat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            }

            if (skyboxMat == null)
            {
                Debug.LogError($"[CampusSkyboxSetup] Skybox material not found at '{SkyboxMaterialPath}'!");
                return;
            }

            UnityEngine.SceneManagement.Scene activeScene = EditorSceneManager.GetActiveScene();
            bool wasLoaded = activeScene.path == ExteriorScenePath;

            if (!wasLoaded)
            {
                if (activeScene.isDirty)
                {
                    EditorSceneManager.SaveScene(activeScene);
                }
                activeScene = EditorSceneManager.OpenScene(ExteriorScenePath, OpenSceneMode.Single);
            }

            // Apply RenderSettings
            RenderSettings.skybox = skyboxMat;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.reflectionIntensity = 1.0f;

            // Find directional light if present
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    RenderSettings.sun = l;
                    break;
                }
            }

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);

            Debug.Log($"[CampusSkyboxSetup] Successfully applied Skybox '{skyboxMat.name}' to '{ExteriorScenePath}'.");
        }
    }
}
