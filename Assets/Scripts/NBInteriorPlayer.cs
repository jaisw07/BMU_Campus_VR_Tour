using UnityEngine;
using UnityEngine.InputSystem;

namespace BMU.VRTour
{
    /// <summary>
    /// Desktop first-person player for inspecting Scene NB_Interior without a headset.
    /// WASD move, mouse look, Shift sprint, 1 / 2 teleport to the AR-VR lab / classroom, Esc frees the cursor.
    /// Time.deltaTime based: ~0.15 s acceleration / deceleration, exponentially smoothed look, camera driven in LateUpdate.
    /// Switches itself off when an XR rig is present (the Quest build uses the rig in Master_Tour_Core instead).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NBInteriorPlayer : MonoBehaviour
    {
        [System.Serializable]
        public struct TeleportTarget
        {
            public string Label;
            public Key Hotkey;
            public Transform Anchor;
        }

        [SerializeField] private Transform m_Head;
        [SerializeField] private float m_WalkSpeed = 2.0f;
        [SerializeField] private float m_SprintSpeed = 4.0f;
        [SerializeField] private float m_AccelerationTime = 0.15f;   // seconds to reach full speed
        [SerializeField] private float m_DecelerationTime = 0.15f;   // seconds to stop
        [SerializeField] private float m_LookSensitivity = 0.1f;
        [SerializeField] private float m_LookSmoothTime = 0.04f;     // exponential smoothing time constant
        [SerializeField] private float m_Gravity = -15f;
        [SerializeField] private TeleportTarget[] m_Teleports;

        private CharacterController m_Controller;
        private Vector3 m_Velocity;
        private float m_VerticalVelocity;
        private float m_DecelRate;
        private bool m_WasAccelerating;
        private float m_TargetYaw, m_TargetPitch, m_Yaw, m_Pitch;
        private bool m_CursorLocked;

        // A scene can hold several of these (one per room, e.g. the Gateway lab and auditorium). Only the first one to wake is
        // the active player; the others go dormant (camera, listener and Update off) but still lend their anchors and hotkeys,
        // so a hotkey pressed in one room teleports the single active player to any room's anchor.
        private static readonly System.Collections.Generic.List<NBInteriorPlayer> s_All = new System.Collections.Generic.List<NBInteriorPlayer>();
        private static NBInteriorPlayer s_Active;
        private bool m_Dormant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_All.Clear(); s_Active = null; }

        private void Awake()
        {
            if (Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>() != null)
            {
                gameObject.SetActive(false);
                return;
            }
            m_Controller = GetComponent<CharacterController>();
            s_All.Add(this);
            if (s_Active != null && s_Active != this)
            {
                m_Dormant = true;
                foreach (Camera c in GetComponentsInChildren<Camera>(true)) c.enabled = false;
                foreach (AudioListener l in GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
                enabled = false;
                return;
            }
            s_Active = this;
            m_Yaw = m_TargetYaw = transform.eulerAngles.y;
        }

        private void OnDestroy()
        {
            s_All.Remove(this);
            if (s_Active == this) s_Active = null;
        }

        private void OnEnable() { if (!m_Dormant) SetCursorLocked(true); }
        private void OnDisable() { if (!m_Dormant) SetCursorLocked(false); }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (!m_CursorLocked && mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);

            foreach (NBInteriorPlayer p in s_All)
            {
                if (p == null || p.m_Teleports == null) continue;
                foreach (TeleportTarget t in p.m_Teleports)
                    if (t.Anchor != null && kb[t.Hotkey].wasPressedThisFrame) TeleportTo(t.Anchor);
            }

            if (m_CursorLocked)
            {
                Vector2 look = mouse.delta.ReadValue() * m_LookSensitivity;
                m_TargetYaw += look.x;
                m_TargetPitch = Mathf.Clamp(m_TargetPitch - look.y, -85f, 85f);
            }

            float dt = Time.deltaTime;
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            // Use the smoothed yaw so movement direction and view direction always agree.
            Quaternion yawRot = Quaternion.Euler(0f, m_Yaw, 0f);
            Vector3 wish = Vector3.ClampMagnitude(yawRot * new Vector3(x, 0f, z), 1f);
            float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? m_SprintSpeed : m_WalkSpeed;

            bool accelerating = wish.sqrMagnitude > 0.0001f;
            if (!accelerating && m_WasAccelerating)
                m_DecelRate = m_Velocity.magnitude / Mathf.Max(0.01f, m_DecelerationTime); // stop takes m_DecelerationTime from ANY speed
            m_WasAccelerating = accelerating;
            // m/s^2: full speed takes m_AccelerationTime; a stop takes m_DecelerationTime (rate fixed when the keys are released).
            float rate = accelerating ? speed / Mathf.Max(0.01f, m_AccelerationTime) : m_DecelRate;
            m_Velocity = Vector3.MoveTowards(m_Velocity, wish * speed, rate * dt);

            m_VerticalVelocity = m_Controller.isGrounded ? -1f : m_VerticalVelocity + m_Gravity * dt;
            m_Controller.Move((m_Velocity + Vector3.up * m_VerticalVelocity) * dt);
        }

        // Camera/look is applied after all movement so the view never lags or jitters against the body.
        private void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, m_LookSmoothTime));
            m_Yaw = Mathf.LerpAngle(m_Yaw, m_TargetYaw, k);
            m_Pitch = Mathf.Lerp(m_Pitch, m_TargetPitch, k);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            if (m_Head != null) m_Head.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
        }

        public void TeleportTo(Transform anchor)
        {
            // CharacterController must be off while we set the position, or it fights the move.
            m_Controller.enabled = false;
            transform.position = anchor.position;
            m_Yaw = m_TargetYaw = anchor.eulerAngles.y;
            m_Pitch = m_TargetPitch = 0f;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_Velocity = Vector3.zero;
            m_VerticalVelocity = 0f;
            m_WasAccelerating = false;
            m_Controller.enabled = true;
        }

        private void SetCursorLocked(bool locked)
        {
            m_CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
