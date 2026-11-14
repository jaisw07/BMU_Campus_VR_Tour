using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace BMU.VRTour
{
    /// <summary>
    /// Provides rich, high-fidelity tactile and visual feedback for VR UI buttons.
    /// Replaces jarring hard borders with a feathered, diffused ambient backlight aura,
    /// a smooth -10mm forward float (-6mm for mini-controls), gentle 1.02x scale punch,
    /// and strict raycast isolation across all child graphic elements.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class VRButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Hover Scale Settings")]
        [SerializeField] private float m_HoverScale = 1.02f; // Refined from 1.04x
        [SerializeField] private float m_PressScale = 0.98f;
        [SerializeField] private float m_LerpSpeed = 12f;    // Refined cubic damping

        [Header("Backlight Aura Settings")]
        [SerializeField] private Color m_HighlightColor = new Color(0.01f, 0.58f, 0.88f, 0.75f); // Lighter natural sky blue aura #0294E0
        [SerializeField] private float m_GlowPadding = 24f; // 24px soft margins
        [SerializeField] private Sprite m_CustomGlowSprite;

        [Header("3D Hover Translation")]
        [SerializeField] private float m_HoverZOffset = -10f; // Refined to -10mm forward toward VR viewer
        private Vector3 m_OriginalAnchoredPos3D;
        private Vector3 m_TargetAnchoredPos3D;
        private float m_CurrentHoverZOffset = 0f;
        private float m_TargetHoverZOffset = 0f;
        private bool m_HasCardArcParent = false;

        private Button m_Button;
        private Image m_ButtonImage;
        private RectTransform m_RectTransform;
        private Image m_BacklightImage;
        private float m_CurrentGlowAlpha = 0f;
        private float m_TargetGlowAlpha = 0f;
        private Vector3 m_OriginalScale = Vector3.one;
        private Vector3 m_TargetScale = Vector3.one;
        private Color m_OriginalColor;
        private bool m_IsHovered = false;
        private bool m_IsPressed = false;
        private bool m_IsRestingPositionCached = false;

        public bool IsHovered => m_IsHovered;
        public bool IsPressed => m_IsPressed;
        public float HoverZOffset { get => m_HoverZOffset; set => m_HoverZOffset = value; }
        public float HoverScale { get => m_HoverScale; set => m_HoverScale = value; }
        public float GlowPadding { get => m_GlowPadding; set => m_GlowPadding = value; }
        public Sprite CustomGlowSprite { get => m_CustomGlowSprite; set { m_CustomGlowSprite = value; if (m_BacklightImage != null) m_BacklightImage.sprite = value; } }
        public float CurrentHoverZOffset => m_CurrentHoverZOffset;
        public Color HighlightColor
        {
            get => m_HighlightColor;
            set
            {
                m_HighlightColor = value;
                if (m_BacklightImage != null)
                {
                    Color c = value;
                    c.a = m_CurrentGlowAlpha;
                    m_BacklightImage.color = c;
                }
            }
        }

        public void SetBaseColor(Color newBaseColor)
        {
            m_OriginalColor = newBaseColor;
            if (!m_IsHovered && m_ButtonImage != null)
            {
                m_ButtonImage.color = newBaseColor;
            }
        }

        private void Awake()
        {
            m_Button = GetComponent<Button>();
            m_ButtonImage = GetComponent<Image>();
            m_RectTransform = GetComponent<RectTransform>();

            if (m_ButtonImage != null)
            {
                m_OriginalColor = m_ButtonImage.color;
            }

            m_HasCardArcParent = transform.parent != null && transform.parent.GetComponent<UICardArc>() != null;

            EnsureBacklight();
        }

        private void Start()
        {
            if (!m_IsRestingPositionCached)
            {
                InitializeRestingPosition();
            }
            EnforceChildRaycastIsolation();
        }

        private void OnEnable()
        {
            ResetVisualState();
            EnforceChildRaycastIsolation();
        }

        private void OnDisable()
        {
            ResetVisualState();
        }

        /// <summary>
        /// Explicitly caches resting coordinates and isolates child raycasting.
        /// Call this after procedural layout calculations to eliminate coordinate jumping.
        /// </summary>
        public void InitializeRestingPosition()
        {
            if (m_RectTransform == null) m_RectTransform = GetComponent<RectTransform>();
            m_OriginalScale = m_RectTransform.localScale;
            m_TargetScale = m_OriginalScale;
            m_OriginalAnchoredPos3D = m_RectTransform.anchoredPosition3D;
            m_TargetAnchoredPos3D = m_OriginalAnchoredPos3D;
            m_IsRestingPositionCached = true;
            EnforceChildRaycastIsolation();
        }

        /// <summary>
        /// Ensures all child graphics do not intercept raycasts independently,
        /// ensuring whole-button unit hover cohesion.
        /// </summary>
        public void EnforceChildRaycastIsolation()
        {
            Graphic[] childGraphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < childGraphics.Length; i++)
            {
                if (childGraphics[i].gameObject != gameObject && childGraphics[i] != m_BacklightImage)
                {
                    childGraphics[i].raycastTarget = false;
                }
            }
        }

        public void ResetVisualState()
        {
            m_IsHovered = false;
            m_IsPressed = false;
            m_TargetScale = m_OriginalScale;
            m_TargetHoverZOffset = 0f;
            m_CurrentHoverZOffset = 0f;
            m_TargetAnchoredPos3D = m_OriginalAnchoredPos3D;
            m_TargetGlowAlpha = 0f;
            m_CurrentGlowAlpha = 0f;

            if (m_RectTransform != null)
            {
                m_RectTransform.localScale = m_OriginalScale;
                if (!m_HasCardArcParent && m_HoverZOffset != 0f && m_IsRestingPositionCached)
                {
                    m_RectTransform.anchoredPosition3D = m_OriginalAnchoredPos3D;
                }
            }

            if (m_BacklightImage != null)
            {
                Color c = m_HighlightColor;
                c.a = 0f;
                m_BacklightImage.color = c;
            }

            if (m_ButtonImage != null && m_OriginalColor.a > 0.01f)
            {
                m_ButtonImage.color = m_OriginalColor;
            }
        }

        /// <summary>
        /// Backward-compatible alias for EnsureBacklight.
        /// </summary>
        public void EnsureHighlightBorder()
        {
            EnsureBacklight();
        }

        /// <summary>
        /// Ensures a soft, feathered ambient backlight glow sits behind the button element.
        /// </summary>
        public void EnsureBacklight(Sprite customSprite = null)
        {
            if (m_ButtonImage == null)
            {
                m_ButtonImage = GetComponent<Image>();
            }

            if (customSprite != null)
            {
                m_CustomGlowSprite = customSprite;
            }

            Transform existing = transform.Find("BacklightGlow");
            if (existing == null)
            {
                // Clean up any legacy hard HighlightBorder child
                Transform legacy = transform.Find("HighlightBorder");
                if (legacy != null)
                {
                    DestroyImmediate(legacy.gameObject);
                }

                GameObject glowObj = new GameObject("BacklightGlow");
                glowObj.transform.SetParent(transform, false);
                glowObj.transform.SetAsFirstSibling(); // Render directly behind button face

                m_BacklightImage = glowObj.AddComponent<Image>();
                m_BacklightImage.raycastTarget = false;

                RectTransform glowRt = glowObj.GetComponent<RectTransform>();
                glowRt.anchorMin = Vector2.zero;
                glowRt.anchorMax = Vector2.one;
                glowRt.offsetMin = new Vector2(-m_GlowPadding, -m_GlowPadding);
                glowRt.offsetMax = new Vector2(m_GlowPadding, m_GlowPadding);
            }
            else
            {
                m_BacklightImage = existing.GetComponent<Image>();
                RectTransform glowRt = existing.GetComponent<RectTransform>();
                glowRt.anchorMin = Vector2.zero;
                glowRt.anchorMax = Vector2.one;
                glowRt.offsetMin = new Vector2(-m_GlowPadding, -m_GlowPadding);
                glowRt.offsetMax = new Vector2(m_GlowPadding, m_GlowPadding);
            }

            if (m_CustomGlowSprite != null)
            {
                m_BacklightImage.sprite = m_CustomGlowSprite;
            }
            else if (m_BacklightImage.sprite == null)
            {
                // Fallback attempt from Resources
                m_BacklightImage.sprite = Resources.Load<Sprite>("UI/ui_backlight_glow");
            }

            bool isCircle = (m_CustomGlowSprite != null && m_CustomGlowSprite.name.Contains("circle")) ||
                            (m_BacklightImage.sprite != null && m_BacklightImage.sprite.name.Contains("circle"));

            if (isCircle)
            {
                m_BacklightImage.type = Image.Type.Simple;
            }
            else
            {
                // All rectangular / card backlight glows must be Sliced so highlight covers the entire card with no empty edges
                m_BacklightImage.type = Image.Type.Sliced;
            }

            Color c = m_HighlightColor;
            c.a = 0f;
            m_BacklightImage.color = c;
            m_BacklightImage.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (m_RectTransform == null) return;

            // 1. Scale punch lerp
            if (m_RectTransform.localScale != m_TargetScale)
            {
                m_RectTransform.localScale = Vector3.Lerp(
                    m_RectTransform.localScale,
                    m_TargetScale,
                    Time.unscaledDeltaTime * m_LerpSpeed
                );
            }

            // 2. 3D Z-translation lerp
            m_CurrentHoverZOffset = Mathf.Lerp(m_CurrentHoverZOffset, m_TargetHoverZOffset, Time.unscaledDeltaTime * m_LerpSpeed);

            if (!m_HasCardArcParent && m_HoverZOffset != 0f && m_IsRestingPositionCached)
            {
                if (m_RectTransform.anchoredPosition3D != m_TargetAnchoredPos3D)
                {
                    m_RectTransform.anchoredPosition3D = Vector3.Lerp(
                        m_RectTransform.anchoredPosition3D,
                        m_TargetAnchoredPos3D,
                        Time.unscaledDeltaTime * m_LerpSpeed
                    );
                }
            }

            // 3. Ambient backlight aura alpha lerp
            if (m_BacklightImage != null)
            {
                if (Mathf.Abs(m_CurrentGlowAlpha - m_TargetGlowAlpha) > 0.001f)
                {
                    m_CurrentGlowAlpha = Mathf.Lerp(m_CurrentGlowAlpha, m_TargetGlowAlpha, Time.unscaledDeltaTime * m_LerpSpeed);
                    Color glowCol = m_HighlightColor;
                    glowCol.a = m_CurrentGlowAlpha;
                    m_BacklightImage.color = glowCol;
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (m_Button != null && !m_Button.interactable) return;

            m_IsHovered = true;
            m_TargetScale = m_OriginalScale * m_HoverScale;
            m_TargetHoverZOffset = m_HoverZOffset;
            m_TargetAnchoredPos3D = new Vector3(m_OriginalAnchoredPos3D.x, m_OriginalAnchoredPos3D.y, m_OriginalAnchoredPos3D.z + m_HoverZOffset);
            m_TargetGlowAlpha = m_HighlightColor.a;

            if (m_ButtonImage != null && m_OriginalColor.a > 0.01f)
            {
                // Subtly lift button face brightness
                m_ButtonImage.color = Color.Lerp(m_OriginalColor, Color.white, 0.22f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_IsHovered = false;
            m_IsPressed = false;
            m_TargetScale = m_OriginalScale;
            m_TargetHoverZOffset = 0f;
            m_TargetAnchoredPos3D = m_OriginalAnchoredPos3D;
            m_TargetGlowAlpha = 0f;

            if (m_ButtonImage != null)
            {
                m_ButtonImage.color = m_OriginalColor;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (m_Button != null && !m_Button.interactable) return;

            m_IsPressed = true;
            m_TargetScale = m_OriginalScale * m_PressScale;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_IsPressed = false;
            m_TargetScale = m_IsHovered ? (m_OriginalScale * m_HoverScale) : m_OriginalScale;
        }
    }
}
