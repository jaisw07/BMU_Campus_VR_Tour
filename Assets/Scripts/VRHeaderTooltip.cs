using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace BMU.VRTour
{
    /// <summary>
    /// Displays a floating glass pill tooltip above an icon button on Quest Ray Pointer hover.
    /// Smoothly fades in on IPointerEnter and fades out on IPointerExit.
    /// </summary>
    public class VRHeaderTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        [Header("Configuration")]
        [SerializeField] private string m_TooltipText = "";
        [SerializeField] private float m_VerticalOffset = 46f;
        [SerializeField] private float m_FadeSpeed = 10f;

        [Header("Visual References")]
        [SerializeField] private GameObject m_TooltipRoot;
        [SerializeField] private CanvasGroup m_CanvasGroup;
        [SerializeField] private TextMeshProUGUI m_TextLabel;

        private bool m_IsHovered = false;
        private float m_TargetAlpha = 0f;

        public bool IsHovered => m_IsHovered;

        public string TooltipText
        {
            get => m_TooltipText;
            set
            {
                m_TooltipText = value;
                if (m_TextLabel != null) m_TextLabel.text = value;
            }
        }

        private void Awake()
        {
            EnsureTooltipVisual();
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.alpha = 0f;
                m_CanvasGroup.blocksRaycasts = false;
                m_CanvasGroup.interactable = false;
            }
        }

        private void OnEnable()
        {
            m_IsHovered = false;
            m_TargetAlpha = 0f;
            if (m_CanvasGroup != null) m_CanvasGroup.alpha = 0f;
        }

        private void OnDisable()
        {
            m_IsHovered = false;
            m_TargetAlpha = 0f;
            if (m_CanvasGroup != null) m_CanvasGroup.alpha = 0f;
        }

        private void Update()
        {
            if (m_CanvasGroup == null) return;

            if (Mathf.Abs(m_CanvasGroup.alpha - m_TargetAlpha) > 0.01f)
            {
                m_CanvasGroup.alpha = Mathf.MoveTowards(m_CanvasGroup.alpha, m_TargetAlpha, Time.unscaledDeltaTime * m_FadeSpeed);
            }
            else
            {
                m_CanvasGroup.alpha = m_TargetAlpha;
            }

            if (m_TooltipRoot != null)
            {
                m_TooltipRoot.SetActive(m_CanvasGroup.alpha > 0.001f);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_IsHovered = true;
            m_TargetAlpha = 1f;
            EnsureTooltipVisual();
            if (m_TooltipRoot != null) m_TooltipRoot.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_IsHovered = false;
            m_TargetAlpha = 0f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Dim on click for immediate responsive feel
            m_TargetAlpha = 0f;
        }

        public void EnsureTooltipVisual()
        {
            if (m_TooltipRoot != null)
            {
                if (m_TextLabel != null && !string.IsNullOrEmpty(m_TooltipText))
                {
                    m_TextLabel.text = m_TooltipText;
                }
                return;
            }

            // Find existing child if already built
            Transform existing = transform.Find("FloatingTooltip");
            if (existing != null)
            {
                m_TooltipRoot = existing.gameObject;
                m_CanvasGroup = m_TooltipRoot.GetComponent<CanvasGroup>();
                m_TextLabel = m_TooltipRoot.GetComponentInChildren<TextMeshProUGUI>();
                if (m_TextLabel != null && !string.IsNullOrEmpty(m_TooltipText))
                {
                    m_TextLabel.text = m_TooltipText;
                }
                return;
            }

            // Runtime fallback creation
            m_TooltipRoot = new GameObject("FloatingTooltip");
            m_TooltipRoot.transform.SetParent(transform, false);

            RectTransform rootRt = m_TooltipRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.anchoredPosition = new Vector2(0f, m_VerticalOffset);
            rootRt.sizeDelta = new Vector2(160f, 30f);

            Image bg = m_TooltipRoot.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.08f, 0.16f, 0.92f); // Deep frosted navy
            bg.raycastTarget = false;

            var outline = m_TooltipRoot.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.72f, 1f, 0.60f); // Laser cyan subtle glow
            outline.effectDistance = new Vector2(1.2f, 1.2f);

            m_CanvasGroup = m_TooltipRoot.AddComponent<CanvasGroup>();
            m_CanvasGroup.blocksRaycasts = false;
            m_CanvasGroup.interactable = false;
            m_CanvasGroup.alpha = 0f;

            GameObject textObj = new GameObject("TooltipLabel");
            textObj.transform.SetParent(m_TooltipRoot.transform, false);
            m_TextLabel = textObj.AddComponent<TextMeshProUGUI>();
            m_TextLabel.text = m_TooltipText;
            m_TextLabel.fontSize = 12f;
            m_TextLabel.alignment = TextAlignmentOptions.Center;
            m_TextLabel.color = Color.white;
            m_TextLabel.fontStyle = FontStyles.Bold;
            m_TextLabel.raycastTarget = false;

            RectTransform txtRt = textObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(10f, 0f);
            txtRt.offsetMax = new Vector2(-10f, 0f);

            m_TooltipRoot.SetActive(false);
        }

        public void Configure(string text, GameObject root, CanvasGroup cg, TextMeshProUGUI label)
        {
            m_TooltipText = text;
            m_TooltipRoot = root;
            m_CanvasGroup = cg;
            m_TextLabel = label;
            if (m_TextLabel != null) m_TextLabel.text = text;
        }
    }
}
