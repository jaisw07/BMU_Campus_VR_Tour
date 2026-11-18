using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Lighting and atmosphere for the outdoor campus (PC VR target):
    ///  - Polyhaven HDRI (CC0) skybox + ambient, sun direction taken from the brightest pixel of the HDRI,
    ///  - a global post-processing Volume (ACES, mild bloom, warm hazy grade), exponential-squared haze,
    ///  - the PC quality level's URP asset/renderer (HDR, MSAA, longer shadows) with the Decal and SSAO features.
    /// The Quest (Mobile) quality level is left untouched; none of this is meant for standalone Quest.
    /// </summary>
    public static class RealismAtmosphere
    {
        public const string HdrPath = "Assets/Art/Sky/kloofendal_43d_clear_puresky_2k.hdr";
        const string SkyMatPath = "Assets/Art/Sky/M_Campus_Sky.mat";
        const string VolumePath = "Assets/Settings/Campus_Exterior_Volume.asset";
        const string PcAssetPath = "Assets/Settings/PC_RPAsset.asset";

        // ------------------------------------------------------------------ sun from the HDRI
        public static Vector3 SunDirection()
        {
            var ti = AssetImporter.GetAtPath(HdrPath) as TextureImporter;
            if (ti == null) return new Vector3(-0.45f, 0.68f, 0.58f).normalized;
            ti.isReadable = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HdrPath);
            Vector3 dir = new Vector3(-0.45f, 0.68f, 0.58f).normalized;
            try
            {
                var px = tex.GetPixels();
                int w = tex.width, h = tex.height; float best = 0f; int bi = 0;
                for (int i = 0; i < px.Length; i++) { float l = px[i].r + px[i].g + px[i].b; if (l > best) { best = l; bi = i; } }
                double sx = 0, sy = 0, sw = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float l = px[i].r + px[i].g + px[i].b;
                    if (l < best * 0.85f) continue;
                    // only pixels near the peak (avoid wrapping artefacts)
                    int x = i % w, y = i / w, bx = bi % w, by = bi / w;
                    if (Mathf.Abs(x - bx) > 40 || Mathf.Abs(y - by) > 40) continue;
                    sx += x * l; sy += y * l; sw += l;
                }
                float u = (float)(sx / sw + 0.5) / w, v = (float)(sy / sw + 0.5) / h;
                float lon = (u - 0.5f) * 2f * Mathf.PI, lat = (v - 0.5f) * Mathf.PI;
                dir = new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(lon) * Mathf.Cos(lat)).normalized;
                Debug.Log("[RealismAtmosphere] HDRI sun peak " + best + " at uv (" + u.ToString("F3") + "," + v.ToString("F3") + ") -> dir " + dir);
            }
            catch (System.Exception e) { Debug.LogWarning("[RealismAtmosphere] could not read HDRI pixels: " + e.Message); }
            ti.isReadable = false; ti.textureCompression = TextureImporterCompression.Compressed; ti.SaveAndReimport();
            return dir;
        }

        public static Material SkyboxMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
            if (mat == null) { mat = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(mat, SkyMatPath); }
            mat.shader = Shader.Find("Skybox/Panoramic");
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HdrPath));
            mat.SetFloat("_Exposure", 1.05f);
            mat.SetFloat("_Rotation", 0f);
            mat.SetColor("_Tint", new Color(0.55f, 0.55f, 0.55f, 1f)); // 0.5 grey = neutral for the Panoramic shader; slight blue-grey haze
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ post-processing volume
        public static VolumeProfile VolumeProfileAsset()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            var tone = GetOrAdd<Tonemapping>(profile); tone.mode.Override(TonemappingMode.ACES);
            var bloom = GetOrAdd<Bloom>(profile); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.18f); bloom.scatter.Override(0.65f); bloom.tint.Override(new Color(1f, 0.96f, 0.9f));
            var ca = GetOrAdd<ColorAdjustments>(profile);
            ca.postExposure.Override(0.1f); ca.contrast.Override(8f); ca.saturation.Override(-8f); ca.colorFilter.Override(new Color(1f, 0.975f, 0.93f));
            var wb = GetOrAdd<WhiteBalance>(profile); wb.temperature.Override(8f); wb.tint.Override(-2f);
            var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.92f, 0.96f, 1.04f, 0f)); smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.94f, 0f));
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T GetOrAdd<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet(out T c)) return c;
            c = p.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        // ------------------------------------------------------------------ URP asset / renderer (PC quality level only)
        public static void SetupRenderer()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath);
            if (urp == null) { Debug.LogWarning("[RealismAtmosphere] " + PcAssetPath + " not found"); return; }
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 90f;
            urp.shadowCascadeCount = 4;
            urp.mainLightShadowmapResolution = 4096;
            urp.useSRPBatcher = true;
            var so = new SerializedObject(urp);
            SetBool(so, "m_SoftShadowsSupported", true);
            var split = so.FindProperty("m_Cascade4Split"); if (split != null) split.vector3Value = new Vector3(0.06f, 0.18f, 0.42f);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);

            var list = so.FindProperty("m_RendererDataList");
            var rd = list.GetArrayElementAtIndex(0).objectReferenceValue as UniversalRendererData;
            if (rd != null)
            {
                bool hasDecal = false;
                foreach (var f in rd.rendererFeatures) { if (f is DecalRendererFeature) hasDecal = true; if (f is ScreenSpaceAmbientOcclusion) ConfigureSsao(f); }
                if (!hasDecal) AddFeature(rd, ScriptableObject.CreateInstance<DecalRendererFeature>(), "Decal");
                EditorUtility.SetDirty(rd);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[RealismAtmosphere] PC URP asset: HDR, MSAA x4, shadow distance 90 m, 4 cascades @ 4096, Decal + SSAO features.");
        }

        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }

        static void ConfigureSsao(ScriptableRendererFeature f)
        {
            f.SetActive(true);
            var so = new SerializedObject(f);
            var s = so.FindProperty("m_Settings");
            if (s != null)
            {
                SetInt(s, "Source", 0);          // Depth (the foliage shader has no DepthNormals pass)
                SetFloat(s, "Intensity", 0.55f); SetFloat(s, "Radius", 0.32f); SetFloat(s, "Falloff", 80f);
                SetBool2(s, "DownSample", true);
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(f);
        }

        static void SetInt(SerializedProperty parent, string n, int v) { var p = parent.FindPropertyRelative(n); if (p != null) p.intValue = v; }
        static void SetFloat(SerializedProperty parent, string n, float v) { var p = parent.FindPropertyRelative(n); if (p != null) p.floatValue = v; }
        static void SetBool2(SerializedProperty parent, string n, bool v) { var p = parent.FindPropertyRelative(n); if (p != null) p.boolValue = v; }

        static void AddFeature(UniversalRendererData rd, ScriptableRendererFeature feature, string name)
        {
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, rd);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string guid, out long localId);
            var so = new SerializedObject(rd);
            var features = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++; features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ scene objects (called from BuildExteriorScene)
        public static void Build()
        {
            SetupRenderer();
            Vector3 sunDir = SunDirection();
            Material sky = SkyboxMaterial();

            var sunGo = GameObject.Find("Exterior_Directional_Light");
            Light sun = null;
            if (sunGo != null)
            {
                sun = sunGo.GetComponent<Light>();
                sunGo.transform.rotation = Quaternion.LookRotation(-sunDir);
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.955f, 0.86f);          // warm, slightly hazy afternoon sun
                sun.intensity = 2.1f;
                sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.92f;
                sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
                sun.lightmapBakeType = LightmapBakeType.Mixed;
            }

            var atmos = new GameObject("Campus_Atmosphere");
            var comp = atmos.AddComponent<CampusAtmosphere>();
            var so = new SerializedObject(comp);
            so.FindProperty("m_Skybox").objectReferenceValue = sky;
            so.FindProperty("m_Sun").objectReferenceValue = sun;
            so.FindProperty("m_AmbientIntensity").floatValue = 1.25f;
            so.ApplyModifiedProperties();

            var volGo = new GameObject("Campus_Exterior_Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 5; vol.weight = 1f;
            vol.sharedProfile = VolumeProfileAsset();

            var probe = GameObject.Find("Campus_Exterior_ReflectionProbe");
            if (probe != null)
            {
                var rp = probe.GetComponent<ReflectionProbe>();
                rp.mode = ReflectionProbeMode.Realtime;
                rp.refreshMode = ReflectionProbeRefreshMode.OnAwake;     // one capture on load, not every frame
                rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                rp.resolution = 256; rp.hdr = true; rp.intensity = 1f; rp.shadowDistance = 90f;
                rp.center = new Vector3(-480f, 30f, -130f); rp.size = new Vector3(900f, 120f, 700f);
            }
            comp.Apply();
        }

        [MenuItem("BMU VR Tour/Realism/Setup PC Renderer (HDR, MSAA, Decals, SSAO)")]
        public static void MenuSetupRenderer() { SetupRenderer(); }
    }
}
