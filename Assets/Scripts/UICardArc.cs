using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Curves a row of world-space UI cards onto a cylinder centred on the viewer, so side cards
    /// turn to face the user and sit slightly closer. Only depth (z) and yaw are touched, which
    /// lets it sit alongside a HorizontalLayoutGroup that owns x/y. Works with cards spawned at
    /// runtime (e.g. interior room cards) because it re-evaluates every frame.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UICardArc : MonoBehaviour
    {
        [Tooltip("Cylinder radius in this transform's local units. On a 0.001-scale canvas, 1800 = 1.8m (the UI follow distance).")]
        [SerializeField] private float m_Radius = 1800f;

        private void LateUpdate()
        {
            if (m_Radius <= 1f) return;

            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeSelf) continue;

                Vector3 p = child.localPosition;
                float angle = p.x / m_Radius; // arc length -> radians
                float z = m_Radius * (Mathf.Cos(angle) - 1f); // negative = toward the viewer
                Quaternion yaw = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);

                // Only write when changed, to avoid re-batching the canvas every frame
                if (Mathf.Abs(p.z - z) > 0.01f)
                {
                    p.z = z;
                    child.localPosition = p;
                }
                if (Quaternion.Angle(child.localRotation, yaw) > 0.01f)
                {
                    child.localRotation = yaw;
                }
            }
        }
    }
}
