using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BMU.VRTour
{
    /// <summary>
    /// Smooth screen fader for VR comfort transitions between campus zones and rooms.
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        private static ScreenFade s_Instance;
        public static ScreenFade Instance => s_Instance;

        [SerializeField] private CanvasGroup m_FadeCanvasGroup;
        [SerializeField] private Image m_FadeImage;
        [SerializeField] private float m_DefaultFadeDuration = 0.35f;

        private Coroutine m_FadeRoutine;

        private void Awake()
        {
            if (s_Instance == null)
            {
                s_Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (m_FadeCanvasGroup == null)
            {
                m_FadeCanvasGroup = GetComponentInChildren<CanvasGroup>();
            }

            if (m_FadeCanvasGroup != null)
            {
                m_FadeCanvasGroup.alpha = 0f;
                m_FadeCanvasGroup.blocksRaycasts = false;
            }
        }

        public void FadeOut(Action onComplete = null, float duration = -1f)
        {
            float d = duration > 0f ? duration : m_DefaultFadeDuration;
            if (m_FadeRoutine != null) StopCoroutine(m_FadeRoutine);
            m_FadeRoutine = StartCoroutine(FadeRoutine(0f, 1f, d, onComplete));
        }

        public void FadeIn(Action onComplete = null, float duration = -1f)
        {
            float d = duration > 0f ? duration : m_DefaultFadeDuration;
            if (m_FadeRoutine != null) StopCoroutine(m_FadeRoutine);
            m_FadeRoutine = StartCoroutine(FadeRoutine(1f, 0f, d, onComplete));
        }

        public void FadeOutAndIn(Action onBlackout, float fadeDuration = 0.3f, float holdDuration = 0.1f)
        {
            if (m_FadeRoutine != null) StopCoroutine(m_FadeRoutine);
            m_FadeRoutine = StartCoroutine(FadeOutInRoutine(fadeDuration, holdDuration, onBlackout));
        }

        private IEnumerator FadeOutInRoutine(float fadeDuration, float holdDuration, Action onBlackout)
        {
            yield return FadeRoutine(0f, 1f, fadeDuration, null);
            onBlackout?.Invoke();
            yield return new WaitForSeconds(holdDuration);
            yield return FadeRoutine(1f, 0f, fadeDuration, null);
        }

        private IEnumerator FadeRoutine(float fromAlpha, float toAlpha, float duration, Action onComplete)
        {
            if (m_FadeCanvasGroup == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            m_FadeCanvasGroup.blocksRaycasts = toAlpha > 0.1f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                m_FadeCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
                yield return null;
            }

            m_FadeCanvasGroup.alpha = toAlpha;
            m_FadeCanvasGroup.blocksRaycasts = toAlpha > 0.1f;
            onComplete?.Invoke();
        }
    }
}
