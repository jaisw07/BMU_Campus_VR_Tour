using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace BMU.VRTour
{
    /// <summary>
    /// Master Scene Transition and Locomotion Anchor Controller for BMU VR Tour.
    /// Coordinates asynchronous additive scene loading, memory unloads between exterior and interior
    /// environments, screen fade comfort blackouts, and XR Origin anchor repositioning.
    /// </summary>
    public class SceneTransitionManager : MonoBehaviour
    {
        private static SceneTransitionManager s_Instance;
        public static SceneTransitionManager Instance => s_Instance;

        // Scene Names
        public const string MasterSceneName = "Master_Tour_Core";
        public const string ExteriorSceneName = "Scene_Campus_Exterior";
        public const string NorthBlockInteriorsSceneName = "Scene_NorthBlock_Interiors";
        public const string GatewayInteriorsSceneName = "Scene_Gateway_Interiors";

        [Header("XR Rig References")]
        [SerializeField] private XROrigin m_XROrigin;
        [SerializeField] private CharacterController m_CharacterController;
        [SerializeField] private ScreenFade m_ScreenFade;

        [Header("Transition Settings")]
        [SerializeField] private float m_FadeDuration = 0.35f;
        [SerializeField] private float m_BlackoutHold = 0.08f;

        private string m_CurrentLoadedSubScene = "";
        private Transform m_CurrentAnchorTransform;
        private bool m_IsTransitioning = false;

        public Transform CurrentAnchorTransform => m_CurrentAnchorTransform;
        public bool IsTransitioning => m_IsTransitioning;
        public string CurrentLoadedSubScene => m_CurrentLoadedSubScene;

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

            FindRigReferences();
        }

        private void Start()
        {
            FindRigReferences();

            // If we started in Master_Tour_Core and no sub-scene is loaded, load Exterior by default
            if (SceneManager.GetActiveScene().name == MasterSceneName || gameObject.scene.name == MasterSceneName)
            {
                if (!IsSceneLoaded(ExteriorSceneName) &&
                    !IsSceneLoaded(NorthBlockInteriorsSceneName) &&
                    !IsSceneLoaded(GatewayInteriorsSceneName))
                {
                    StartCoroutine(InitialExteriorLoadRoutine());
                }
                else
                {
                    DetectCurrentLoadedScene();
                }
            }
            else
            {
                DetectCurrentLoadedScene();
            }
        }

        private void FindRigReferences()
        {
            if (m_XROrigin == null)
            {
                m_XROrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            }

            if (m_CharacterController == null && m_XROrigin != null)
            {
                m_CharacterController = m_XROrigin.GetComponent<CharacterController>();
            }

            if (m_ScreenFade == null)
            {
                m_ScreenFade = ScreenFade.Instance != null ? ScreenFade.Instance : UnityEngine.Object.FindAnyObjectByType<ScreenFade>();
            }
        }

        private void DetectCurrentLoadedScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene sc = SceneManager.GetSceneAt(i);
                if (sc.isLoaded && sc.name != MasterSceneName)
                {
                    m_CurrentLoadedSubScene = sc.name;
                    break;
                }
            }
        }

        private IEnumerator InitialExteriorLoadRoutine()
        {
            yield return StartCoroutine(LoadSceneAdditiveRoutine(ExteriorSceneName));
            Transform frontGateAnchor = FindExteriorAnchor("front-gate");
            if (frontGateAnchor != null)
            {
                MoveRigToAnchor(frontGateAnchor);
            }
        }

        // ====================================================================
        // EXTERIOR TRANSITIONS
        // ====================================================================
        public void TransitionToExterior(string locationKey, Action onComplete = null)
        {
            if (m_IsTransitioning) return;
            StartCoroutine(TransitionToExteriorRoutine(locationKey, onComplete));
        }

        private IEnumerator TransitionToExteriorRoutine(string locationKey, Action onComplete)
        {
            m_IsTransitioning = true;

            // 1. Fade Out
            if (m_ScreenFade != null)
            {
                bool fadeDone = false;
                m_ScreenFade.FadeOut(() => fadeDone = true, m_FadeDuration);
                yield return new WaitUntil(() => fadeDone);
            }

            // 2. Unload active interior scene if present
            if (m_CurrentLoadedSubScene == NorthBlockInteriorsSceneName || IsSceneLoaded(NorthBlockInteriorsSceneName))
            {
                yield return StartCoroutine(UnloadSceneRoutine(NorthBlockInteriorsSceneName));
            }
            if (m_CurrentLoadedSubScene == GatewayInteriorsSceneName || IsSceneLoaded(GatewayInteriorsSceneName))
            {
                yield return StartCoroutine(UnloadSceneRoutine(GatewayInteriorsSceneName));
            }

            // 3. Load Exterior Scene if not loaded
            if (!IsSceneLoaded(ExteriorSceneName))
            {
                yield return StartCoroutine(LoadSceneAdditiveRoutine(ExteriorSceneName));
            }
            else
            {
                m_CurrentLoadedSubScene = ExteriorSceneName;
                Scene sc = SceneManager.GetSceneByName(ExteriorSceneName);
                if (sc.isLoaded) SceneManager.SetActiveScene(sc);
            }

            // 4. Move Rig to requested exterior anchor
            Transform targetAnchor = FindExteriorAnchor(locationKey);
            if (targetAnchor != null)
            {
                MoveRigToAnchor(targetAnchor);
            }
            else
            {
                Debug.LogWarning($"[SceneTransitionManager] Exterior anchor for '{locationKey}' not found. Defaulting to (0,0,0).");
            }

            yield return new WaitForSeconds(m_BlackoutHold);

            onComplete?.Invoke();

            // 5. Fade In
            if (m_ScreenFade != null)
            {
                bool fadeDone = false;
                m_ScreenFade.FadeIn(() => fadeDone = true, m_FadeDuration);
                yield return new WaitUntil(() => fadeDone);
            }

            m_IsTransitioning = false;
        }

        // ====================================================================
        // INTERIOR TRANSITIONS
        // ====================================================================
        public void TransitionToInterior(string buildingKey, string roomKey, Action onComplete = null)
        {
            if (m_IsTransitioning) return;
            StartCoroutine(TransitionToInteriorRoutine(buildingKey, roomKey, onComplete));
        }

        private IEnumerator TransitionToInteriorRoutine(string buildingKey, string roomKey, Action onComplete)
        {
            m_IsTransitioning = true;

            // Determine target interior scene
            string targetInteriorScene = NorthBlockInteriorsSceneName;
            if (buildingKey == "gateway" || roomKey.StartsWith("gw-"))
            {
                targetInteriorScene = GatewayInteriorsSceneName;
            }

            // 1. Fade Out
            if (m_ScreenFade != null)
            {
                bool fadeDone = false;
                m_ScreenFade.FadeOut(() => fadeDone = true, m_FadeDuration);
                yield return new WaitUntil(() => fadeDone);
            }

            // 2. Unload Exterior Scene to free Quest 3S memory and prevent exterior void visibility
            if (IsSceneLoaded(ExteriorSceneName))
            {
                yield return StartCoroutine(UnloadSceneRoutine(ExteriorSceneName));
            }

            // 3. Unload alternate interior scene if loaded
            string alternateInterior = targetInteriorScene == NorthBlockInteriorsSceneName ? GatewayInteriorsSceneName : NorthBlockInteriorsSceneName;
            if (IsSceneLoaded(alternateInterior))
            {
                yield return StartCoroutine(UnloadSceneRoutine(alternateInterior));
            }

            // 4. Load Target Interior Scene
            if (!IsSceneLoaded(targetInteriorScene))
            {
                yield return StartCoroutine(LoadSceneAdditiveRoutine(targetInteriorScene));
            }
            else
            {
                m_CurrentLoadedSubScene = targetInteriorScene;
                Scene sc = SceneManager.GetSceneByName(targetInteriorScene);
                if (sc.isLoaded) SceneManager.SetActiveScene(sc);
            }

            // 5. Locate Room Pod & Position Rig
            Transform targetAnchor = FindRoomAnchor(roomKey);
            if (targetAnchor != null)
            {
                MoveRigToAnchor(targetAnchor);
            }
            else
            {
                Debug.LogWarning($"[SceneTransitionManager] Interior anchor for room '{roomKey}' not found.");
            }

            yield return new WaitForSeconds(m_BlackoutHold);

            onComplete?.Invoke();

            // 6. Fade In
            if (m_ScreenFade != null)
            {
                bool fadeDone = false;
                m_ScreenFade.FadeIn(() => fadeDone = true, m_FadeDuration);
                yield return new WaitUntil(() => fadeDone);
            }

            m_IsTransitioning = false;
        }

        // ====================================================================
        // RIG TELEPORTATION & POSITIONING
        // ====================================================================
        public void MoveRigToAnchor(Transform anchorTransform)
        {
            if (anchorTransform == null) return;
            FindRigReferences();

            if (m_XROrigin == null)
            {
                Debug.LogError("[SceneTransitionManager] Cannot move rig: XROrigin is null.");
                return;
            }

            // Disable CharacterController to prevent collision physics fighting position set
            bool wasCcEnabled = false;
            if (m_CharacterController != null)
            {
                wasCcEnabled = m_CharacterController.enabled;
                m_CharacterController.enabled = false;
            }

            Transform rigTransform = m_XROrigin.transform;

            // Align position and yaw rotation
            Vector3 targetPos = anchorTransform.position;
            Quaternion targetRot = Quaternion.Euler(0f, anchorTransform.eulerAngles.y, 0f);

            rigTransform.position = targetPos;
            rigTransform.rotation = targetRot;

            if (m_CharacterController != null && wasCcEnabled)
            {
                m_CharacterController.enabled = true;
            }

            m_CurrentAnchorTransform = anchorTransform;

            // Recenter UI if LazyFollow is attached
            LazyFollowUI lazyFollow = UnityEngine.Object.FindAnyObjectByType<LazyFollowUI>();
            if (lazyFollow != null)
            {
                lazyFollow.RecenterToCurrentGaze();
            }
        }

        public void RecenterToCurrentAnchor()
        {
            if (m_CurrentAnchorTransform != null)
            {
                MoveRigToAnchor(m_CurrentAnchorTransform);
            }
        }

        // ====================================================================
        // ANCHOR SEARCH HELPERS
        // ====================================================================
        private Transform FindExteriorAnchor(string locationKey)
        {
            string expectedAnchorName = "";
            switch (locationKey)
            {
                case "front-gate":
                    expectedAnchorName = "Anchor_FrontGate";
                    break;
                case "outside-north-block":
                    expectedAnchorName = "Anchor_NorthBlock_Plaza";
                    break;
                case "outside-gateway":
                    expectedAnchorName = "Anchor_Gateway_Plaza";
                    break;
                case "outside-workshop":
                    expectedAnchorName = "Anchor_Workshop_Plaza";
                    break;
                default:
                    expectedAnchorName = $"Anchor_{locationKey}";
                    break;
            }

            GameObject anchorObj = GameObject.Find(expectedAnchorName);
            if (anchorObj != null) return anchorObj.transform;

            // Fallback: search any GameObject starting with "Anchor_"
            var allAnchors = GameObject.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>(FindObjectsSortMode.None);
            foreach (var a in allAnchors)
            {
                if (a.name.Equals(expectedAnchorName, StringComparison.OrdinalIgnoreCase))
                {
                    return a.transform;
                }
            }

            if (allAnchors.Length > 0)
            {
                return allAnchors[0].transform;
            }

            return null;
        }

        private Transform FindRoomAnchor(string roomKey)
        {
            var podControllers = UnityEngine.Object.FindObjectsByType<RoomPodAnchorController>(FindObjectsSortMode.None);
            foreach (var pod in podControllers)
            {
                if (pod.PodKey.Equals(roomKey, StringComparison.OrdinalIgnoreCase))
                {
                    pod.NotifyPlayerEntered();
                    return pod.PrimarySpawnAnchor;
                }
            }

            // Fallback: search by GameObject name pattern
            string expectedPodName = "";
            switch (roomKey)
            {
                case "nb-classroom": expectedPodName = "RoomPod_SmartClassroom"; break;
                case "nb-lab": expectedPodName = "RoomPod_RoboticsLab"; break;
                case "nb-faculty": expectedPodName = "RoomPod_FacultyCabins"; break;
                case "gw-classroom": expectedPodName = "RoomPod_ExecutiveMBASeminar"; break;
                case "gw-lab": expectedPodName = "RoomPod_ACICStartupIncubator"; break;
            }

            if (!string.IsNullOrEmpty(expectedPodName))
            {
                GameObject podObj = GameObject.Find(expectedPodName);
                if (podObj != null)
                {
                    RoomPodAnchorController controller = podObj.GetComponent<RoomPodAnchorController>();
                    if (controller != null)
                    {
                        controller.NotifyPlayerEntered();
                        return controller.PrimarySpawnAnchor;
                    }
                    return podObj.transform;
                }
            }

            return null;
        }

        // ====================================================================
        // ASYNC SCENE LOAD / UNLOAD HELPERS
        // ====================================================================
        private IEnumerator LoadSceneAdditiveRoutine(string sceneName)
        {
            if (IsSceneLoaded(sceneName))
            {
                m_CurrentLoadedSubScene = sceneName;
                yield break;
            }

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[SceneTransitionManager] Scene '{sceneName}' failed to load. Is it in Build Settings?");
                yield break;
            }

            while (!op.isDone)
            {
                yield return null;
            }

            m_CurrentLoadedSubScene = sceneName;
            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            if (loadedScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedScene);
            }
        }

        private IEnumerator UnloadSceneRoutine(string sceneName)
        {
            if (!IsSceneLoaded(sceneName)) yield break;

            AsyncOperation op = SceneManager.UnloadSceneAsync(sceneName);
            if (op != null)
            {
                while (!op.isDone)
                {
                    yield return null;
                }
            }

            // Clean up unused assets from memory for Quest 3S optimization
            yield return Resources.UnloadUnusedAssets();

            if (m_CurrentLoadedSubScene == sceneName)
            {
                m_CurrentLoadedSubScene = "";
            }
        }

        private bool IsSceneLoaded(string sceneName)
        {
            Scene sc = SceneManager.GetSceneByName(sceneName);
            return sc.isLoaded;
        }
    }
}
