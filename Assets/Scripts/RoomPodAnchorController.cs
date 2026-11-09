using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace BMU.VRTour
{
    /// <summary>
    /// Attached to each interior room pod root (e.g. RoomPod_SmartClassroom).
    /// Registers primary spawn anchors, alternate vantage pads, and out-of-bounds recovery points.
    /// </summary>
    [SelectionBase]
    public class RoomPodAnchorController : MonoBehaviour
    {
        [Header("Room Identity")]
        [Tooltip("Unique key matching CampusTourManager room keys (e.g. 'nb-classroom', 'nb-lab', 'nb-faculty', 'gw-classroom', 'gw-lab').")]
        [SerializeField] private string m_PodKey;

        [Tooltip("Display name for UI / HUD verification.")]
        [SerializeField] private string m_PodDisplayName;

        [Header("Anchor Configuration")]
        [Tooltip("Primary anchor where user spawns upon entering this room.")]
        [SerializeField] private Transform m_PrimarySpawnAnchor;

        [Tooltip("Alternate curated vantage anchors inside this room.")]
        [SerializeField] private Transform[] m_AlternateAnchors;

        [Tooltip("Fallback anchor used when OutOfBounds safety trigger fires.")]
        [SerializeField] private Transform m_FallbackRecoveryAnchor;

        public string PodKey => m_PodKey;
        public string PodDisplayName => m_PodDisplayName;
        public Transform PrimarySpawnAnchor => m_PrimarySpawnAnchor != null ? m_PrimarySpawnAnchor : transform;
        public Transform[] AlternateAnchors => m_AlternateAnchors;

        public Transform FallbackRecoveryAnchor
        {
            get
            {
                if (m_FallbackRecoveryAnchor != null) return m_FallbackRecoveryAnchor;
                if (m_PrimarySpawnAnchor != null) return m_PrimarySpawnAnchor;
                return transform;
            }
        }

        public event Action OnPlayerEnteredPod;

        public void NotifyPlayerEntered()
        {
            OnPlayerEnteredPod?.Invoke();
        }

        public Transform FindAnchor(string anchorName)
        {
            if (m_PrimarySpawnAnchor != null && m_PrimarySpawnAnchor.name.Equals(anchorName, StringComparison.OrdinalIgnoreCase))
            {
                return m_PrimarySpawnAnchor;
            }

            if (m_AlternateAnchors != null)
            {
                foreach (var anchor in m_AlternateAnchors)
                {
                    if (anchor != null && anchor.name.Equals(anchorName, StringComparison.OrdinalIgnoreCase))
                    {
                        return anchor;
                    }
                }
            }

            return PrimarySpawnAnchor;
        }

        private void Reset()
        {
            m_PodKey = gameObject.name.Replace("RoomPod_", "").ToLowerInvariant();
            m_PodDisplayName = gameObject.name;
        }
    }
}
