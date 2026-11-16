using System.Collections;
using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Single 2D voiceover player for the tour. One clip at a time: Play() fades the current clip out (~0.15 s)
    /// before starting the next, so clips never overlap. Mute state is saved in PlayerPrefs.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VoiceoverManager : MonoBehaviour
    {
        private const string MutePrefKey = "BMU_VoiceoverMuted";
        private const float FadeOutSeconds = 0.15f;

        private static VoiceoverManager s_Instance;
        public static VoiceoverManager Instance => s_Instance;

        private AudioSource m_Source;
        private Coroutine m_Routine;
        private bool m_IsMuted;

        public bool IsMuted => m_IsMuted;
        public bool IsPlaying => m_Source != null && m_Source.isPlaying;

        /// <summary>Returns the scene's manager, creating one next to the narrator (or on a new object) if none exists.</summary>
        public static VoiceoverManager EnsureInstance(GameObject host = null)
        {
            if (s_Instance != null) return s_Instance;
            var found = FindAnyObjectByType<VoiceoverManager>();
            if (found != null) return found;
            if (host == null) host = new GameObject("VoiceoverManager");
            return host.AddComponent<VoiceoverManager>();
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this) { Destroy(this); return; }
            s_Instance = this;

            m_Source = GetComponent<AudioSource>();
            m_Source.spatialBlend = 0f;
            m_Source.playOnAwake = false;
            m_Source.loop = false;

            m_IsMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
            m_Source.mute = m_IsMuted;
        }

        private void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        /// <summary>Stops whatever is playing (short fade) and plays <paramref name="clip"/> after <paramref name="startDelay"/>. A null clip just stops.</summary>
        public void Play(AudioClip clip, float startDelay = 0.3f)
        {
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = StartCoroutine(PlayRoutine(clip, startDelay));
        }

        public void Stop()
        {
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = StartCoroutine(PlayRoutine(null, 0f));
        }

        public void ToggleMute() => SetMuted(!m_IsMuted);

        public void SetMuted(bool muted)
        {
            m_IsMuted = muted;
            if (m_Source != null) m_Source.mute = muted;
            PlayerPrefs.SetInt(MutePrefKey, muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        private IEnumerator PlayRoutine(AudioClip next, float startDelay)
        {
            // Fade the current clip out (unscaled so it works if timeScale is 0).
            if (m_Source.isPlaying)
            {
                float startVol = m_Source.volume;
                for (float t = 0f; t < FadeOutSeconds; t += Time.unscaledDeltaTime)
                {
                    m_Source.volume = Mathf.Lerp(startVol, 0f, t / FadeOutSeconds);
                    yield return null;
                }
            }
            m_Source.Stop();
            m_Source.volume = 1f;

            if (next == null) { m_Routine = null; yield break; }

            if (startDelay > 0f) yield return new WaitForSecondsRealtime(startDelay);

            m_Source.clip = next;
            m_Source.Play();
            m_Routine = null;
        }
    }
}
