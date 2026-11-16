using System;
using UnityEngine;

namespace BMU.VRTour
{
    /// <summary>
    /// Data-only component placed on each existing stage panel. Maps the content states that panel shows
    /// (intro slides, hub, a location, a room...) to their voiceover clip. It never plays anything itself:
    /// CampusTourManager looks the clip up and hands it to TourAudioNarrator.PlayNarration, the single trigger point.
    /// </summary>
    public class PanelVoiceover : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("State key, e.g. slide-0, campus-hub, outside-gateway, hub-workshop, ws-mech.")]
            public string key;
            public AudioClip clip;
            public float startDelay = 0.3f;
        }

        [SerializeField] private Entry[] m_Entries = new Entry[0];

        public bool TryGet(string key, out AudioClip clip, out float startDelay)
        {
            clip = null;
            startDelay = 0.3f;
            if (m_Entries == null) return false;
            foreach (var e in m_Entries)
            {
                if (e != null && e.key == key)
                {
                    clip = e.clip;
                    startDelay = e.startDelay;
                    return true;
                }
            }
            return false;
        }
    }
}
