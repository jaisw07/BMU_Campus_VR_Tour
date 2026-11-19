using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace BMU.VRTour
{
    /// <summary>
    /// Hides the controller pointer ray while the player is walking or turning, and brings it back a moment after they stop.
    /// Sits on the "LineVisual" object next to the CurveVisualController.
    /// The toolkit re-enables its LineRenderer every frame, so the CurveVisualController itself has to be switched off to hide it.
    /// The blue VRPointerReticle is separate and is not affected.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public class VRRayAutoHide : MonoBehaviour
    {
        [Tooltip("Thumbstick deflection (0-1) above which the player counts as moving or turning.")]
        [SerializeField, Range(0.05f, 0.9f)] private float m_StickThreshold = 0.15f;

        [Tooltip("Seconds the ray stays hidden after the sticks are released.")]
        [SerializeField, Range(0f, 2f)] private float m_ResumeDelay = 0.4f;

        private CurveVisualController m_Curve;
        private LineRenderer m_Line;
        private ContinuousMoveProvider m_Move;
        private SnapTurnProvider m_Turn;
        private ContinuousTurnProvider m_ContinuousTurn;
        private float m_LastActiveTime = -100f;
        private bool m_Visible = true;

        private void Awake()
        {
            m_Curve = GetComponent<CurveVisualController>();
            m_Line = GetComponent<LineRenderer>();
        }

        private void OnEnable()
        {
            // Providers live on the rig, not on this object; resolve lazily because scenes can load after this component.
            m_Move = null; m_Turn = null; m_ContinuousTurn = null;
        }

        private void LateUpdate()
        {
            if (m_Curve == null) return;

            ResolveProviders();

            if (IsMovingOrTurning()) m_LastActiveTime = Time.unscaledTime;
            bool show = Time.unscaledTime - m_LastActiveTime >= m_ResumeDelay;

            if (show == m_Visible) return;
            m_Visible = show;
            m_Curve.enabled = show;
            if (m_Line != null) m_Line.enabled = show;
        }

        private void OnDisable()
        {
            // Never leave the ray stuck off if this component is removed or the rig is rebuilt.
            m_Visible = true;
            if (m_Curve != null) m_Curve.enabled = true;
        }

        private void ResolveProviders()
        {
            if (m_Move == null) m_Move = FindAnyObjectByType<ContinuousMoveProvider>(FindObjectsInactive.Include);
            if (m_Turn == null) m_Turn = FindAnyObjectByType<SnapTurnProvider>(FindObjectsInactive.Include);
            if (m_ContinuousTurn == null) m_ContinuousTurn = FindAnyObjectByType<ContinuousTurnProvider>(FindObjectsInactive.Include);
        }

        private bool IsMovingOrTurning()
        {
            float t = m_StickThreshold * m_StickThreshold;

            if (m_Move != null && m_Move.isActiveAndEnabled)
            {
                if (m_Move.leftHandMoveInput.ReadValue().sqrMagnitude > t) return true;
                if (m_Move.rightHandMoveInput.ReadValue().sqrMagnitude > t) return true;
            }
            if (m_Turn != null && m_Turn.isActiveAndEnabled)
            {
                if (m_Turn.leftHandTurnInput.ReadValue().sqrMagnitude > t) return true;
                if (m_Turn.rightHandTurnInput.ReadValue().sqrMagnitude > t) return true;
            }
            if (m_ContinuousTurn != null && m_ContinuousTurn.isActiveAndEnabled)
            {
                if (m_ContinuousTurn.leftHandTurnInput.ReadValue().sqrMagnitude > t) return true;
                if (m_ContinuousTurn.rightHandTurnInput.ReadValue().sqrMagnitude > t) return true;
            }
            return false;
        }
    }
}
