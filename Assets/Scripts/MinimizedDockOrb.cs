using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BMU.VRTour
{
    /// <summary>
    /// Manages collapsing the full spatial UI into a compact, glowing BMU Dock Orb
    /// for unobstructed 360 exploration, and restoring it on click.
    /// </summary>
    public class MinimizedDockOrb : MonoBehaviour
    {
        [Header("Target References")]
        [SerializeField] private GameObject m_MainCanvasContainer;
        [SerializeField] private GameObject m_DockOrbVisual;
        [SerializeField] private CanvasGroup m_MainCanvasGroup;

        [Header("Animation Settings")]
        [SerializeField] private float m_TransitionDuration = 0.35f;
        [SerializeField] private Vector3 m_OrbOffset = new Vector3(0f, -0.45f, 1.4f);

        [Header("Pulse Effect")]
        [SerializeField] private Transform m_PulseRing;
        [SerializeField] private float m_PulseSpeed = 2.0f;
        [SerializeField] private float m_PulseScaleAmount = 0.2f;

        private bool m_IsMinimized = false;
        private Coroutine m_TransitionRoutine;
        private Vector3 m_OriginalRingScale = Vector3.one;

        public bool IsMinimized => m_IsMinimized;

        private void Start()
        {
            if (m_PulseRing != null)
            {
                m_OriginalRingScale = m_PulseRing.localScale;
            }

            if (m_DockOrbVisual != null)
            {
                m_DockOrbVisual.SetActive(false);
            }
        }

        private void Update()
        {
            if (m_IsMinimized && m_PulseRing != null)
            {
                float wave = Mathf.Sin(Time.time * m_PulseSpeed) * m_PulseScaleAmount;
                m_PulseRing.localScale = m_OriginalRingScale * (1f + wave);
            }
        }

        /// <summary>
        /// Minimizes the large UI deck into the floating orb.
        /// </summary>
        public void MinimizeToDock()
        {
            if (m_IsMinimized) return;
            if (m_TransitionRoutine != null) StopCoroutine(m_TransitionRoutine);
            m_TransitionRoutine = StartCoroutine(AnimateMinimize(true));
        }

        /// <summary>
        /// Restores the large UI deck from the orb.
        /// </summary>
        public void RestoreFromDock()
        {
            if (!m_IsMinimized) return;
            if (m_TransitionRoutine != null) StopCoroutine(m_TransitionRoutine);
            m_TransitionRoutine = StartCoroutine(AnimateMinimize(false));
        }

        public void ToggleDockState()
        {
            if (m_IsMinimized) RestoreFromDock();
            else MinimizeToDock();
        }

        private IEnumerator AnimateMinimize(bool toMinimized)
        {
            float elapsed = 0f;
            Vector3 startScale = toMinimized ? Vector3.one : Vector3.zero;
            Vector3 targetScale = toMinimized ? Vector3.zero : Vector3.one;

            if (toMinimized)
            {
                if (m_DockOrbVisual != null)
                {
                    m_DockOrbVisual.SetActive(true);
                    m_DockOrbVisual.transform.localScale = Vector3.zero;
                }
            }

            while (elapsed < m_TransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / m_TransitionDuration);

                if (m_MainCanvasContainer != null)
                {
                    m_MainCanvasContainer.transform.localScale = Vector3.Lerp(startScale, targetScale, t);
                }

                if (m_MainCanvasGroup != null)
                {
                    m_MainCanvasGroup.alpha = toMinimized ? (1f - t) : t;
                }

                if (m_DockOrbVisual != null)
                {
                    m_DockOrbVisual.transform.localScale = toMinimized ? Vector3.Lerp(Vector3.zero, Vector3.one, t) : Vector3.Lerp(Vector3.one, Vector3.zero, t);
                }

                yield return null;
            }

            m_IsMinimized = toMinimized;

            if (m_MainCanvasContainer != null)
            {
                m_MainCanvasContainer.transform.localScale = targetScale;
                m_MainCanvasContainer.SetActive(!toMinimized);
            }

            if (m_DockOrbVisual != null)
            {
                m_DockOrbVisual.transform.localScale = toMinimized ? Vector3.one : Vector3.zero;
                m_DockOrbVisual.SetActive(toMinimized);
            }
        }
    }
}
