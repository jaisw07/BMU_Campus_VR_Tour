using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BMU.VRTour
{
    /// <summary>
    /// Manages tour narration, subtitle captions, mute state, and animated waveform equalizer HUD.
    /// Provides dynamic visual feedback for active vs muted audio states and procedural caption playback.
    /// </summary>
    public class TourAudioNarrator : MonoBehaviour
    {
        private static TourAudioNarrator s_Instance;
        public static TourAudioNarrator Instance => s_Instance;

        [Header("Audio Components")]
        [SerializeField] private AudioSource m_AudioSource;

        [Header("HUD Elements")]
        [SerializeField] private TextMeshProUGUI m_CaptionText;
        [SerializeField] private Button m_AudioToggleButton;
        [SerializeField] private TextMeshProUGUI m_ToggleButtonText;
        [SerializeField] private Image m_ToggleButtonImage;
        [SerializeField] private GameObject m_SpeakerIconOn;
        [SerializeField] private GameObject m_SpeakerIconOff;
        [SerializeField] private RectTransform[] m_WaveformBars;

        [Header("Waveform Settings")]
        [SerializeField] private float m_MinBarHeight = 4f;
        [SerializeField] private float m_MaxBarHeight = 24f;
        [SerializeField] private float m_WaveSpeed = 8f;

        [Header("Visual Feedback Styling")]
        [SerializeField] private Color m_ActiveCaptionColor = Color.black;                          // #000000
        [SerializeField] private Color m_MutedCaptionColor = new Color(0.541f, 0.608f, 0.659f, 1f); // #8A9BA8
        [SerializeField] private Color m_ActiveButtonColor = new Color(0f, 0.294f, 0.529f, 1f);     // #004B87
        [SerializeField] private Color m_MutedButtonColor = new Color(0.333f, 0.333f, 0.333f, 1f);    // #555555

        private bool m_IsMuted = false;
        private bool m_IsPlaying = false;
        private string m_CurrentCaption = "";
        private Coroutine m_TypewriterRoutine;

        public bool IsMuted => m_IsMuted;

        private void Awake()
        {
            if (s_Instance == null) s_Instance = this;
            else { Destroy(gameObject); return; }

            if (m_AudioSource == null) m_AudioSource = GetComponent<AudioSource>();
        }

        private void Start()
        {
            if (m_ToggleButtonImage == null && m_AudioToggleButton != null)
            {
                m_ToggleButtonImage = m_AudioToggleButton.GetComponent<Image>();
            }

            if (m_ToggleButtonText == null && m_AudioToggleButton != null)
            {
                m_ToggleButtonText = m_AudioToggleButton.GetComponentInChildren<TextMeshProUGUI>();
            }

            if (m_AudioToggleButton != null)
            {
                m_AudioToggleButton.onClick.AddListener(ToggleMute);
            }

            UpdateMuteVisuals();
        }

        private void Update()
        {
            UpdateWaveform();
        }

        private void UpdateWaveform()
        {
            if (m_WaveformBars == null || m_WaveformBars.Length == 0) return;

            if (m_IsMuted)
            {
                // Muted State: Waveform visualizer bars flatten to zero height with no movement
                for (int i = 0; i < m_WaveformBars.Length; i++)
                {
                    if (m_WaveformBars[i] == null) continue;
                    Vector2 size = m_WaveformBars[i].sizeDelta;
                    size.y = Mathf.Lerp(size.y, 0f, Time.deltaTime * 18f);
                    m_WaveformBars[i].sizeDelta = size;
                }
                return;
            }

            bool active = m_IsPlaying;

            for (int i = 0; i < m_WaveformBars.Length; i++)
            {
                if (m_WaveformBars[i] == null) continue;

                float targetHeight = m_MinBarHeight;
                if (active)
                {
                    // Procedural animated equalizer wave
                    float freq = (i + 1) * 1.5f;
                    float wave = Mathf.Sin((Time.time * m_WaveSpeed) + freq) * 0.5f + 0.5f;
                    targetHeight = Mathf.Lerp(m_MinBarHeight, m_MaxBarHeight, wave);
                }

                Vector2 size = m_WaveformBars[i].sizeDelta;
                size.y = Mathf.Lerp(size.y, targetHeight, Time.deltaTime * 15f);
                m_WaveformBars[i].sizeDelta = size;
            }
        }

        public void PlayNarration(string caption, AudioClip clip = null)
        {
            m_CurrentCaption = caption;

            if (m_TypewriterRoutine != null) StopCoroutine(m_TypewriterRoutine);
            m_TypewriterRoutine = StartCoroutine(TypewriterCaption(caption));

            if (clip != null && m_AudioSource != null)
            {
                m_AudioSource.clip = clip;
                if (!m_IsMuted)
                {
                    m_AudioSource.Play();
                }
                m_IsPlaying = true;
            }
            else
            {
                // Fallback simulated speaking duration based on caption length
                m_IsPlaying = true;
                StartCoroutine(StopPlayingAfterDelay(Mathf.Clamp(caption.Length * 0.05f, 3f, 8f)));
            }
        }

        private IEnumerator StopPlayingAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            m_IsPlaying = false;
        }

        private IEnumerator TypewriterCaption(string text)
        {
            if (m_CaptionText == null) yield break;

            m_CaptionText.color = m_IsMuted ? m_MutedCaptionColor : m_ActiveCaptionColor;
            m_CaptionText.text = $"\"{text}\"";
            yield break;
        }

        public void ToggleMute()
        {
            m_IsMuted = !m_IsMuted;
            if (m_AudioSource != null)
            {
                m_AudioSource.mute = m_IsMuted;
            }

            UpdateMuteVisuals();
        }

        private void UpdateMuteVisuals()
        {
            if (m_SpeakerIconOn != null) m_SpeakerIconOn.SetActive(!m_IsMuted);
            if (m_SpeakerIconOff != null) m_SpeakerIconOff.SetActive(m_IsMuted);

            // Active vs Muted Caption Color
            if (m_CaptionText != null)
            {
                m_CaptionText.color = m_IsMuted ? m_MutedCaptionColor : m_ActiveCaptionColor;
            }

            // Audio toggle button text: "VOICE" vs "MUTED"
            if (m_ToggleButtonText != null)
            {
                m_ToggleButtonText.text = m_IsMuted ? "MUTED" : "VOICE";
            }

            // Audio toggle button background color: brand blue (#004B87) vs dark grey (#555555)
            Color targetBtnColor = m_IsMuted ? m_MutedButtonColor : m_ActiveButtonColor;
            if (m_ToggleButtonImage != null)
            {
                m_ToggleButtonImage.color = targetBtnColor;
            }

            if (m_AudioToggleButton != null)
            {
                ColorBlock cb = m_AudioToggleButton.colors;
                cb.normalColor = targetBtnColor;
                cb.highlightedColor = Color.Lerp(targetBtnColor, Color.white, 0.35f);
                cb.selectedColor = cb.highlightedColor;
                m_AudioToggleButton.colors = cb;

                var feedback = m_AudioToggleButton.GetComponent<VRButtonFeedback>();
                if (feedback != null)
                {
                    feedback.SetBaseColor(targetBtnColor);
                }
            }

            if (m_IsMuted && m_WaveformBars != null)
            {
                for (int i = 0; i < m_WaveformBars.Length; i++)
                {
                    if (m_WaveformBars[i] != null)
                    {
                        Vector2 s = m_WaveformBars[i].sizeDelta;
                        s.y = 0f;
                        m_WaveformBars[i].sizeDelta = s;
                    }
                }
            }
        }
    }
}
