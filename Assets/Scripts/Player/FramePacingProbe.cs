using System;
using System.Collections;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Development tool: records frame times, thread times, GC allocations and draw-call counts while a
    /// CharacterController walks a route, then builds a text report (average/worst frame time, hitches).
    /// Editor numbers include editor overhead, so use them to compare before/after, not as Quest numbers.
    /// </summary>
    public class FramePacingProbe : MonoBehaviour
    {
        [SerializeField] private int m_MaxFrames = 20000;
        [SerializeField] private float m_WarmupSeconds = 1.5f;

        private float[] m_FrameMs;
        private float[] m_MainMs;
        private float[] m_RenderMs;
        private float[] m_GcBytes;
        private double m_Batches;
        private double m_SetPass;
        private double m_Triangles;
        private int m_Count;
        private bool m_Recording;
        private float m_StartTime;

        private ProfilerRecorder m_MainRecorder;
        private ProfilerRecorder m_RenderRecorder;
        private ProfilerRecorder m_GcRecorder;
        private ProfilerRecorder m_BatchRecorder;
        private ProfilerRecorder m_SetPassRecorder;
        private ProfilerRecorder m_TriangleRecorder;

        private Transform m_Track;
        private Vector3 m_LastPos;
        private float m_LastSpeed;
        private float m_MaxSpeed;
        private float m_MaxAccel;
        private float m_FirstMoveSpeed = -1f;
        private int m_AccelSpikes;

        public string LastReport { get; private set; } = "";
        public bool IsRunning { get; private set; }

        /// <summary>Starts a manual recording (e.g. while input is injected). Optionally tracks a transform's smoothness.</summary>
        public void BeginRecording(Transform track = null)
        {
            m_Count = 0;
            m_Batches = m_SetPass = m_Triangles = 0;
            m_Track = track;
            m_MaxSpeed = m_MaxAccel = m_LastSpeed = 0f;
            m_FirstMoveSpeed = -1f;
            m_AccelSpikes = 0;
            if (track != null) m_LastPos = track.position;
            m_StartTime = Time.unscaledTime;
            m_Recording = true;
        }

        public string EndRecording()
        {
            m_Recording = false;
            LastReport = BuildReport();
            return LastReport;
        }

        private void OnEnable()
        {
            m_MainRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            m_RenderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            m_GcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            m_BatchRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            m_SetPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            m_TriangleRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            m_FrameMs = new float[m_MaxFrames];
            m_MainMs = new float[m_MaxFrames];
            m_RenderMs = new float[m_MaxFrames];
            m_GcBytes = new float[m_MaxFrames];
        }

        private void OnDisable()
        {
            m_MainRecorder.Dispose();
            m_RenderRecorder.Dispose();
            m_GcRecorder.Dispose();
            m_BatchRecorder.Dispose();
            m_SetPassRecorder.Dispose();
            m_TriangleRecorder.Dispose();
        }

        private void Update()
        {
            if (!m_Recording || m_Count >= m_MaxFrames) return;
            if (Time.unscaledTime - m_StartTime < m_WarmupSeconds) return;

            m_FrameMs[m_Count] = Time.unscaledDeltaTime * 1000f;
            m_MainMs[m_Count] = (float)(m_MainRecorder.LastValue / 1e6);
            m_RenderMs[m_Count] = (float)(m_RenderRecorder.LastValue / 1e6);
            m_GcBytes[m_Count] = m_GcRecorder.LastValue;
            m_Batches += m_BatchRecorder.LastValue;
            m_SetPass += m_SetPassRecorder.LastValue;
            m_Triangles += m_TriangleRecorder.LastValue;
            m_Count++;

            if (m_Track != null && Time.unscaledDeltaTime > 0f)
            {
                Vector3 delta = m_Track.position - m_LastPos;
                delta.y = 0f;
                float speed = delta.magnitude / Time.unscaledDeltaTime;
                float accel = Mathf.Abs(speed - m_LastSpeed) / Time.unscaledDeltaTime;
                if (m_FirstMoveSpeed < 0f && speed > 0.05f) m_FirstMoveSpeed = speed;
                if (speed > m_MaxSpeed) m_MaxSpeed = speed;
                if (accel > m_MaxAccel) m_MaxAccel = accel;
                if (accel > 40f) m_AccelSpikes++;
                m_LastSpeed = speed;
                m_LastPos = m_Track.position;
            }
        }

        /// <summary>Walks the controller through the waypoints at the given speed while recording.</summary>
        public void WalkRoute(CharacterController controller, Vector3[] points, float speed)
        {
            if (IsRunning || controller == null || points == null || points.Length == 0) return;
            StartCoroutine(WalkRoutine(controller, points, speed));
        }

        private IEnumerator WalkRoutine(CharacterController cc, Vector3[] points, float speed)
        {
            IsRunning = true;
            m_Count = 0;
            m_Batches = m_SetPass = m_Triangles = 0;
            m_StartTime = Time.unscaledTime;
            m_Recording = true;

            for (int i = 0; i < points.Length; i++)
            {
                while (true)
                {
                    Vector3 flat = points[i] - cc.transform.position;
                    flat.y = 0f;
                    if (flat.magnitude < 0.3f) break;

                    Vector3 dir = flat.normalized;
                    cc.transform.rotation = Quaternion.Slerp(cc.transform.rotation, Quaternion.LookRotation(dir), 6f * Time.deltaTime);
                    cc.Move((dir * speed + Vector3.down * 2f) * Time.deltaTime);
                    yield return null;
                }
            }

            m_Recording = false;
            LastReport = BuildReport();
            IsRunning = false;
        }

        private string BuildReport()
        {
            int n = m_Count;
            if (n < 2) return "No frames recorded.";

            float[] sorted = new float[n];
            Array.Copy(m_FrameMs, sorted, n);
            Array.Sort(sorted);

            double sum = 0, sumSq = 0, mainSum = 0, renderSum = 0, gcSum = 0;
            float worstMain = 0, worstRender = 0, worstGc = 0;
            int gcFrames = 0;
            for (int i = 0; i < n; i++)
            {
                sum += m_FrameMs[i];
                sumSq += m_FrameMs[i] * m_FrameMs[i];
                mainSum += m_MainMs[i];
                renderSum += m_RenderMs[i];
                gcSum += m_GcBytes[i];
                if (m_MainMs[i] > worstMain) worstMain = m_MainMs[i];
                if (m_RenderMs[i] > worstRender) worstRender = m_RenderMs[i];
                if (m_GcBytes[i] > worstGc) worstGc = m_GcBytes[i];
                if (m_GcBytes[i] > 0) gcFrames++;
            }

            double avg = sum / n;
            double std = Math.Sqrt(Math.Max(0, sumSq / n - avg * avg));
            int hitches = 0, over33 = 0;
            for (int i = 0; i < n; i++)
            {
                if (m_FrameMs[i] > avg * 1.5) hitches++;
                if (m_FrameMs[i] > 33.3f) over33++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"frames={n} duration={sum / 1000.0:F1}s avgFps={1000.0 / avg:F1}");
            sb.AppendLine($"frame ms: avg={avg:F2} p50={sorted[n / 2]:F2} p95={sorted[(int)(n * 0.95)]:F2} p99={sorted[(int)(n * 0.99)]:F2} worst={sorted[n - 1]:F2} stdDev={std:F2}");
            sb.AppendLine($"hitches(>1.5x avg)={hitches} framesOver33ms={over33}");
            sb.AppendLine($"main thread ms: avg={mainSum / n:F2} worst={worstMain:F2} | render thread ms: avg={renderSum / n:F2} worst={worstRender:F2}");
            sb.AppendLine($"GC alloc/frame: avg={gcSum / n:F0} B worst={worstGc:F0} B framesWithAlloc={gcFrames} total={gcSum / 1024.0:F1} KB");
            sb.AppendLine($"batches avg={m_Batches / n:F0} setPass avg={m_SetPass / n:F0} triangles avg={m_Triangles / n:F0}");
            if (m_Track != null)
            {
                sb.AppendLine($"movement: speed on first moving frame={m_FirstMoveSpeed:F2} m/s, max speed={m_MaxSpeed:F2} m/s, peak accel={m_MaxAccel:F1} m/s^2, accel spikes(>40)={m_AccelSpikes}");
            }

            return sb.ToString();
        }
    }
}
