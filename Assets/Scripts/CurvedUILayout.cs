using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Arranges spatial panels along a cylindrical arc centered on the user.
    /// Provides optimal viewing ergonomics for VR headsets (Meta Quest 3S) without edge stretching.
    /// </summary>
    [ExecuteAlways]
    public class CurvedUILayout : MonoBehaviour
    {
        [Header("Arc Settings")]
        [Tooltip("Radius of the cylindrical viewing sphere in meters.")]
        [SerializeField] private float m_Radius = 1.8f;

        [Tooltip("Max angular spread in degrees across the horizontal field of view.")]
        [SerializeField] private float m_TotalSpreadAngle = 60f;

        [Tooltip("Offset angle to center the layout.")]
        [SerializeField] private float m_CenterAngleOffset = 0f;

        private void OnValidate()
        {
            UpdateLayout();
        }

        private void Start()
        {
            UpdateLayout();
        }

        [ContextMenu("Apply Curved Layout")]
        public void UpdateLayout()
        {
            int childCount = transform.childCount;
            if (childCount <= 1) return;

            float stepAngle = m_TotalSpreadAngle / (childCount - 1);
            float startAngle = (-m_TotalSpreadAngle * 0.5f) + m_CenterAngleOffset;

            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (!child.gameObject.activeSelf) continue;

                float currentAngle = startAngle + (i * stepAngle);
                float rad = currentAngle * Mathf.Deg2Rad;

                float x = Mathf.Sin(rad) * m_Radius;
                float z = (Mathf.Cos(rad) * m_Radius) - m_Radius; // Relative to canvas center

                child.localPosition = new Vector3(x, child.localPosition.y, z);
                child.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
            }
        }
    }
}
