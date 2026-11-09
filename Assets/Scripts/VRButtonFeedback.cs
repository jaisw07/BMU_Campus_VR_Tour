using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace BMU.VRTour
{
    /// <summary>
    /// Provides rich, high-fidelity tactile and visual feedback for VR UI buttons.
    /// Displays a luminous glowing border outline, smooth scale punch, and color enhancement
    /// when the Meta Quest 3S light pointer hovers over or presses the button.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class VRButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Hover Scale Settings")]
        [SerializeField] private float m_HoverScale = 1.04f;
        [SerializeField] private float m_PressScale = 0.96f;
        [SerializeField] private float m_LerpSpeed = 16f;

        [Header("Highlight Outline Settings")]
        [SerializeField] private Color m_HighlightColor = new Color(0f, 0.72f, 1f, 1f); // Vibrant electric cyan/blue
        [SerializeField] private float m_BorderThickness = 3f;

        private Button m_Button;
        private Image m_ButtonImage;
        private RectTransform m_RectTransform;
        private Image m_HighlightOutlineImage;
        private Vector3 m_OriginalScale;
        private Vector3 m_TargetScale;
        private Color m_OriginalColor;
        private bool m_IsHovered = false;
        private bool m_IsPressed = false;

        public bool IsHovered => m_IsHovered;
        public bool IsPressed => m_IsPressed;

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
            m_OriginalScale = m_RectTransform.localScale;
            m_TargetScale = m_OriginalScale;

            if (m_ButtonImage != null)
            {
                m_OriginalColor = m_ButtonImage.color;
            }

            EnsureHighlightBorder();
        }

        private void OnEnable()
        {
            ResetVisualState();
        }

        private void OnDisable()
        {
            ResetVisualState();
        }

        private void ResetVisualState()
        {
            m_IsHovered = false;
            m_IsPressed = false;
            m_TargetScale = m_OriginalScale;
            if (m_RectTransform != null)
            {
                m_RectTransform.localScale = m_OriginalScale;
            }
            if (m_HighlightOutlineImage != null)
            {
                m_HighlightOutlineImage.gameObject.SetActive(false);
            }
            if (m_ButtonImage != null && m_OriginalColor.a > 0.01f)
            {
                m_ButtonImage.color = m_OriginalColor;
            }
        }

        /// <summary>
        /// Ensures a dedicated, luminous highlight border exists around the button.
        /// </summary>
        public void EnsureHighlightBorder()
        {
            if (m_ButtonImage == null)
            {
                m_ButtonImage = GetComponent<Image>();
            }

            Transform existing = transform.Find("HighlightBorder");
            if (existing != null)
            {
                m_HighlightOutlineImage = existing.GetComponent<Image>();
            }
            else
            {
                GameObject borderObj = new GameObject("HighlightBorder");
                borderObj.transform.SetParent(transform, false);
                borderObj.transform.SetAsFirstSibling(); // Render behind button content

                m_HighlightOutlineImage = borderObj.AddComponent<Image>();
                m_HighlightOutlineImage.color = m_HighlightColor;
                m_HighlightOutlineImage.raycastTarget = false;

                // Frame-style outline using four border margins
                RectTransform borderRt = borderObj.GetComponent<RectTransform>();
                borderRt.anchorMin = Vector2.zero;
                borderRt.anchorMax = Vector2.one;
                borderRt.offsetMin = new Vector2(-m_BorderThickness, -m_BorderThickness);
                borderRt.offsetMax = new Vector2(m_BorderThickness, m_BorderThickness);
            }

            if (m_HighlightOutlineImage != null && m_ButtonImage != null && m_ButtonImage.sprite != null)
            {
                m_HighlightOutlineImage.sprite = m_ButtonImage.sprite;
                m_HighlightOutlineImage.type = m_ButtonImage.type;
            }

            if (m_HighlightOutlineImage != null)
            {
                m_HighlightOutlineImage.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (m_RectTransform != null && m_RectTransform.localScale != m_TargetScale)
            {
                m_RectTransform.localScale = Vector3.Lerp(
                    m_RectTransform.localScale,
                    m_TargetScale,
                    Time.unscaledDeltaTime * m_LerpSpeed
                );
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (m_Button != null && !m_Button.interactable) return;

            m_IsHovered = true;
            m_TargetScale = m_OriginalScale * m_HoverScale;

            if (m_HighlightOutlineImage != null)
            {
                m_HighlightOutlineImage.gameObject.SetActive(true);
            }

            if (m_ButtonImage != null)
            {
                // Brighten button background on hover
                m_ButtonImage.color = Color.Lerp(m_OriginalColor, Color.white, 0.28f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_IsHovered = false;
            m_IsPressed = false;
            m_TargetScale = m_OriginalScale;

            if (m_HighlightOutlineImage != null)
            {
                m_HighlightOutlineImage.gameObject.SetActive(false);
            }

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
