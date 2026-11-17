using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Voiceover tooling: import settings for Assets/Audio/Voiceover and wiring of the clips into the stage panels'
    /// PanelVoiceover tables. Called by BMUTourSceneBuilder, and exposed as menus so Master_Tour_Core can be updated
    /// without a rebuild.
    /// </summary>
    public static class VoiceoverSetup
    {
        private const string Folder = "Assets/Audio/Voiceover";
        private const float StreamingThresholdSeconds = 30f;

        // clip number (leading number of the file name) -> (panel field on CampusTourManager, state key)
        private static readonly (int clip, string panelField, string key)[] Map =
        {
            (1,  "m_IntroCarouselPanel",       "slide-0"),
            (2,  "m_IntroCarouselPanel",       "slide-1"),
            (3,  "m_IntroCarouselPanel",       "slide-2"),
            (4,  "m_IntroCarouselPanel",       "slide-3"),
            (5,  "m_IntroCarouselPanel",       "slide-4"),
            (6,  "m_CampusHubPanel",           "campus-hub"),
            (7,  "m_LocationInfoPanel",        "outside-north-block"),
            (8,  "m_InternalBuildingHubPanel", "hub-north-block"),
            (9,  "m_LocationInfoPanel",        "nb-classroom"),
            (10, "m_LocationInfoPanel",        "nb-lab"),
            (11, "m_LocationInfoPanel",        "nb-faculty"),
            (12, "m_LocationInfoPanel",        "outside-gateway"),
            (13, "m_InternalBuildingHubPanel", "hub-gateway"),
            (14, "m_LocationInfoPanel",        "gw-office"),
            (15, "m_LocationInfoPanel",        "gw-ai-lab"),
            (16, "m_LocationInfoPanel",        "gw-auditorium"),
            (17, "m_LocationInfoPanel",        "outside-workshop"),
            (18, "m_InternalBuildingHubPanel", "hub-workshop"),
            (19, "m_LocationInfoPanel",        "ws-foyer"),
            (20, "m_LocationInfoPanel",        "ws-hall"),
            (21, "m_LocationInfoPanel",        "ws-mech"),
            (22, "m_LocationInfoPanel",        "nb-classroom-2"), // Standard Classroom (file: standard_room.mp3)
        };

        // Clips whose file name does not start with their clip number.
        private static readonly Dictionary<string, int> NameAliases = new Dictionary<string, int>
        {
            { "standard_room", 22 },
        };

        private static Dictionary<int, AudioClip> LoadClipsByNumber()
        {
            var result = new Dictionary<int, AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                int n;
                if (!NameAliases.TryGetValue(file, out n))
                {
                    int digits = 0;
                    while (digits < file.Length && char.IsDigit(file[digits])) digits++;
                    if (digits == 0 || !int.TryParse(file.Substring(0, digits), out n)) continue;
                }
                result[n] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return result;
        }

        /// <summary>Adds/updates a PanelVoiceover on each stage panel of the tour manager and fills in its clip entries.</summary>
        public static void ApplyToTourManager(CampusTourManager tm)
        {
            if (tm == null) return;
            var clips = LoadClipsByNumber();
            var so = new SerializedObject(tm);

            var byPanel = new Dictionary<string, List<(string key, AudioClip clip)>>();
            var missing = new List<int>();
            foreach (var m in Map)
            {
                if (!clips.TryGetValue(m.clip, out AudioClip c) || c == null) { missing.Add(m.clip); continue; }
                if (!byPanel.TryGetValue(m.panelField, out var list)) byPanel[m.panelField] = list = new List<(string, AudioClip)>();
                list.Add((m.key, c));
            }

            foreach (var kv in byPanel)
            {
                var prop = so.FindProperty(kv.Key);
                var panel = prop != null ? prop.objectReferenceValue as GameObject : null;
                if (panel == null) { Debug.LogWarning($"[Voiceover] Panel field {kv.Key} is not assigned on CampusTourManager."); continue; }

                var pv = panel.GetComponent<PanelVoiceover>();
                if (pv == null) pv = panel.AddComponent<PanelVoiceover>();

                var pso = new SerializedObject(pv);
                var entries = pso.FindProperty("m_Entries");
                entries.arraySize = kv.Value.Count;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("key").stringValue = kv.Value[i].key;
                    e.FindPropertyRelative("clip").objectReferenceValue = kv.Value[i].clip;
                    e.FindPropertyRelative("startDelay").floatValue = 0.3f;
                }
                pso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pv);
            }

            if (missing.Count > 0) Debug.LogWarning("[Voiceover] No clip found for number(s): " + string.Join(", ", missing));
        }

        [MenuItem("BMU VR Tour/Sync Voiceover Clips Into Master Scene", false, 22)]
        public static void SyncIntoMasterScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Master_Tour_Core.unity", OpenSceneMode.Single);
            var tm = Object.FindFirstObjectByType<CampusTourManager>();
            if (tm == null) { Debug.LogError("[Voiceover] CampusTourManager not found in Master_Tour_Core."); return; }
            ApplyToTourManager(tm);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Voiceover] Clip tables synced into Master_Tour_Core.");
        }

        [MenuItem("BMU VR Tour/Apply Voiceover Import Settings", false, 23)]
        public static void ApplyImportSettings()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                bool streaming = clip != null && clip.length > StreamingThresholdSeconds;

                var s = new AudioImporterSampleSettings
                {
                    loadType = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory,
                    compressionFormat = AudioCompressionFormat.Vorbis,
                    quality = 0.7f,
                    sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                    preloadAudioData = false,
                };

                importer.forceToMono = true;
                importer.loadInBackground = true;
                importer.defaultSampleSettings = s;
                importer.SetOverrideSampleSettings("Android", s); // Quest build
                importer.SaveAndReimport();
                count++;
            }
            Debug.Log($"[Voiceover] Applied import settings to {count} clips.");
        }
    }
}
