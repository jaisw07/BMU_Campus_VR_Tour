using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace BMU.VRTour
{
    /// <summary>
    /// Centralized VR controller haptic feedback for Quest / OpenXR controllers (via XRI HapticImpulsePlayer
    /// + InputDevices XRNode fallback + optional Gamepad rumble). Delivers subtle, well-paced pulses for
    /// UI hover/click, zone arrival/exit, slide navigation, footstep cadence, and wall/boundary collision bumps.
    /// </summary>
    public class VRHaptics : MonoBehaviour
    {
        private static VRHaptics s_Instance;

        private readonly List<HapticImpulsePlayer> m_LeftPlayers = new List<HapticImpulsePlayer>();
        private readonly List<HapticImpulsePlayer> m_RightPlayers = new List<HapticImpulsePlayer>();
        private float m_LastBumpTime = -10f;
        private float m_LastScanTime = -10f;
        private Coroutine m_GamepadStopRoutine;

        private static VRHaptics EnsureInstance()
        {
            if (s_Instance != null) return s_Instance;
            s_Instance = Object.FindAnyObjectByType<VRHaptics>();
            if (s_Instance == null)
            {
                var go = new GameObject("[VR_Haptics_Manager]");
                s_Instance = go.AddComponent<VRHaptics>();
            }
            return s_Instance;
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            RefreshPlayers();
        }

        private void RefreshPlayers()
        {
            m_LeftScanSafe();
        }

        private void m_LeftScanSafe()
        {
            m_LastScanTime = Time.unscaledTime;
            m_LeftPlayers.Clear();
            m_RightPlayers.Clear();

            var players = Object.FindObjectsByType<HapticImpulsePlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var p in players)
            {
                if (p == null) continue;
                string full = GetHierarchyPathLower(p.transform);
                if (full.Contains("left")) m_LeftPlayers.Add(p);
                else if (full.Contains("right")) m_RightPlayers.Add(p);
                else
                {
                    m_LeftPlayers.Add(p);
                    m_RightPlayers.Add(p);
                }
            }
        }

        private static string GetHierarchyPathLower(Transform t)
        {
            string s = t.name;
            Transform cur = t.parent;
            while (cur != null)
            {
                s = cur.name + "/" + s;
                cur = cur.parent;
            }
            return s.ToLowerInvariant();
        }

        public static void PulseHover()
        {
            EnsureInstance().SendPulse(true, true, 0.14f, 0.025f);
        }

        public static void PulseClick()
        {
            EnsureInstance().SendPulse(true, true, 0.42f, 0.055f);
        }

        public static void PulseSlide()
        {
            EnsureInstance().SendPulse(false, true, 0.24f, 0.038f);
        }

        public static void PulseZoneEnter()
        {
            EnsureInstance().StartPattern(0.36f, 0.055f, 0.065f, 0.58f, 0.085f);
        }

        public static void PulseZoneExit()
        {
            EnsureInstance().SendPulse(true, true, 0.16f, 0.035f);
        }

        public static void PulseFootstep(bool leftFoot)
        {
            EnsureInstance().SendPulse(leftFoot, !leftFoot, 0.09f, 0.024f);
        }

        public static void PulseWallBump()
        {
            var inst = EnsureInstance();
            if (Time.unscaledTime - inst.m_LastBumpTime < 0.28f) return;
            inst.m_LastBumpTime = Time.unscaledTime;
            inst.SendPulse(true, true, 0.40f, 0.065f);
        }

        public static void PulseBoundaryAlert()
        {
            EnsureInstance().StartPattern(0.65f, 0.09f, 0.07f, 0.72f, 0.11f);
        }

        private void StartPattern(float amp1, float dur1, float gap, float amp2, float dur2)
        {
            StartCoroutine(PatternRoutine(amp1, dur1, gap, amp2, dur2));
        }

        private IEnumerator PatternRoutine(float amp1, float dur1, float gap, float amp2, float dur2)
        {
            SendPulse(true, true, amp1, dur1);
            yield return new WaitForSecondsRealtime(dur1 + gap);
            SendPulse(true, true, amp2, dur2);
        }

        private void SendPulse(bool left, bool right, float amplitude, float duration)
        {
            if (Time.unscaledTime - m_LastScanTime > 4f || (m_LeftPlayers.Count == 0 && m_RightPlayers.Count == 0))
            {
                RefreshPlayers();
            }

            bool sentAny = false;
            if (left)
            {
                foreach (var p in m_LeftPlayers)
                {
                    if (p != null && p.gameObject.activeInHierarchy && p.SendHapticImpulse(amplitude, duration))
                        sentAny = true;
                }
                if (!sentAny)
                {
                    var dev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                    if (dev.isValid && dev.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                        dev.SendHapticImpulse(0u, amplitude, duration);
                }
            }

            if (right)
            {
                bool sentRight = false;
                foreach (var p in m_RightPlayers)
                {
                    if (p != null && p.gameObject.activeInHierarchy && p.SendHapticImpulse(amplitude, duration))
                        sentRight = true;
                }
                if (!sentRight)
                {
                    var dev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                    if (dev.isValid && dev.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                        dev.SendHapticImpulse(0u, amplitude, duration);
                }
            }

            if (Gamepad.current != null && amplitude >= 0.20f)
            {
                Gamepad.current.SetMotorSpeeds(left ? amplitude * 0.4f : 0f, right ? amplitude * 0.4f : 0f);
                if (m_GamepadStopRoutine != null) StopCoroutine(m_GamepadStopRoutine);
                m_GamepadStopRoutine = StartCoroutine(StopGamepadAfter(duration));
            }
        }

        private IEnumerator StopGamepadAfter(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            if (Gamepad.current != null) Gamepad.current.SetMotorSpeeds(0f, 0f);
            m_GamepadStopRoutine = null;
        }
    }
}
