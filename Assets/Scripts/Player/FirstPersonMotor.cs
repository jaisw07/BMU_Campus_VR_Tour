using UnityEngine;
using UnityEngine.InputSystem;

namespace BMU.VRTour
{
    /// <summary>
    /// Desktop first-person movement for walking interior/exterior scenes without a headset.
    /// Input is read in Update and one CharacterController.Move is applied per frame with Time.deltaTime.
    /// Speeds ease in and out; gravity, step offset and slope limit are set here so the capsule climbs
    /// stairs without hopping and slides along walls instead of jittering. Not used on Quest (the XR rig moves there).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMotor : MonoBehaviour
    {
        [Header("Speed (metres per second, real scale)")]
        [SerializeField] private float m_WalkSpeed = 1.6f;
        [SerializeField] private float m_RunSpeed = 3.2f;
        [SerializeField] private float m_Acceleration = 8f;
        [SerializeField] private float m_Deceleration = 12f;

        [Header("Gravity")]
        [SerializeField] private float m_Gravity = -20f;
        [SerializeField] private float m_TerminalVelocity = -30f;
        [Tooltip("Downward speed while grounded; keeps the capsule glued to stairs and ramps.")]
        [SerializeField] private float m_GroundStickSpeed = -6f;

        [Header("Capsule")]
        [SerializeField] private float m_Height = 1.8f;
        [SerializeField] private float m_Radius = 0.3f;
        [SerializeField] private float m_StepOffset = 0.3f;
        [SerializeField] private float m_SlopeLimit = 45f;
        [SerializeField] private float m_SkinWidth = 0.03f;

        [Header("Input")]
        [SerializeField, Range(0f, 0.5f)] private float m_StickDeadZone = 0.2f;

        private CharacterController m_Controller;
        private Vector3 m_HorizontalVelocity;
        private float m_VerticalVelocity;

        public Vector3 Velocity => m_HorizontalVelocity + Vector3.up * m_VerticalVelocity;

        private void Awake()
        {
            // With the XR rig loaded (tour scenes) the rig is the player: switch this desktop player off.
            if (FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>() != null)
            {
                gameObject.SetActive(false);
                return;
            }

            m_Controller = GetComponent<CharacterController>();
            m_Controller.height = m_Height;
            m_Controller.radius = m_Radius;
            m_Controller.center = new Vector3(0f, m_Height * 0.5f, 0f);
            m_Controller.stepOffset = m_StepOffset;
            m_Controller.slopeLimit = m_SlopeLimit;
            m_Controller.skinWidth = m_SkinWidth;
            m_Controller.minMoveDistance = 0f;
        }

        private void Update()
        {
            Vector2 input = ReadMoveInput(out bool run);
            Vector3 wish = Vector3.ClampMagnitude(transform.right * input.x + transform.forward * input.y, 1f);
            Vector3 target = wish * (run ? m_RunSpeed : m_WalkSpeed);

            float rate = target.sqrMagnitude > 0.0001f ? m_Acceleration : m_Deceleration;
            m_HorizontalVelocity = Vector3.MoveTowards(m_HorizontalVelocity, target, rate * Time.deltaTime);

            if (m_Controller.isGrounded && m_VerticalVelocity <= 0f)
            {
                m_VerticalVelocity = m_GroundStickSpeed;
            }
            else
            {
                m_VerticalVelocity = Mathf.Max(m_VerticalVelocity + m_Gravity * Time.deltaTime, m_TerminalVelocity);
            }

            Vector3 motion = (m_HorizontalVelocity + Vector3.up * m_VerticalVelocity) * Time.deltaTime;
            m_Controller.Move(motion);
        }

        private Vector2 ReadMoveInput(out bool run)
        {
            Vector2 move = Vector2.zero;
            run = false;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                move.x += (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                move.y += (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = ApplyRadialDeadZone(pad.leftStick.ReadValue(), m_StickDeadZone);
                if (stick.sqrMagnitude > 0f) move = stick;
                run |= pad.leftStickButton.isPressed || pad.leftTrigger.isPressed;
            }

            return Vector2.ClampMagnitude(move, 1f);
        }

        internal static Vector2 ApplyRadialDeadZone(Vector2 value, float deadZone)
        {
            float mag = value.magnitude;
            if (mag <= deadZone) return Vector2.zero;
            return value / mag * Mathf.InverseLerp(deadZone, 1f, mag);
        }
    }
}
