using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Trigger zone attached to UI_popup* / UI popup* objects in Scene_Campus_Exterior.
    /// Displays the simplified exterior infographic panel (or Main Gate intro carousel) when the VR player
    /// walks inside, hides it upon exit, and gently animates the holographic indicator.
    /// </summary>
    public class UIPopupTriggerZone : MonoBehaviour
    {
        [Header("Zone Mapping")]
        [Tooltip("Location key in CampusTourManager (e.g. 'outside-north-block', 'outside-gargi', 'main-gate').")]
        [SerializeField] private string m_LocationKey;

        [Tooltip("If true, displays the Main Gate Intro Carousel (Prev / Next / Skip Intro only).")]
        [SerializeField] private bool m_IsMainGate = false;

        [Header("Holographic Visual Animation")]
        [SerializeField] private Transform m_AnimatedRing;
        [SerializeField] private float m_RingRotationSpeed = 28f;

        private bool m_PlayerInside = false;
        private int m_PlayerColliderCount = 0;

        public string LocationKey
        {
            get => m_LocationKey;
            set => m_LocationKey = value;
        }

        public bool IsMainGate
        {
            get => m_IsMainGate;
            set => m_IsMainGate = value;
        }

        public Transform AnimatedRing
        {
            get => m_AnimatedRing;
            set => m_AnimatedRing = value;
        }

        private void Awake()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            if (string.IsNullOrEmpty(m_LocationKey))
            {
                ResolveKeyFromName(name, out m_LocationKey, out m_IsMainGate);
            }
        }

        private void Update()
        {
            if (m_AnimatedRing != null)
            {
                m_AnimatedRing.Rotate(0f, m_RingRotationSpeed * Time.deltaTime, 0f, Space.Self);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerCollider(other)) return;

            m_PlayerColliderCount++;
            if (m_PlayerInside) return;

            m_PlayerInside = true;
            VRHaptics.PulseZoneEnter();
            if (CampusTourManager.Instance == null) return;

            if (m_IsMainGate || m_LocationKey == "main-gate" || m_LocationKey == "front-gate")
            {
                CampusTourManager.Instance.ShowMainGateIntro();
            }
            else if (!string.IsNullOrEmpty(m_LocationKey))
            {
                CampusTourManager.Instance.ShowExteriorZone(m_LocationKey);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayerCollider(other)) return;

            m_PlayerColliderCount = Mathf.Max(0, m_PlayerColliderCount - 1);
            if (m_PlayerColliderCount > 0 || !m_PlayerInside) return;

            m_PlayerInside = false;
            VRHaptics.PulseZoneExit();
            if (CampusTourManager.Instance == null) return;

            string expectedKey = m_IsMainGate ? "main-gate" : m_LocationKey;
            CampusTourManager.Instance.HideAllPanels(expectedKey);
        }

        private static bool IsPlayerCollider(Collider other)
        {
            if (other == null || other.isTrigger) return false;

            if (other.CompareTag("MainCamera") || other.CompareTag("Player"))
                return true;

            if (other.GetComponent<CharacterController>() != null)
                return true;

            if (other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null)
                return true;

            if (other.GetComponentInParent<FirstPersonWalker>() != null)
                return true;

            return false;
        }

        /// <summary>
        /// Resolves a scene GameObject name (supporting both 'UI_popup*' and 'UI popup*' prefixes)
        /// to its CampusTourManager exterior location key.
        /// </summary>
        public static void ResolveKeyFromName(string objName, out string locationKey, out bool isMainGate)
        {
            isMainGate = false;
            locationKey = string.Empty;
            if (string.IsNullOrEmpty(objName)) return;

            string lower = objName.Trim().ToLowerInvariant();
            if (lower.Contains("main gate") || lower.Contains("main_gate") || lower.Contains("front gate") || lower.Contains("front_gate"))
            {
                isMainGate = true;
                locationKey = "main-gate";
                return;
            }

            if (lower.Contains("north block") || lower.Contains("north_block"))
            {
                locationKey = "outside-north-block";
            }
            else if (lower.Contains("gateway"))
            {
                locationKey = "outside-gateway";
            }
            else if (lower.Contains("mechanical") || lower.Contains("workshop"))
            {
                locationKey = "outside-workshop";
            }
            else if (lower.Contains("gargi"))
            {
                locationKey = "outside-gargi";
            }
            else if (lower.Contains("food") || lower.Contains("maggi"))
            {
                locationKey = "outside-food-outlets";
            }
            else if (lower.Contains("vsh") || lower.Contains("vikram"))
            {
                locationKey = "outside-vsh";
            }
            else if (lower.Contains("bsh") || lower.Contains("bhagat"))
            {
                locationKey = "outside-bsh";
            }
            else if (lower.Contains("librar")) // matches both "Library" and "Libraray"
            {
                locationKey = "outside-library";
            }
            else if (lower.Contains("kch") || lower.Contains("kalpana"))
            {
                locationKey = "outside-kch";
            }
            else if (lower.Contains("apj") || lower.Contains("kalam") || lower.EndsWith(" d") || lower.EndsWith("_d"))
            {
                locationKey = "outside-apj";
            }
        }
    }
}
