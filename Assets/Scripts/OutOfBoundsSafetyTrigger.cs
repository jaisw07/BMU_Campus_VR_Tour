using System;
using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Attached to the invisible safety recovery floor located underneath each room pod
    /// (typically at Y = -2.0m). Catches tracking drift or physics clips and immediately
    /// teleports the player back to the curated room anchor.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class OutOfBoundsSafetyTrigger : MonoBehaviour
    {
        [Header("Recovery Destination")]
        [Tooltip("Fallback anchor transform. If null, queries the parent RoomPodAnchorController.")]
        [SerializeField] private Transform m_FallbackAnchor;

        [Tooltip("Parent room pod controller.")]
        [SerializeField] private RoomPodAnchorController m_PodController;

        [Header("Comfort Transition")]
        [Tooltip("Duration of comfort blackout when recovering from a fall.")]
        [SerializeField] private float m_FadeDuration = 0.2f;

        private float m_LastTriggerTime = -10f;
        private const float k_TriggerCooldown = 1.0f;

        private void Awake()
        {
            if (m_PodController == null)
            {
                m_PodController = GetComponentInParent<RoomPodAnchorController>();
            }

            BoxCollider col = GetComponent<BoxCollider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Time.time - m_LastTriggerTime < k_TriggerCooldown) return;

            // Check if player / camera / character controller entered
            bool isPlayer = other.CompareTag("MainCamera") ||
                            other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null ||
                            other.GetComponent<CharacterController>() != null;

            if (isPlayer)
            {
                m_LastTriggerTime = Time.time;
                Debug.LogWarning($"[OutOfBoundsSafetyTrigger] Player breached room boundary on '{name}'! Recovering position...");
                ExecuteRecovery();
            }
        }

        public void ExecuteRecovery()
        {
            Transform targetAnchor = m_FallbackAnchor;
            if (targetAnchor == null && m_PodController != null)
            {
                targetAnchor = m_PodController.FallbackRecoveryAnchor;
            }

            if (targetAnchor == null && SceneTransitionManager.Instance != null)
            {
                targetAnchor = SceneTransitionManager.Instance.CurrentAnchorTransform;
            }

            if (targetAnchor != null)
            {
                if (ScreenFade.Instance != null)
                {
                    ScreenFade.Instance.FadeOutAndIn(() =>
                    {
                        if (SceneTransitionManager.Instance != null)
                        {
                            SceneTransitionManager.Instance.MoveRigToAnchor(targetAnchor);
                        }
                    }, m_FadeDuration, 0.05f);
                }
                else if (SceneTransitionManager.Instance != null)
                {
                    SceneTransitionManager.Instance.MoveRigToAnchor(targetAnchor);
                }
            }
        }

        private void Reset()
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
            m_PodController = GetComponentInParent<RoomPodAnchorController>();
        }
    }
}
