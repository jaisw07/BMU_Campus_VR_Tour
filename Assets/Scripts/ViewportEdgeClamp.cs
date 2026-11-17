using Unity.XR.CoreUtils;
using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Keeps a floating world-space UI panel reachable and out of the walls.
    ///
    /// Put it on the panel (for the tour: MainSpatialCanvas, a child of the LazyFollowUI root, so every stage panel
    /// inside it is covered). The component never touches its parent: it remembers the panel's "home" pose relative to
    /// the parent (whatever LazyFollowUI / the drag handle does to the parent) and only adds a smoothed offset on top.
    ///
    ///  1. Viewport clamp  - if the home position leaves the safe viewport margin, the panel is pulled to the margin
    ///     edge at the same distance from the head instead of disappearing; it is released when home is back in view.
    ///  2. Wall avoidance  - a SphereCast (NonAlloc) from the head toward the wanted position stops the panel in front
    ///     of any environment collider; if it ends up closer than MinDistance it is scaled down instead of being pushed
    ///     through the player's face.
    ///
    /// Rotation is not modified (the panel keeps facing the way its parent faces it). Runs in LateUpdate, after
    /// LazyFollowUI, using the XR camera (head), not the rig root.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public class ViewportEdgeClamp : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Head camera. If empty, the XR Origin's camera (then Camera.main) is used.")]
        [SerializeField] private Camera m_Camera;

        [Header("Viewport Clamp")]
        [SerializeField] private bool m_ClampToViewport = true;
        [Tooltip("Safe margin on each side of the view (0.12 = keep the panel anchor inside the middle 76% of the view).")]
        [Range(0f, 0.4f)] [SerializeField] private float m_ViewportMargin = 0.12f;
        [Tooltip("Extra margin the anchor must be back inside by before the clamp is released (prevents flicker at the edge).")]
        [Range(0f, 0.1f)] [SerializeField] private float m_ReleaseHysteresis = 0.03f;

        [Header("Distance / Wall Avoidance")]
        [SerializeField] private bool m_AvoidWalls = true;
        [Tooltip("Use SphereCast (true) or a plain Raycast (false, cheaper).")]
        [SerializeField] private bool m_UseSphereCast = true;
        [Tooltip("Layers treated as environment. Default: everything except UI, Ignore Raycast and the Teleport layer. Triggers are always ignored.")]
        [SerializeField] private LayerMask m_CollisionMask = ~((1 << 2) | (1 << 5) | (1 << 31));
        [Tooltip("Never closer than this to the head unless a wall forces it; closer than this the panel is scaled down.")]
        [SerializeField] private float m_MinDistance = 0.6f;
        [Tooltip("Absolute closest the panel may get to the head (it shrinks instead of passing through the camera).")]
        [SerializeField] private float m_HardMinDistance = 0.3f;
        [Tooltip("Gap kept between the panel and the wall it stops in front of.")]
        [SerializeField] private float m_WallPadding = 0.1f;
        [Tooltip("SphereCast radius is half the panel's smaller dimension, capped at this value (metres).")]
        [SerializeField] private float m_MaxSphereRadius = 0.3f;
        [Tooltip("Smallest scale the panel is shrunk to when a wall is very close.")]
        [Range(0.1f, 1f)] [SerializeField] private float m_MinScale = 0.35f;

        [Header("Smoothing")]
        [Tooltip("SmoothDamp time for the position offset (seconds). 0 = instant.")]
        [SerializeField] private float m_PositionSmoothTime = 0.18f;
        [Tooltip("SmoothDamp time for the scale (seconds). 0 = instant.")]
        [SerializeField] private float m_ScaleSmoothTime = 0.18f;

        // Home pose, relative to the parent.
        private Vector3 m_HomeLocalPosition;
        private Vector3 m_HomeLocalScale;
        private bool m_HomeCaptured;

        private Vector3 m_Offset;           // world-space offset added on top of the home position
        private Vector3 m_OffsetVelocity;
        private float m_ScaleFactor = 1f;
        private float m_ScaleVelocity;
        private bool m_IsClamped;

        private Transform m_RigRoot;
        private RectTransform m_Rect;
        private readonly RaycastHit[] m_Hits = new RaycastHit[16];

        public bool ClampToViewport { get => m_ClampToViewport; set => m_ClampToViewport = value; }
        public bool AvoidWalls { get => m_AvoidWalls; set => m_AvoidWalls = value; }
        public bool IsViewportClamped => m_IsClamped;
        public float CurrentScaleFactor => m_ScaleFactor;
        /// <summary>World position the panel would have without clamping or wall avoidance.</summary>
        public Vector3 HomePosition => transform.parent != null ? transform.parent.TransformPoint(m_HomeLocalPosition) : transform.position;

        private void OnEnable()
        {
            CaptureHome();
            m_Offset = Vector3.zero;
            m_OffsetVelocity = Vector3.zero;
            m_ScaleFactor = 1f;
            m_ScaleVelocity = 0f;
            m_IsClamped = false;
            m_Rect = transform as RectTransform;
        }

        private void OnDisable()
        {
            // Hand the panel back exactly as it was.
            if (m_HomeCaptured)
            {
                transform.localPosition = m_HomeLocalPosition;
                transform.localScale = m_HomeLocalScale;
            }
        }

        private void CaptureHome()
        {
            m_HomeLocalPosition = transform.localPosition;
            m_HomeLocalScale = transform.localScale;
            m_HomeCaptured = true;
        }

        private Camera GetCamera()
        {
            if (m_Camera != null) return m_Camera;
            var origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin != null && origin.Camera != null)
            {
                m_Camera = origin.Camera;
                m_RigRoot = origin.transform;
            }
            else if (Camera.main != null)
            {
                m_Camera = Camera.main;
            }
            return m_Camera;
        }

        private void LateUpdate()
        {
            if (!m_HomeCaptured) CaptureHome();
            Camera cam = GetCamera();
            if (cam == null || transform.parent == null) return;
            if (m_RigRoot == null) m_RigRoot = cam.transform.root;

            Transform camT = cam.transform;
            Vector3 camPos = camT.position;
            Vector3 home = transform.parent.TransformPoint(m_HomeLocalPosition);

            Vector3 desired = home;
            if (m_ClampToViewport) desired = ClampToView(cam, camPos, home);

            float targetScale = 1f;
            if (m_AvoidWalls) desired = AvoidGeometry(camPos, desired, out targetScale);

            // Smooth the offset (not the position), so the parent's own follow motion passes through untouched.
            Vector3 targetOffset = desired - home;
            float dt = Time.deltaTime;
            if (m_PositionSmoothTime > 0f && dt > 0f)
                m_Offset = Vector3.SmoothDamp(m_Offset, targetOffset, ref m_OffsetVelocity, m_PositionSmoothTime, Mathf.Infinity, dt);
            else
                m_Offset = targetOffset;

            if (m_ScaleSmoothTime > 0f && dt > 0f)
                m_ScaleFactor = Mathf.SmoothDamp(m_ScaleFactor, targetScale, ref m_ScaleVelocity, m_ScaleSmoothTime, Mathf.Infinity, dt);
            else
                m_ScaleFactor = targetScale;

            transform.position = home + m_Offset;
            transform.localScale = m_HomeLocalScale * m_ScaleFactor;
        }

        // ------------------------------------------------------------------ viewport clamp
        private Vector3 ClampToView(Camera cam, Vector3 camPos, Vector3 home)
        {
            Vector3 vp = cam.WorldToViewportPoint(home);

            float lo = m_ViewportMargin;
            float hi = 1f - m_ViewportMargin;
            float rel = m_IsClamped ? m_ReleaseHysteresis : 0f; // must come back inside by an extra margin to release
            bool inside = vp.z > 0f && vp.x >= lo + rel && vp.x <= hi - rel && vp.y >= lo + rel && vp.y <= hi - rel;
            m_IsClamped = !inside;
            if (inside) return home;

            float cx, cy;
            if (vp.z > 0f)
            {
                cx = Mathf.Clamp(vp.x, lo, hi);
                cy = Mathf.Clamp(vp.y, lo, hi);
            }
            else
            {
                // Behind the head: push toward the edge in the direction the panel is "off" in head space.
                Vector3 l = cam.transform.InverseTransformPoint(home);
                Vector2 dir = new Vector2(l.x, l.y);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right; // dead behind: pick the right edge
                dir.Normalize();
                cx = Mathf.Clamp(0.5f + dir.x * 0.5f, lo, hi);
                cy = Mathf.Clamp(0.5f + dir.y * 0.5f, lo, hi);
            }

            float dist = Vector3.Distance(camPos, home);
            Ray ray = cam.ViewportPointToRay(new Vector3(cx, cy, 0f));
            return camPos + ray.direction * dist; // same distance from the head
        }

        // ------------------------------------------------------------------ wall avoidance
        private Vector3 AvoidGeometry(Vector3 camPos, Vector3 desired, out float scale)
        {
            scale = 1f;
            Vector3 toDesired = desired - camPos;
            float wanted = toDesired.magnitude;
            if (wanted < 1e-4f) return desired;
            Vector3 dir = toDesired / wanted;

            float panelMin = 0.5f;
            if (m_Rect != null)
            {
                Vector2 size = Vector2.Scale(m_Rect.rect.size, (Vector2)m_Rect.lossyScale) / Mathf.Max(0.0001f, m_ScaleFactor);
                panelMin = Mathf.Min(size.x, size.y);
            }
            float radius = Mathf.Clamp(0.5f * panelMin, 0.05f, m_MaxSphereRadius);

            float castLength = wanted + m_WallPadding;
            int count = m_UseSphereCast
                ? Physics.SphereCastNonAlloc(camPos, radius, dir, m_Hits, castLength, m_CollisionMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(camPos, dir, m_Hits, castLength, m_CollisionMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = m_Hits[i].collider;
                if (c == null) continue;
                Transform ct = c.transform;
                if (ct.IsChildOf(transform) || (transform.parent != null && ct.IsChildOf(transform.parent))) continue; // the panel itself
                if (m_RigRoot != null && ct.IsChildOf(m_RigRoot)) continue;                                           // the player / hands
                if (m_Hits[i].distance < nearest) nearest = m_Hits[i].distance;
            }

            float finalDist = wanted;
            if (nearest < float.MaxValue)
                finalDist = Mathf.Min(wanted, nearest - m_WallPadding);

            if (finalDist < m_MinDistance)
            {
                // Too close to keep the full-size panel: shrink it and keep it just in front of the head.
                scale = Mathf.Clamp(finalDist / m_MinDistance, m_MinScale, 1f);
                finalDist = Mathf.Max(finalDist, m_HardMinDistance);
            }

            return camPos + dir * finalDist;
        }
    }
}
