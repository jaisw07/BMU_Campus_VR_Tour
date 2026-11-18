using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Imports the ambientCG (CC0) PBR texture sets in Assets/Art/Textures/Ground, composes a Unity mask map per set
    /// (R = metallic 0, G = ambient occlusion, B = height, A = smoothness; used by both Terrain Layers and URP Lit),
    /// and builds Terrain Layers / URP Lit materials from them. See Assets/Art/ASSET_SOURCES.md.
    /// </summary>
    public static class RealismTextures
    {
        public const string GroundDir = "Assets/Art/Textures/Ground";
        public const string LayerDir = "Assets/Art/Terrain/Layers";
        public const string MatDir = "Assets/Materials/Campus";

        static string Tex(string set, string map) { return GroundDir + "/" + set + "/" + set + "_2K-JPG_" + map + ".jpg"; }
        public static string MaskPath(string set) { return GroundDir + "/" + set + "/" + set + "_Mask.png"; }

        public static bool SetExists(string set) { return File.Exists(Tex(set, "Color")); }

        /// <summary>Sets import settings for every texture of a set and (re)creates its mask map.</summary>
        public static void ImportSet(string set)
        {
            foreach (string map in new[] { "Color", "NormalGL", "Roughness", "AmbientOcclusion", "Displacement" })
            {
                string path = Tex(set, map);
                if (!File.Exists(path)) continue;
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool linear = map != "Color";
                TextureImporterType type = map == "NormalGL" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type || ti.sRGBTexture == linear || ti.anisoLevel != 8 || !ti.mipmapEnabled)
                {
                    ti.textureType = type;
                    ti.sRGBTexture = !linear;
                    ti.mipmapEnabled = true;
                    ti.anisoLevel = 8;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.maxTextureSize = 2048;
                    ti.textureCompression = TextureImporterCompression.Compressed;
                    ti.SaveAndReimport();
                }
            }
            EnsureMask(set);
        }

        static Texture2D Load(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        static void EnsureMask(string set)
        {
            string outPath = MaskPath(set);
            if (File.Exists(outPath)) { ConfigureMask(outPath); return; }
            Texture2D ao = Load(Tex(set, "AmbientOcclusion")), rough = Load(Tex(set, "Roughness")), disp = Load(Tex(set, "Displacement"));
            const int n = 1024;
            var mask = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float a = ao != null ? ao.GetPixelBilinear(u, v).r : 1f;
                    float r = rough != null ? rough.GetPixelBilinear(u, v).r : 0.8f;
                    float h = disp != null ? disp.GetPixelBilinear(u, v).r : 0.5f;
                    px[y * n + x] = new Color32(0, (byte)(a * 255f), (byte)(h * 255f), (byte)((1f - r) * 255f));
                }
            mask.SetPixels32(px);
            File.WriteAllBytes(outPath, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            if (ao) Object.DestroyImmediate(ao); if (rough) Object.DestroyImmediate(rough); if (disp) Object.DestroyImmediate(disp);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureMask(outPath);
        }

        static void ConfigureMask(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            if (ti.sRGBTexture || !ti.mipmapEnabled)
            {
                ti.sRGBTexture = false;
                ti.mipmapEnabled = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
        }

        public static Texture2D ColorMap(string set) { return AssetDatabase.LoadAssetAtPath<Texture2D>(Tex(set, "Color")); }
        public static Texture2D NormalMap(string set) { return AssetDatabase.LoadAssetAtPath<Texture2D>(Tex(set, "NormalGL")); }
        public static Texture2D MaskMap(string set) { return AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath(set)); }

        /// <summary>Creates / updates a Terrain Layer asset for a texture set. tileMetres = real size of one texture repeat.</summary>
        public static TerrainLayer Layer(string set, float tileMetres, float smoothness = 0.5f)
        {
            if (!Directory.Exists(LayerDir)) { Directory.CreateDirectory(LayerDir); AssetDatabase.Refresh(); }
            string path = LayerDir + "/TL_" + set + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
            layer.diffuseTexture = ColorMap(set);
            layer.normalMapTexture = NormalMap(set);
            layer.maskMapTexture = MaskMap(set);
            layer.tileSize = new Vector2(tileMetres, tileMetres);
            layer.normalScale = 1f;
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>URP Lit material from a texture set (world-UV meshes: tiling comes from the mesh UVs, so tile stays 1).</summary>
        public static Material LitMaterial(string name, string set, Color tint, float smoothnessScale = 1f)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", ColorMap(set));
            mat.SetColor("_BaseColor", tint);
            mat.SetTexture("_BumpMap", NormalMap(set));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            var mask = MaskMap(set);
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetTexture("_OcclusionMap", mask);
            mat.SetFloat("_Smoothness", smoothnessScale);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Untextured URP Lit material (colour only).</summary>
        public static Material PlainMaterial(string name, Color tint, float smoothness, float metallic = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", tint); mat.SetFloat("_Smoothness", smoothness); mat.SetFloat("_Metallic", metallic);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("BMU VR Tour/Realism/Import Ground Texture Sets")]
        public static void ImportAll()
        {
            foreach (string set in AllSets) if (SetExists(set)) ImportSet(set);
            AssetDatabase.SaveAssets();
            Debug.Log("[Realism] ground texture sets imported.");
        }

        public static readonly string[] AllSets =
        {
            "Grass005", "Grass006", "Grass004", "Grass007", "Ground054", "Ground103", "Ground106", "Ground108",
            "Asphalt031", "PavingStones128", "Concrete034", "Concrete031",
        };
    }
}
