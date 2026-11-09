using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Implements comfortable deadzone-based soft "lazy follow" for spatial VR UI.
    /// Anchors in world space during slight head/glance movements, and gently glides
    /// into view when the user looks past the deadzone threshold.
    /// </summary>
    public class LazyFollowUI : MonoBehaviour
    {
        [Header("Target Tracking")]
        [Tooltip("Camera / HMD transform to follow. If null, Camera.main is used.")]
        [SerializeField] private Transform m_TargetCamera;

        [Header("Positioning")]
        [Tooltip("Distance from the HMD in meters.")]
        [SerializeField] private float m_FollowDistance = 1.8f;

        [Tooltip("Vertical height offset relative to HMD eye level.")]
        [SerializeField] private float m_VerticalOffset = -0.12f;

        [Header("Comfort Deadzones")]
        [Tooltip("Horizontal angle threshold (in degrees) before UI begins following.")]
        [SerializeField] private float m_MaxHorizontalAngle = 22f;

        [Tooltip("Vertical angle threshold (in degrees) before UI adjusts height/pitch.")]
        [SerializeField] private float m_MaxVerticalAngle = 14f;

        [Header("Damping & Motion")]
        [Tooltip("Speed multiplier for translational follow.")]
        [SerializeField] private float m_PositionSmoothSpeed = 4.5f;

        [Tooltip("Speed multiplier for rotational follow.")]
        [SerializeField] private float m_RotationSmoothSpeed = 4.0f;

        [Tooltip("True to lock rotation strictly to horizon (no head roll).")]
        [SerializeField] private bool m_LockPitchAndRoll = true;

        [Tooltip("If true, UI will remain pinned in world space until recentered.")]
        [SerializeField] private bool m_IsPinned = false;

        private Vector3 m_TargetPosition;
        private Quaternion m_TargetRotation;
        private bool m_IsFirstFrame = true;

        public bool IsPinned
        {
            get => m_IsPinned;
            set => m_IsPinned = value;
        }

        public void Pin()
        {
            m_IsPinned = true;
        }

        public void Unpin()
        {
            m_IsPinned = false;
        }

        /// <summary>
        /// Snaps the UI directly in front of the user gaze (1.8m distance, eye level)
        /// and unpins LazyFollow back to gentle follow mode.
        /// </summary>
        public void RecenterToCurrentGaze()
        {
            m_IsPinned = false;
            RecenterInstant();
        }

        private void Start()
        {
            if (m_TargetCamera == null && Camera.main != null)
            {
                m_TargetCamera = Camera.main.transform;
            }

            RecenterInstant();
        }

        private void LateUpdate()
        {
            if (m_TargetCamera == null)
            {
                if (Camera.main != null)
                {
                    m_TargetCamera = Camera.main.transform;
                }
                else
                {
                    return;
                }
            }

            if (m_IsFirstFrame)
            {
                RecenterInstant();
                m_IsFirstFrame = false;
                return;
            }

            if (m_IsPinned) return;

            EvaluateDeadzoneAndFollow();
        }

        private void EvaluateDeadzoneAndFollow()
        {
            Vector3 camPos = m_TargetCamera.position;
            Vector3 camForward = m_TargetCamera.forward;

            // Direction from camera to current UI position
            Vector3 toCurrent = (transform.position - camPos).normalized;

            // Compute angle between camera forward and current UI direction
            float horizontalAngle = Vector3.Angle(Vector3.ProjectOnPlane(camForward, Vector3.up), Vector3.ProjectOnPlane(toCurrent, Vector3.up));
            float verticalAngle = Vector3.Angle(Vector3.ProjectOnPlane(camForward, m_TargetCamera.right), Vector3.ProjectOnPlane(toCurrent, m_TargetCamera.right));

            bool outsideDeadzone = horizontalAngle > m_MaxHorizontalAngle || verticalAngle > m_MaxVerticalAngle;

            if (outsideDeadzone)
            {
                // Calculate ideal target position
                Vector3 forwardFlat = m_LockPitchAndRoll ? Vector3.ProjectOnPlane(camForward, Vector3.up).normalized : camForward;
                if (forwardFlat.sqrMagnitude < 0.001f) forwardFlat = m_TargetCamera.forward;

                m_TargetPosition = camPos + (forwardFlat * m_FollowDistance) + (Vector3.up * m_VerticalOffset);

                // Face the user with horizon lock
                Vector3 lookDir = (m_TargetPosition - camPos).normalized;
                if (m_LockPitchAndRoll)
                {
                    lookDir.y = 0;
                }

                if (lookDir.sqrMagnitude > 0.001f)
                {
                    m_TargetRotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
            }

            // Smoothly interpolate towards target
            float dt = Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, m_TargetPosition, dt * m_PositionSmoothSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, m_TargetRotation, dt * m_RotationSmoothSpeed);
        }

        /// <summary>
        /// Recenter the UI immediately in front of user's current gaze.
        /// </summary>
        public void RecenterInstant()
        {
            if (m_TargetCamera == null)
            {
                if (Camera.main != null) m_TargetCamera = Camera.main.transform;
                else return;
            }

            Vector3 camPos = m_TargetCamera.position;
            Vector3 camForward = m_TargetCamera.forward;
            Vector3 forwardFlat = m_LockPitchAndRoll ? Vector3.ProjectOnPlane(camForward, Vector3.up).normalized : camForward;
            if (forwardFlat.sqrMagnitude < 0.001f) forwardFlat = Vector3.forward;

            m_TargetPosition = camPos + (forwardFlat * m_FollowDistance) + (Vector3.up * m_VerticalOffset);
            Vector3 lookDir = (m_TargetPosition - camPos).normalized;
            if (m_LockPitchAndRoll) lookDir.y = 0;

            m_TargetRotation = Quaternion.LookRotation(lookDir, Vector3.up);

            transform.position = m_TargetPosition;
            transform.rotation = m_TargetRotation;
        }

        public void SetFollowDistance(float distance)
        {
            m_FollowDistance = distance;
            RecenterInstant();
        }
    }
}
