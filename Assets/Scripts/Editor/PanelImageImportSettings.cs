using UnityEditor;
using UnityEngine;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Import settings for Assets/Art/PanelImages: Sprite (2D and UI), max 2048, normal-quality compression,
    /// no mipmaps. PNG hub cards keep their alpha so the rounded corners survive.
    /// </summary>
    public class PanelImageImportSettings : AssetPostprocessor
    {
        private const string Folder = "Assets/Art/PanelImages/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;

            var imp = (TextureImporter)assetImporter;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Compressed; // "Normal Quality"
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = assetPath.EndsWith(".png");
        }

        [MenuItem("BMU VR Tour/Reimport Panel Images", false, 22)]
        public static void ReimportAll()
        {
            AssetDatabase.ImportAsset("Assets/Art/PanelImages", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }
    }
}
