using UnityEngine;
using UnityEngine.InputSystem;

namespace BMU.VRTour
{
    /// <summary>
    /// Desktop first-person walker for the Library + Medical Center preview scene.
    /// WASD move, mouse look, Shift run, Space jump, keys 1-4 jump to preset views.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class LibraryWalkerController : MonoBehaviour
    {
        [System.Serializable]
        public struct ViewPreset { public string label; public Vector3 position; public Vector3 euler; }

        [SerializeField] Transform m_Camera;
        [SerializeField] float m_WalkSpeed = 3.5f;
        [SerializeField] float m_RunSpeed = 7f;
        [SerializeField] float m_JumpHeight = 1.1f;
        [SerializeField] float m_Gravity = -20f;
        [SerializeField] float m_LookSensitivity = 0.1f;
        [SerializeField] float m_Smoothing = 12f;
        [SerializeField] ViewPreset[] m_Views = new ViewPreset[4];

        CharacterController m_Controller;
        Vector3 m_Velocity, m_PlanarVelocity;
        float m_Pitch;
        bool m_Free; // aerial/overview views fly the camera; walking resumes on any move key

        void Awake() { m_Controller = GetComponent<CharacterController>(); }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

            if (kb.digit1Key.wasPressedThisFrame) GoToView(0);
            if (kb.digit2Key.wasPressedThisFrame) GoToView(1);
            if (kb.digit3Key.wasPressedThisFrame) GoToView(2);
            if (kb.digit4Key.wasPressedThisFrame) GoToView(3);

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue() * m_LookSensitivity;
                transform.Rotate(0f, d.x, 0f);
                m_Pitch = Mathf.Clamp(m_Pitch - d.y, -89f, 89f);
                m_Camera.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            }

            Vector2 move = Vector2.zero;
            if (kb.wKey.isPressed) move.y += 1;
            if (kb.sKey.isPressed) move.y -= 1;
            if (kb.dKey.isPressed) move.x += 1;
            if (kb.aKey.isPressed) move.x -= 1;
            move = Vector2.ClampMagnitude(move, 1f);

            float speed = kb.leftShiftKey.isPressed ? m_RunSpeed : m_WalkSpeed;
            Vector3 target = (transform.right * move.x + transform.forward * move.y) * speed;
            m_PlanarVelocity = Vector3.Lerp(m_PlanarVelocity, target, 1f - Mathf.Exp(-m_Smoothing * Time.deltaTime));

            if (m_Controller.isGrounded && m_Velocity.y < 0f) m_Velocity.y = -2f;
            if (m_Controller.isGrounded && kb.spaceKey.wasPressedThisFrame)
                m_Velocity.y = Mathf.Sqrt(m_JumpHeight * -2f * m_Gravity);
            m_Velocity.y += m_Gravity * Time.deltaTime;

            m_Controller.Move((m_PlanarVelocity + new Vector3(0f, m_Velocity.y, 0f)) * Time.deltaTime);
        }

        /// <summary>Moves the body so the camera (eye) lands at the preset position, then rotates.</summary>
        public void GoToView(int index)
        {
            if (m_Views == null || index < 0 || index >= m_Views.Length) return;
            var v = m_Views[index];
            float eye = m_Camera.localPosition.y;
            m_Controller.enabled = false;
            transform.position = v.position - new Vector3(0f, eye, 0f);
            transform.rotation = Quaternion.Euler(0f, v.euler.y, 0f);
            m_Pitch = v.euler.x > 180f ? v.euler.x - 360f : v.euler.x;
            m_Camera.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            m_Controller.enabled = true;
            m_Velocity = Vector3.zero;
            m_PlanarVelocity = Vector3.zero;
        }
    }
}
