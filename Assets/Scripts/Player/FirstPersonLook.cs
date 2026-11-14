using UnityEngine;
using UnityEngine.InputSystem;

namespace BMU.VRTour
{
    /// <summary>
    /// Desktop mouse (and gamepad right stick) look. Raw input is accumulated in Update and applied once per frame in
    /// LateUpdate with a small smoothing, so the camera never jitters against the movement. Pitch is clamped to +/-85
    /// degrees. Esc frees the cursor; a left click locks it again. Eye height is 1.7 m and the camera near clip 0.05.
    /// </summary>
    public class FirstPersonLook : MonoBehaviour
    {
        [SerializeField] private Transform m_Head;
        [SerializeField] private float m_EyeHeight = 1.7f;
        [SerializeField] private float m_NearClip = 0.05f;
        [SerializeField] private float m_MouseSensitivity = 0.1f;
        [SerializeField] private float m_StickLookSpeed = 140f;
        [SerializeField, Range(0f, 0.5f)] private float m_StickDeadZone = 0.2f;
        [SerializeField] private float m_SmoothTime = 0.03f;
        [SerializeField] private float m_MaxPitch = 85f;

        private float m_TargetYaw;
        private float m_TargetPitch;
        private float m_Yaw;
        private float m_Pitch;
        private float m_YawVelocity;
        private float m_PitchVelocity;
        private bool m_CursorLocked;

        private void Awake()
        {
            // The XR rig owns the camera in the tour; never grab the cursor or the camera there.
            if (FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>() != null)
            {
                enabled = false;
                return;
            }

            if (m_Head == null)
            {
                Camera cam = GetComponentInChildren<Camera>(true);
                if (cam != null) m_Head = cam.transform.parent != transform ? cam.transform.parent : cam.transform;
            }

            if (m_Head != null)
            {
                m_Head.localPosition = new Vector3(0f, m_EyeHeight, 0f);
                Camera cam = m_Head.GetComponentInChildren<Camera>(true);
                if (cam != null) cam.nearClipPlane = m_NearClip;
            }

            m_Yaw = m_TargetYaw = transform.eulerAngles.y;
        }

        private void OnEnable() => SetCursorLocked(true);

        private void OnDisable() => SetCursorLocked(false);

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (!m_CursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);

            if (m_CursorLocked && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue() * m_MouseSensitivity;
                m_TargetYaw += delta.x;
                m_TargetPitch -= delta.y;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = FirstPersonMotor.ApplyRadialDeadZone(pad.rightStick.ReadValue(), m_StickDeadZone);
                m_TargetYaw += stick.x * m_StickLookSpeed * Time.deltaTime;
                m_TargetPitch -= stick.y * m_StickLookSpeed * Time.deltaTime;
            }

            m_TargetPitch = Mathf.Clamp(m_TargetPitch, -m_MaxPitch, m_MaxPitch);
        }

        private void LateUpdate()
        {
            m_Yaw = Mathf.SmoothDampAngle(m_Yaw, m_TargetYaw, ref m_YawVelocity, m_SmoothTime);
            m_Pitch = Mathf.SmoothDamp(m_Pitch, m_TargetPitch, ref m_PitchVelocity, m_SmoothTime);

            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            if (m_Head != null) m_Head.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
        }

        private void SetCursorLocked(bool locked)
        {
            m_CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
