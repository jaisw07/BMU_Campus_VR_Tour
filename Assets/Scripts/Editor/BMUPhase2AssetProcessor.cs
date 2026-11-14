using System.IO;
using UnityEngine;
using UnityEditor;

namespace BMU.VRTour.Editor
{
    public static class BMUPhase2AssetProcessor
    {
        [MenuItem("BMU VR Tour/Execute Phase 2 Setup", false, 51)]
        public static void ExecutePhase2Setup()
        {
            Debug.Log("[Phase 2] Starting Glassmorphism & Token Alignment Setup...");

            string[] targetDirs = new string[] { "Assets/Textures/UI", "Assets/Resources/UI" };
            int configuredCount = 0;

            foreach (string uiDir in targetDirs)
            {
                if (!Directory.Exists(uiDir)) continue;

                string[] files = Directory.GetFiles(uiDir, "*.png");
                foreach (string file in files)
                {
                string assetPath = file.Replace('\\', '/');
                TextureImporter uiImp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (uiImp == null)
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    uiImp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                }
                if (uiImp == null) continue;

                uiImp.textureType = TextureImporterType.Sprite;
                uiImp.spriteImportMode = SpriteImportMode.Single;
                uiImp.alphaIsTransparency = true;
                uiImp.mipmapEnabled = false;
                uiImp.wrapMode = TextureWrapMode.Clamp;
                uiImp.filterMode = FilterMode.Bilinear;

                string fn = Path.GetFileNameWithoutExtension(assetPath);

                if (fn.Contains("circle"))
                {
                    uiImp.spriteBorder = Vector4.zero;
                }
                else if (fn.Contains("glow"))
                {
                    uiImp.spriteBorder = new Vector4(24, 24, 24, 24);
                }
                else if (fn.Contains("pill"))
                {
                    uiImp.spriteBorder = new Vector4(24, 24, 24, 24);
                }
                else if (fn.Contains("26"))
                {
                    uiImp.spriteBorder = new Vector4(36, 36, 36, 36);
                }
                else if (fn.Contains("24"))
                {
                    uiImp.spriteBorder = new Vector4(32, 32, 32, 32);
                }
                else if (fn.Contains("20"))
                {
                    uiImp.spriteBorder = new Vector4(30, 30, 30, 30);
                }
                else if (fn.Contains("16"))
                {
                    uiImp.spriteBorder = new Vector4(24, 24, 24, 24);
                }
                else if (fn.Contains("12"))
                {
                    uiImp.spriteBorder = new Vector4(20, 20, 20, 20);
                }
                else if (fn.Contains("8"))
                {
                    uiImp.spriteBorder = new Vector4(16, 16, 16, 16);
                }

                    EditorUtility.SetDirty(uiImp);
                    uiImp.SaveAndReimport();
                    configuredCount++;
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Phase 2] Successfully configured and reimported {configuredCount} UI sprites across target directories.");
        }
    }
}
