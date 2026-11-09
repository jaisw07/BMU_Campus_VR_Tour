using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BMU.VRTour
{
    /// <summary>
    /// Enables XR Ray Pointer dragging and repositioning of the VR spatial UI canvas.
    /// Billboards to face the user camera during drag, maintains ray distance, and
    /// firmly pins the UI in world space upon release.
    /// </summary>
    public class VRUIDraggableHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Target Root")]
        [Tooltip("The root transform of the spatial UI to move. Defaults to the canvas root if null.")]
        [SerializeField] private Transform m_UIRoot;

        [Tooltip("LazyFollowUI component on the UI root.")]
        [SerializeField] private LazyFollowUI m_LazyFollowUI;

        [Header("Motion & Smoothing")]
        [SerializeField] private float m_MoveSpeed = 16f;
        [SerializeField] private float m_RotateSpeed = 14f;
        [SerializeField] private float m_MinDistance = 0.8f;
        [SerializeField] private float m_MaxDistance = 3.5f;

        [Header("Visual Feedback")]
        [SerializeField] private Image m_HandleImage;
        [SerializeField] private TextMeshProUGUI m_HandleText;
        [SerializeField] private Color m_NormalColor = new Color(0f, 0.17f, 0.29f, 0.90f);       // Navy
        [SerializeField] private Color m_HoverColor = new Color(0f, 0.40f, 0.70f, 0.95f);        // Accent Blue
        [SerializeField] private Color m_DraggingColor = new Color(0f, 0.85f, 1f, 1f);           // Electric Cyan (#00D8FF)
        [SerializeField] private Color m_NormalTextColor = Color.white;
        [SerializeField] private Color m_DraggingTextColor = new Color(0f, 0.17f, 0.29f, 1f);   // Navy on Cyan

        private bool m_IsHovered = false;
        private bool m_IsDragging = false;
        private Transform m_RaySource;
        private float m_GrabDistance = 1.8f;
        private Vector3 m_GrabOffset = Vector3.zero;

        public bool IsDragging => m_IsDragging;

        private void Awake()
        {
            if (m_UIRoot == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                m_UIRoot = canvas != null ? canvas.transform.root : transform.root;
            }

            if (m_LazyFollowUI == null && m_UIRoot != null)
            {
                m_LazyFollowUI = m_UIRoot.GetComponent<LazyFollowUI>();
            }

            if (m_HandleImage == null)
            {
                m_HandleImage = GetComponent<Image>();
            }

            if (m_HandleText == null)
            {
                m_HandleText = GetComponentInChildren<TextMeshProUGUI>();
            }

            UpdateVisualState();
        }

        private void Update()
        {
            if (!m_IsDragging || m_UIRoot == null) return;

            if (m_RaySource == null)
            {
                m_RaySource = ResolveRaySource();
                if (m_RaySource == null) return;
            }

            // Calculate target position along the controller ray
            Vector3 rayPoint = m_RaySource.position + (m_RaySource.forward * m_GrabDistance);
            Vector3 desiredPos = rayPoint + m_GrabOffset;

            float dt = Time.deltaTime;
            m_UIRoot.position = Vector3.Lerp(m_UIRoot.position, desiredPos, dt * m_MoveSpeed);

            // Automatically billboard to face user camera
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                Vector3 lookDir = m_UIRoot.position - cam.position;
                lookDir.y = 0f; // Lock horizon for comfort
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion desiredRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                    m_UIRoot.rotation = Quaternion.Slerp(m_UIRoot.rotation, desiredRot, dt * m_RotateSpeed);
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_IsHovered = true;
            UpdateVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_IsHovered = false;
            UpdateVisualState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_IsDragging = true;

            // Pause / pin lazy follow so it does not fight manual movement
            if (m_LazyFollowUI != null)
            {
                m_LazyFollowUI.Pin();
            }

            // Find ray source (controller interactor or camera)
            m_RaySource = ResolveRaySource(eventData);

            Vector3 rootPos = m_UIRoot != null ? m_UIRoot.position : transform.root.position;
            if (m_RaySource != null)
            {
                m_GrabDistance = Vector3.Distance(m_RaySource.position, rootPos);
                m_GrabDistance = Mathf.Clamp(m_GrabDistance, m_MinDistance, m_MaxDistance);

                Vector3 initialRayPoint = m_RaySource.position + (m_RaySource.forward * m_GrabDistance);
                m_GrabOffset = rootPos - initialRayPoint;
            }
            else
            {
                m_GrabOffset = Vector3.zero;
            }

            UpdateVisualState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_IsDragging = false;
            m_RaySource = null;

            // Firmly pin UI in world space upon release
            if (m_LazyFollowUI != null)
            {
                m_LazyFollowUI.Pin();
            }

            UpdateVisualState();
        }

        private Transform ResolveRaySource(PointerEventData eventData = null)
        {
            // 1. Try to find the active NearFarInteractor hitting this handle
            var interactors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            foreach (var interactor in interactors)
            {
                if (interactor.TryGetCurrentUIRaycastResult(out RaycastResult hit) && hit.isValid)
                {
                    if (hit.gameObject == gameObject || hit.gameObject.transform.IsChildOf(transform))
                    {
                        return interactor.transform;
                    }
                }
            }

            // 2. Check eventData event camera
            if (eventData != null && eventData.enterEventCamera != null)
            {
                return eventData.enterEventCamera.transform;
            }

            // 3. Fallback to main camera
            if (Camera.main != null)
            {
                return Camera.main.transform;
            }

            return null;
        }

        private void UpdateVisualState()
        {
            if (m_HandleImage != null)
            {
                if (m_IsDragging)
                {
                    m_HandleImage.color = m_DraggingColor;
                }
                else if (m_IsHovered)
                {
                    m_HandleImage.color = m_HoverColor;
                }
                else
                {
                    m_HandleImage.color = m_NormalColor;
                }
            }

            if (m_HandleText != null)
            {
                m_HandleText.color = m_IsDragging ? m_DraggingTextColor : m_NormalTextColor;
            }
        }
    }
}
