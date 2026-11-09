using System.IO;
using UnityEngine;
using UnityEditor;
using TMPro;

namespace BMU.VRTour.Editor
{
    public static class BMUPhase1AssetProcessor
    {
        [MenuItem("BMU VR Tour/Execute Phase 1 Setup", false, 50)]
        public static void ExecutePhase1Setup()
        {
            Debug.Log("[Phase 1] Starting Brand & Typography Foundation Setup...");

            // 1. Logo
            string logoPath = "Assets/Textures/bmu_logo.png";
            AssetDatabase.ImportAsset(logoPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(logoPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                Debug.Log("[Phase 1] Configured bmu_logo.png as Sprite2D.");
            }

            // 2. UI Sprites
            string uiDir = "Assets/Textures/UI";
            if (Directory.Exists(uiDir))
            {
                string[] files = Directory.GetFiles(uiDir, "*.png");
                foreach (string file in files)
                {
                    string assetPath = file.Replace('\\', '/');
                    TextureImporter uiImp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (uiImp == null) continue;

                    uiImp.textureType = TextureImporterType.Sprite;
                    uiImp.spriteImportMode = SpriteImportMode.Single;
                    uiImp.alphaIsTransparency = true;
                    uiImp.mipmapEnabled = false;
                    uiImp.wrapMode = TextureWrapMode.Clamp;
                    uiImp.filterMode = FilterMode.Bilinear;

                    string fn = Path.GetFileNameWithoutExtension(assetPath);
                    if (fn.Contains("24"))
                        uiImp.spriteBorder = new Vector4(32, 32, 32, 32);
                    else if (fn.Contains("16"))
                        uiImp.spriteBorder = new Vector4(24, 24, 24, 24);
                    else if (fn.Contains("8"))
                        uiImp.spriteBorder = new Vector4(16, 16, 16, 16);

                    EditorUtility.SetDirty(uiImp);
                    uiImp.SaveAndReimport();
                }
                Debug.Log($"[Phase 1] Configured UI sprites in {uiDir}.");
            }

            // 3. Photo Sprites
            string photoDir = "Assets/Textures/Photos";
            if (Directory.Exists(photoDir))
            {
                string[] files = Directory.GetFiles(photoDir, "*.jpg");
                foreach (string file in files)
                {
                    string assetPath = file.Replace('\\', '/');
                    TextureImporter pImp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (pImp == null) continue;

                    pImp.textureType = TextureImporterType.Sprite;
                    pImp.spriteImportMode = SpriteImportMode.Single;
                    pImp.alphaIsTransparency = false;
                    pImp.mipmapEnabled = true;
                    pImp.maxTextureSize = 2048;
                    pImp.wrapMode = TextureWrapMode.Clamp;
                    pImp.filterMode = FilterMode.Bilinear;

                    EditorUtility.SetDirty(pImp);
                    pImp.SaveAndReimport();
                }
                Debug.Log($"[Phase 1] Configured {files.Length} Unsplash photos in {photoDir}.");
            }

            // 4. Generate TMP Font Assets
            string fontsDir = "Assets/TextMesh Pro/Fonts";
            string[] fontFiles = Directory.GetFiles(fontsDir, "*.ttf");

            foreach (string file in fontFiles)
            {
                string assetPath = file.Replace('\\', '/');
                string fontName = Path.GetFileNameWithoutExtension(assetPath);
                if (fontName.Contains("Liberation")) continue;

                string sdfPath = $"{fontsDir}/{fontName} SDF.asset";
                TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
                if (existing != null) continue;

                Font font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                if (font == null) continue;

                TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font);
                if (fontAsset != null)
                {
                    AssetDatabase.CreateAsset(fontAsset, sdfPath);
                    if (fontAsset.material != null)
                    {
                        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                    }
                    if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                    {
                        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
                    }
                    EditorUtility.SetDirty(fontAsset);
                    Debug.Log($"[Phase 1] Created SDF Font: {sdfPath}");
                }
            }

            // 5. TMP Settings Default Font
            TMP_FontAsset pjsRegular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/PlusJakartaSans-Regular SDF.asset");
            if (pjsRegular != null && TMP_Settings.instance != null)
            {
                TMP_Settings.defaultFontAsset = pjsRegular;
                EditorUtility.SetDirty(TMP_Settings.instance);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Phase 1] Complete! All typography and brand assets are prepared and ready for UI integration.");
        }
    }
}
