using UnityEngine;
using UnityEngine.Rendering;

namespace BMU.VRTour
{
    /// <summary>
    /// Sky, ambient light, haze and sun for the outdoor campus. Lives in Scene_Campus_Exterior, so it only applies while the
    /// exterior is loaded: it sets RenderSettings on enable and restores them on disable, and switches off the Master scene's
    /// directional light so the exterior sun is the only main light. Lighting values are authored by
    /// BMU VR Tour &gt; Realism (RealismAtmosphere.cs) from the Polyhaven HDRI (CC0) and the reference photos.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CampusAtmosphere : MonoBehaviour
    {
        [Header("Sky")]
        [SerializeField] private Material m_Skybox;
        [SerializeField, Range(0f, 2f)] private float m_AmbientIntensity = 1.0f;

        [Header("Haze (hazy North-India daytime)")]
        [SerializeField] private bool m_Fog = true;
        [SerializeField] private Color m_FogColor = new Color(0.74f, 0.78f, 0.82f);
        [SerializeField, Range(0f, 0.01f)] private float m_FogDensity = 0.0007f;

        [Header("Sun")]
        [SerializeField] private Light m_Sun;
        [SerializeField] private bool m_DisableOtherDirectionalLights = true;

        private Material m_PrevSkybox;
        private UnityEngine.Rendering.AmbientMode m_PrevAmbientMode;
        private float m_PrevAmbientIntensity;
        private bool m_PrevFog; private Color m_PrevFogColor; private FogMode m_PrevFogMode; private float m_PrevFogDensity;
        private Light m_PrevSun;
        private readonly System.Collections.Generic.List<Light> m_Disabled = new System.Collections.Generic.List<Light>();
        private bool m_Applied;

        private void OnEnable() { Apply(); }
        private void OnDisable() { Restore(); }
        private void OnValidate() { if (isActiveAndEnabled) Apply(); }

        public void Apply()
        {
            if (!m_Applied)
            {
                m_PrevSkybox = RenderSettings.skybox; m_PrevAmbientMode = RenderSettings.ambientMode; m_PrevAmbientIntensity = RenderSettings.ambientIntensity;
                m_PrevFog = RenderSettings.fog; m_PrevFogColor = RenderSettings.fogColor; m_PrevFogMode = RenderSettings.fogMode; m_PrevFogDensity = RenderSettings.fogDensity;
                m_PrevSun = RenderSettings.sun;
                m_Applied = true;
            }
            if (m_Skybox != null) RenderSettings.skybox = m_Skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = m_AmbientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = m_Fog;
            RenderSettings.fogColor = m_FogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = m_FogDensity;
            if (m_Sun != null) RenderSettings.sun = m_Sun;
            if (m_DisableOtherDirectionalLights)
            {
                m_Disabled.Clear();
                foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (l != m_Sun && l.type == LightType.Directional && l.gameObject.scene != gameObject.scene && l.enabled) { l.enabled = false; m_Disabled.Add(l); }
            }
            DynamicGI.UpdateEnvironment();
        }

        private void Restore()
        {
            if (!m_Applied) return;
            RenderSettings.skybox = m_PrevSkybox; RenderSettings.ambientMode = m_PrevAmbientMode; RenderSettings.ambientIntensity = m_PrevAmbientIntensity;
            RenderSettings.fog = m_PrevFog; RenderSettings.fogColor = m_PrevFogColor; RenderSettings.fogMode = m_PrevFogMode; RenderSettings.fogDensity = m_PrevFogDensity;
            RenderSettings.sun = m_PrevSun;
            foreach (Light l in m_Disabled) if (l != null) l.enabled = true;
            m_Disabled.Clear();
            m_Applied = false;
            DynamicGI.UpdateEnvironment();
        }
    }
}
