using UnityEngine;
using UnityEngine.InputSystem;

namespace BMU.VRTour
{
    /// <summary>
    /// Desktop walk-through controller for testing interior scenes without a headset.
    /// WASD move, mouse look, Shift run, Esc frees the cursor (click to lock it again).
    /// Not used in the Quest build; the VR rig in Master_Tour_Core handles movement there.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonWalker : MonoBehaviour
    {
        [SerializeField] private Transform m_Head;
        [SerializeField] private float m_WalkSpeed = 12f;
        [SerializeField] private float m_RunSpeed = 20f;
        [SerializeField] private float m_LookSensitivity = 0.1f;
        [SerializeField] private float m_Gravity = -15f;
        [SerializeField] private float m_Acceleration = 12f;
        [SerializeField] private float m_Deceleration = 16f;

        private Vector3 m_Velocity;
        private Vector2 m_PendingLook;
        private CharacterController m_Controller;
        private float m_Pitch;
        private float m_VerticalVelocity;
        private bool m_CursorLocked;

        private void Awake()
        {
            // When this scene is loaded additively into Master_Tour_Core the XR rig is the player:
            // switch off this desktop player (and its camera/listener) so there is only one.
            if (UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>() != null)
            {
                gameObject.SetActive(false);
                return;
            }

            m_Controller = GetComponent<CharacterController>();
        }

        private void OnEnable() => SetCursorLocked(true);

        private void OnDisable() => SetCursorLocked(false);

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (!m_CursorLocked && mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);

            // Mouse look is only accumulated here; it is applied in LateUpdate so the camera never lags the body.
            if (m_CursorLocked) m_PendingLook += mouse.delta.ReadValue() * m_LookSensitivity;

            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector3 wish = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
            float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? m_RunSpeed : m_WalkSpeed;

            // Smooth acceleration / deceleration instead of snapping to full speed.
            float rate = wish.sqrMagnitude > 0.0001f ? m_Acceleration : m_Deceleration;
            m_Velocity = Vector3.MoveTowards(m_Velocity, wish * speed, rate * Time.deltaTime);

            m_VerticalVelocity = m_Controller.isGrounded ? -1f : m_VerticalVelocity + m_Gravity * Time.deltaTime;
            m_Controller.Move((m_Velocity + Vector3.up * m_VerticalVelocity) * Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (m_PendingLook == Vector2.zero) return;
            transform.Rotate(0f, m_PendingLook.x, 0f);
            m_Pitch = Mathf.Clamp(m_Pitch - m_PendingLook.y, -85f, 85f);
            if (m_Head != null) m_Head.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            m_PendingLook = Vector2.zero;
        }

        private void SetCursorLocked(bool locked)
        {
            m_CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
