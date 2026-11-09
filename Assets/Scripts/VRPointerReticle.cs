using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BMU.VRTour
{
    /// <summary>
    /// Renders a dynamic, high-fidelity circular targeting reticle at the exact contact point
    /// of the Meta Quest 3S light pointer on UI canvases and buttons.
    /// When pointing at a button, the reticle smoothly focuses and expands into a luminous
    /// electric blue feedback ring, giving immediate confirmation to the user.
    /// </summary>
    public class VRPointerReticle : MonoBehaviour
    {
        [Header("Reticle Size Settings")]
        [Tooltip("Reticle diameter in meters when hovering over a button.")]
        [SerializeField] private float m_ButtonHoverSize = 0.032f; // ~3.2 cm in VR

        [Tooltip("Reticle diameter in meters when hovering over general UI canvas.")]
        [SerializeField] private float m_CanvasHoverSize = 0.016f; // ~1.6 cm in VR

        [Tooltip("Smoothing speed for position and scale transitions.")]
        [SerializeField] private float m_SmoothingSpeed = 24f;

        [Header("Reticle Colors")]
        [SerializeField] private Color m_ButtonHoverColor = new Color(0f, 0.85f, 1f, 1f);      // Bright electric cyan
        [SerializeField] private Color m_CanvasHoverColor = new Color(0f, 0.50f, 0.95f, 0.75f);  // Soft luminous blue

        private NearFarInteractor m_NearFarInteractor;
        private GameObject m_ReticleObj;
        private MeshRenderer m_MeshRenderer;
        private Material m_Material;
        private Texture2D m_ReticleTexture;

        private Vector3 m_TargetPosition;
        private Quaternion m_TargetRotation;
        private float m_TargetScale = 0f;
        private float m_CurrentScale = 0f;
        private Color m_TargetColor;
        private Color m_CurrentColor;
        private bool m_IsActive = false;

        public bool IsOverButton { get; private set; }

        private void Awake()
        {
            m_NearFarInteractor = GetComponent<NearFarInteractor>();
            CreateReticleObject();
        }

        private void OnDestroy()
        {
            if (m_Material != null) Destroy(m_Material);
            if (m_ReticleTexture != null) Destroy(m_ReticleTexture);
            if (m_ReticleObj != null) Destroy(m_ReticleObj);
        }

        private void CreateReticleObject()
        {
            if (m_ReticleObj != null) return;

            // Generate crisp procedural ring + center dot texture
            m_ReticleTexture = GenerateReticleTexture();

            m_ReticleObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_ReticleObj.name = $"{gameObject.name}_PointerReticle";
            m_ReticleObj.transform.SetParent(null, false); // Keep world-space independent

            // Remove collider from reticle quad
            Collider col = m_ReticleObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            m_MeshRenderer = m_ReticleObj.GetComponent<MeshRenderer>();
            m_MeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_MeshRenderer.receiveShadows = false;

            // Use URP Unlit or Sprites/Default with Alpha Blending
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                         ?? Shader.Find("Sprites/Default") 
                         ?? Shader.Find("Unlit/Transparent");

            m_Material = new Material(shader);
            m_Material.mainTexture = m_ReticleTexture;

            // Configure transparent blending on material
            if (m_Material.HasProperty("_Surface"))
            {
                m_Material.SetFloat("_Surface", 1f); // Transparent
                m_Material.SetFloat("_Blend", 0f);   // Alpha blend
                m_Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_Material.SetInt("_ZWrite", 0);
                m_Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m_Material.renderQueue = 3100;
            }

            m_MeshRenderer.material = m_Material;
            m_ReticleObj.SetActive(false);
        }

        private Texture2D GenerateReticleTexture()
        {
            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color clear = new Color(1f, 1f, 1f, 0f);
            Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            float maxR = (size * 0.5f) - 4f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / maxR;

                    if (dist <= 0.22f)
                    {
                        // Solid inner core dot with anti-aliased edge
                        float alpha = Mathf.Clamp01((0.22f - dist) / 0.04f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else if (dist >= 0.65f && dist <= 0.95f)
                    {
                        // Outer circular ring with anti-aliased inner and outer borders
                        float edgeIn = Mathf.Clamp01((dist - 0.65f) / 0.06f);
                        float edgeOut = Mathf.Clamp01((0.95f - dist) / 0.06f);
                        float ringAlpha = Mathf.Min(edgeIn, edgeOut);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, ringAlpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, clear);
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private void Update()
        {
            if (m_NearFarInteractor == null)
            {
                m_NearFarInteractor = GetComponent<NearFarInteractor>();
                if (m_NearFarInteractor == null) return;
            }

            bool hasHit = false;
            Vector3 hitPos = Vector3.zero;
            Vector3 hitNormal = Vector3.forward;
            bool hitButton = false;

            // 1. Check UI Raycast Result from NearFarInteractor
            if (m_NearFarInteractor.TryGetCurrentUIRaycastResult(out RaycastResult uiHit) && uiHit.isValid)
            {
                hasHit = true;
                hitPos = uiHit.worldPosition;
                hitNormal = uiHit.worldNormal != Vector3.zero ? uiHit.worldNormal : Vector3.back;

                if (uiHit.gameObject != null)
                {
                    Button btn = uiHit.gameObject.GetComponentInParent<Button>();
                    hitButton = (btn != null && btn.interactable);
                }
            }

            // 2. Fallback: Check Physics Raycast against Canvas Trigger Collider
            if (!hasHit && m_NearFarInteractor.attachTransform != null)
            {
                Ray ray = new Ray(m_NearFarInteractor.attachTransform.position, m_NearFarInteractor.attachTransform.forward);
                if (Physics.Raycast(ray, out RaycastHit physHit, 20f))
                {
                    if (physHit.collider.GetComponentInParent<Canvas>() != null)
                    {
                        hasHit = true;
                        hitPos = physHit.point;
                        hitNormal = physHit.normal;
                    }
                }
            }

            IsOverButton = hitButton;

            if (hasHit)
            {
                m_IsActive = true;
                // Position reticle 2mm in front of canvas surface along normal to avoid z-fighting
                m_TargetPosition = hitPos + (hitNormal * 0.002f);
                m_TargetRotation = Quaternion.LookRotation(-hitNormal, Vector3.up);

                m_TargetScale = hitButton ? m_ButtonHoverSize : m_CanvasHoverSize;
                m_TargetColor = hitButton ? m_ButtonHoverColor : m_CanvasHoverColor;

                if (!m_ReticleObj.activeSelf)
                {
                    m_ReticleObj.SetActive(true);
                    m_CurrentScale = 0.001f;
                    m_ReticleObj.transform.position = m_TargetPosition;
                    m_ReticleObj.transform.rotation = m_TargetRotation;
                }
            }
            else
            {
                m_IsActive = false;
                m_TargetScale = 0f;
            }

            // Smoothly animate position, rotation, scale, and color
            if (m_ReticleObj != null && m_ReticleObj.activeSelf)
            {
                float dt = Time.unscaledDeltaTime * m_SmoothingSpeed;
                m_ReticleObj.transform.position = Vector3.Lerp(m_ReticleObj.transform.position, m_TargetPosition, dt);
                m_ReticleObj.transform.rotation = Quaternion.Slerp(m_ReticleObj.transform.rotation, m_TargetRotation, dt);

                m_CurrentScale = Mathf.Lerp(m_CurrentScale, m_TargetScale, dt);
                m_ReticleObj.transform.localScale = Vector3.one * m_CurrentScale;

                m_CurrentColor = Color.Lerp(m_CurrentColor, m_TargetColor, dt);
                if (m_Material != null)
                {
                    m_Material.color = m_CurrentColor;
                    if (m_Material.HasProperty("_BaseColor"))
                    {
                        m_Material.SetColor("_BaseColor", m_CurrentColor);
                    }
                }

                if (m_CurrentScale < 0.002f && !m_IsActive)
                {
                    m_ReticleObj.SetActive(false);
                }
            }
        }
    }
}
