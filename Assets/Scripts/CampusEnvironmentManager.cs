using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Manages the 360° photosphere environment surrounding the VR player.
    /// Provides smooth cross-fades, sky/ground panoramic spherical projection,
    /// and clean modular hooks for swapping to full 3D models at a later stage.
    /// </summary>
    public class CampusEnvironmentManager : MonoBehaviour
    {
        private static CampusEnvironmentManager s_Instance;
        public static CampusEnvironmentManager Instance => s_Instance;

        [Header("360 Photosphere Geometry")]
        [SerializeField] private MeshRenderer m_PhotosphereRenderer;
        [SerializeField] private Material m_SphereMaterialTemplate;
        [SerializeField] private float m_SphereRadius = 45f;

        [System.Serializable]
        public class ZoneEnvironmentData
        {
            public string zoneKey;
            public string displayName;
            public Color skyTopColor = new Color(0.09f, 0.39f, 0.62f);
            public Color skyHorizonColor = new Color(0.49f, 0.74f, 0.91f);
            public Color groundColor = new Color(0.22f, 0.45f, 0.27f);
            public Texture2D panoramaTexture;
        }

        [Header("Zone Environments")]
        [SerializeField] private List<ZoneEnvironmentData> m_ZonePresets = new List<ZoneEnvironmentData>();

        private Material m_ActiveSphereMaterial;
        private Coroutine m_FadeRoutine;
        private string m_CurrentZoneKey = "";

        private void Awake()
        {
            if (s_Instance == null) s_Instance = this;
            else { Destroy(gameObject); return; }

            InitializePhotosphere();
            SetupDefaultPresets();
        }

        private void InitializePhotosphere()
        {
            if (m_PhotosphereRenderer == null)
            {
                // Create an inverted sphere if not assigned
                GameObject sphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphereObj.name = "360_Photosphere";
                sphereObj.transform.SetParent(transform);
                sphereObj.transform.localPosition = Vector3.zero;
                sphereObj.transform.localScale = new Vector3(-m_SphereRadius, m_SphereRadius, m_SphereRadius); // Invert normals

                Collider col = sphereObj.GetComponent<Collider>();
                if (col != null) Destroy(col);

                m_PhotosphereRenderer = sphereObj.GetComponent<MeshRenderer>();
            }

            // Shader: Unlit / Skybox / Texture with inverted cull
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Mobile/Unlit (Supports Lightmap)");

            m_ActiveSphereMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
            m_ActiveSphereMaterial.name = "Photosphere_Runtime_Mat";
            m_PhotosphereRenderer.material = m_ActiveSphereMaterial;
        }

        private void SetupDefaultPresets()
        {
            if (m_ZonePresets.Count > 0) return;

            // 1. Front Gate
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "front-gate",
                displayName = "Front Gate & Main Boulevard",
                skyTopColor = new Color(0.09f, 0.39f, 0.62f),
                skyHorizonColor = new Color(0.49f, 0.74f, 0.91f),
                groundColor = new Color(0.22f, 0.45f, 0.27f)
            });

            // 2. Outside North Block
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "outside-north-block",
                displayName = "Outside North Block",
                skyTopColor = new Color(0.11f, 0.42f, 0.64f),
                skyHorizonColor = new Color(0.53f, 0.76f, 0.92f),
                groundColor = new Color(0.27f, 0.45f, 0.32f)
            });

            // 3. North Block - Classroom
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "nb-classroom",
                displayName = "North Block: Smart Classroom",
                skyTopColor = new Color(0.77f, 0.86f, 0.94f),
                skyHorizonColor = new Color(0.86f, 0.92f, 0.97f),
                groundColor = new Color(0.66f, 0.72f, 0.78f)
            });

            // 4. North Block - Lab
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "nb-lab",
                displayName = "North Block: Engineering Lab",
                skyTopColor = new Color(0.72f, 0.83f, 0.93f),
                skyHorizonColor = new Color(0.83f, 0.90f, 0.96f),
                groundColor = new Color(0.55f, 0.63f, 0.70f)
            });

            // 5. North Block - Faculty
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "nb-faculty",
                displayName = "North Block: Faculty Cabins",
                skyTopColor = new Color(0.80f, 0.88f, 0.95f),
                skyHorizonColor = new Color(0.89f, 0.93f, 0.97f),
                groundColor = new Color(0.62f, 0.68f, 0.74f)
            });

            // 6. Outside Gateway
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "outside-gateway",
                displayName = "Outside Gateway Monument",
                skyTopColor = new Color(0.10f, 0.41f, 0.63f),
                skyHorizonColor = new Color(0.51f, 0.75f, 0.92f),
                groundColor = new Color(0.29f, 0.47f, 0.33f)
            });

            // 7. Gateway - Classroom
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "gw-classroom",
                displayName = "Gateway: Executive Classroom",
                skyTopColor = new Color(0.77f, 0.87f, 0.95f),
                skyHorizonColor = new Color(0.86f, 0.91f, 0.96f),
                groundColor = new Color(0.65f, 0.71f, 0.77f)
            });

            // 8. Gateway - ACIC Lab
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "gw-lab",
                displayName = "Gateway: ACIC Incubator",
                skyTopColor = new Color(0.74f, 0.85f, 0.94f),
                skyHorizonColor = new Color(0.84f, 0.91f, 0.97f),
                groundColor = new Color(0.58f, 0.65f, 0.72f)
            });

            // 9. Outside Mechanical Workshop
            m_ZonePresets.Add(new ZoneEnvironmentData
            {
                zoneKey = "outside-workshop",
                displayName = "Outside Mechanical Workshop",
                skyTopColor = new Color(0.15f, 0.42f, 0.61f),
                skyHorizonColor = new Color(0.55f, 0.77f, 0.92f),
                groundColor = new Color(0.33f, 0.43f, 0.35f)
            });
        }

        /// <summary>
        /// Transitions to a given campus zone photosphere.
        /// </summary>
        public void SwitchToZone(string zoneKey, Action onDone = null)
        {
            if (m_CurrentZoneKey == zoneKey)
            {
                onDone?.Invoke();
                return;
            }

            m_CurrentZoneKey = zoneKey;
            ZoneEnvironmentData data = m_ZonePresets.Find(z => z.zoneKey == zoneKey);
            if (data == null && zoneKey.StartsWith("ws-"))
            {
                // The workshop interior is real 3D with no photosphere preset: leave the environment untouched.
                onDone?.Invoke();
                return;
            }

            if (data == null)
            {
                data = m_ZonePresets.Count > 0 ? m_ZonePresets[0] : null;
            }

            if (data != null)
            {
                ApplyEnvironmentData(data);
            }

            onDone?.Invoke();
        }

        private void ApplyEnvironmentData(ZoneEnvironmentData data)
        {
            if (m_ActiveSphereMaterial == null) return;

            if (data.panoramaTexture != null)
            {
                m_ActiveSphereMaterial.mainTexture = data.panoramaTexture;
                m_ActiveSphereMaterial.color = Color.white;
            }
            else
            {
                // Generate procedural 360 gradient texture
                Texture2D gradTex = GenerateEquirectangularGradient(data.skyTopColor, data.skyHorizonColor, data.groundColor);
                m_ActiveSphereMaterial.mainTexture = gradTex;
                m_ActiveSphereMaterial.color = Color.white;
            }
        }

        private Texture2D GenerateEquirectangularGradient(Color skyTop, Color skyHorizon, Color ground)
        {
            int width = 512;
            int height = 256;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (float)y / height; // 0 = south pole (ground), 0.5 = horizon, 1 = north pole (zenith)
                Color c;
                if (v < 0.48f)
                {
                    // Ground to lower horizon
                    float t = v / 0.48f;
                    c = Color.Lerp(ground, Color.Lerp(ground, skyHorizon, 0.5f), t);
                }
                else if (v < 0.54f)
                {
                    // Horizon blend
                    float t = (v - 0.48f) / 0.06f;
                    c = Color.Lerp(Color.Lerp(ground, skyHorizon, 0.5f), skyHorizon, t);
                }
                else
                {
                    // Horizon to Zenith
                    float t = (v - 0.54f) / 0.46f;
                    c = Color.Lerp(skyHorizon, skyTop, t);
                }

                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
