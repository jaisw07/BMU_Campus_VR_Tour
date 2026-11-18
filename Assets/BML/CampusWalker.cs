using UnityEngine;
using UnityEngine.InputSystem;

namespace BML.CampusLayout
{
    /// <summary>WASD + mouse look, Shift run, Space jump. Keys 1-5 jump to preset views.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class CampusWalker : MonoBehaviour
    {
        public float walkSpeed = 4f, runSpeed = 9f, jump = 5f, gravity = -15f, lookSpeed = 0.12f;
        CharacterController cc; Transform cam; float pitch, vy;

        // position, yaw, pitch
        static readonly (Vector3 p, float yaw, float pitch)[] Views =
        {
            (new Vector3(-60f, 230f, -150f), 0f, 55f),      // 1 aerial overview
            (new Vector3(-86.8f, 6f, 70f), 180f, 8f),       // 2 NB front (looking south at it? NB is north of the road)
            (new Vector3(-150f, 14f, -20f), 270f, 14f),     // 3 hostel cluster (south-west)
            (new Vector3(-25f, 12f, -28f), 90f, 10f),       // 4 GA and Library
            (new Vector3(-150f, 3f, 44f), 90f, 2f),         // 5 along the north road
        };

        void Awake() { cc = GetComponent<CharacterController>(); cam = GetComponentInChildren<Camera>().transform; Cursor.lockState = CursorLockMode.Locked; }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current; if (kb == null || ms == null) return;
            if (kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (ms.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;

            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            for (int i = 0; i < keys.Length; i++) if (kb[keys[i]].wasPressedThisFrame) GoTo(Views[i]);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var d = ms.delta.ReadValue() * lookSpeed;
                transform.Rotate(0, d.x, 0); pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                cam.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            Vector3 mv = Vector3.zero;
            if (kb.wKey.isPressed) mv += transform.forward; if (kb.sKey.isPressed) mv -= transform.forward;
            if (kb.dKey.isPressed) mv += transform.right; if (kb.aKey.isPressed) mv -= transform.right;
            mv = mv.normalized * (kb.leftShiftKey.isPressed ? runSpeed : walkSpeed);
            if (cc.isGrounded) { vy = -1f; if (kb.spaceKey.wasPressedThisFrame) vy = jump; } else vy += gravity * Time.deltaTime;
            mv.y = vy; cc.Move(mv * Time.deltaTime);
        }

        void GoTo((Vector3 p, float yaw, float pitch) v)
        {
            cc.enabled = false;
            transform.position = v.p - new Vector3(0, 1.7f, 0); transform.rotation = Quaternion.Euler(0, v.yaw, 0);
            pitch = v.pitch; cam.localRotation = Quaternion.Euler(pitch, 0, 0); vy = 0; cc.enabled = true;
        }
    }

    /// <summary>Keeps a world-space label facing the camera.</summary>
    public class BillboardLabel : MonoBehaviour
    {
        void LateUpdate() { var c = Camera.main; if (c) transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position); }
    }
}
