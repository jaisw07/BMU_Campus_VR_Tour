using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using BMU.VRTour;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace BMU.VRTour.Editor
{
    public static class BMUTourSceneBuilder
    {
        private static readonly Color ColorNavy = new Color(0f, 0.17f, 0.29f);       // #002B49
        private static readonly Color ColorPrimary = new Color(0.01f, 0.52f, 0.78f); // #0284C7 Natural Sky Blue
        private static readonly Color ColorHighlightGlow = new Color(0.01f, 0.58f, 0.88f, 0.75f); // Natural Sky Blue Glow
        private static readonly Color ColorAccent = new Color(0f, 0.40f, 0.70f);     // #0066B3
        private static readonly Color ColorRed = new Color(0.89f, 0.11f, 0.14f);     // #E31B23
        private static readonly Color ColorGlassBg = new Color(1f, 1f, 1f, 0.48f);   // 48% Alpha (Figma parity)
        private static readonly Color ColorGlassCard = new Color(1f, 1f, 1f, 0.56f); // 56% Alpha internal cards
        private static readonly Color ColorGlassHUD = new Color(1f, 1f, 1f, 0.65f);  // 65% Alpha HUDs
        private static readonly Color ColorGlassRim = new Color(1f, 1f, 1f, 0.85f);  // 85% Alpha outer rim
        private static readonly Color ColorGlassInactive = new Color(1f, 1f, 1f, 0.32f); // 32% Alpha inactive
        private static readonly Color ColorWhite = Color.white;
        private static readonly Color ColorPitchBlack = Color.black; // #000000 Pitch Black

        // Glass tints multiply the glass sprite's own alpha (sprite fill ~48-65%, rim ~85%).
        // Kept well below 1 so the campus shows through; legibility comes from the text halo below.
        private static readonly Color GlassCard = new Color(1f, 1f, 1f, 0.72f);     // main stage cards
        private static readonly Color GlassOrnament = new Color(1f, 1f, 1f, 0.80f); // small floating controls
        private static readonly Color GlassInner = new Color(1f, 1f, 1f, 0.50f);    // glass stacked on glass

        // Body copy size: 20px = 20mm at 0.001 scale, ~11 dmm at the 1.8m follow distance (17px was too small to read in-headset).
        private const float BodyTextSize = 20f;

        // Ornaments float toward the viewer (canvas units: 1 = 1mm at 0.001 scale) for real depth.
        private const float OrnamentDepth = -28f;

        // Cylinder radius (canvas units) used to curve card rows around the viewer at 1.8m.
        private const float CardArcRadius = 1800f;

        private static readonly System.Collections.Generic.Dictionary<TMP_FontAsset, Material> s_HaloMaterials =
            new System.Collections.Generic.Dictionary<TMP_FontAsset, Material>();

        // Typography Assets
        private static TMP_FontAsset s_FontSpaceGroteskBold;
        private static TMP_FontAsset s_FontSpaceGroteskMedium;
        private static TMP_FontAsset s_FontPlusJakartaSansBold;
        private static TMP_FontAsset s_FontPlusJakartaSansSemiBold;
        private static TMP_FontAsset s_FontPlusJakartaSansRegular;
        private static TMP_FontAsset s_FontPlusJakartaSansItalic;

        // UI Vector Sprites
        private static Sprite s_SpriteLogo;
        private static Sprite s_SpriteArrowLeft;
        private static Sprite s_SpriteArrowRight;
        private static Sprite s_SpriteSpeakerOn;
        private static Sprite s_SpriteSpeakerOff;
        private static Sprite s_SpriteRestartTour;
        private static Sprite s_SpriteMinimizeDash;
        private static Sprite s_SpriteMoveDirectional;
        private static Sprite s_SpriteHome;

        // Frosted Glass Sprites (85% rim + calibrated fill)
        private static Sprite s_SpriteGlassPanel26;
        private static Sprite s_SpriteGlassPanel24;
        private static Sprite s_SpriteGlassPanel20;
        private static Sprite s_SpriteGlassPanel16;
        private static Sprite s_SpriteGlassPanel12;
        private static Sprite s_SpriteGlassPanel8;
        private static Sprite s_SpriteGlassPill;
        private static Sprite s_SpriteGlassCircle;
        private static Sprite s_SpriteHudChipPill;

        // Rounded Rect Solid Masks
        private static Sprite s_SpriteRoundedRect26;
        private static Sprite s_SpriteRoundedRect24;
        private static Sprite s_SpriteRoundedRect20;
        private static Sprite s_SpriteRoundedRect16;
        private static Sprite s_SpriteRoundedRect12;
        private static Sprite s_SpriteRoundedRect8;
        private static Sprite s_SpriteRoundedRectPill;
        private static Sprite s_SpriteRoundedRectCircle;

        // Backlight Aura Sprites
        private static Sprite s_SpriteBacklightGlow;
        private static Sprite s_SpriteBacklightGlowCircle;

        // Campus Building & Room Photos
        private static Sprite s_PhotoNorthBlock;
        private static Sprite s_PhotoGateway;
        private static Sprite s_PhotoWorkshop;
        private static Sprite s_PhotoNBClassroom;
        private static Sprite s_PhotoNBLab;
        private static Sprite s_PhotoNBFaculty;
        private static Sprite s_PhotoGWClassroom;
        private static Sprite s_PhotoGWLab;

        // Slide 1 Photos
        private static Sprite s_PhotoSlide1_Gate;
        private static Sprite s_PhotoSlide1_Quad;
        private static Sprite s_PhotoSlide1_Skyline;

        [MenuItem("BMU VR Tour/Build Complete VR Tour Scene (All 4 Scenes)", false, 1)]
        public static void BuildCompleteScene()
        {
            BuildAllScenes();
        }

        [MenuItem("BMU VR Tour/Build Multi-Scene Architecture", false, 2)]
        public static void BuildAllScenes()
        {
            Debug.Log("[BMU VR Tour] Starting automated multi-scene construction...");
            LoadAssets();
            XRSetupInitializer.EnsureXRConfigured();

            BuildMasterTourCoreScene();
            BuildExteriorScene();
            BuildNorthBlockInteriorsScene();
            BuildGatewayInteriorsScene();
            UpdateBuildSettings();

            EditorSceneManager.OpenScene("Assets/Scenes/Master_Tour_Core.unity", OpenSceneMode.Single);
            Debug.Log("[BMU VR Tour] Multi-Scene VR architecture completely built and configured!");
        }

        private static void CleanUpOldObjects()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();
            for (int i = rootObjects.Length - 1; i >= 0; i--)
            {
                var obj = rootObjects[i];
                if (obj == null) continue;
                if (obj.name.StartsWith("XR Origin") ||
                    obj.name == "[Campus_Floor]" ||
                    obj.name == "[Campus_Environment]" ||
                    obj.name == "[VR_Spatial_UI_Root]" ||
                    obj.name == "[ScreenFade_Root]" ||
                    obj.name == "EventSystem" ||
                    obj.name == "Main Camera")
                {
                    if (PrefabUtility.IsPartOfAnyPrefab(obj))
                    {
                        PrefabUtility.UnpackPrefabInstance(obj, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    }
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
        }

        private static GameObject SetupXRRig(out Camera mainCam)
        {
            // Try to load XR Origin Hands prefab or Starter Assets XR Origin (3.3.2 or dynamic fallback)
            string handsPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";
            string starterPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(handsPrefabPath);
            if (rigPrefab == null)
            {
                rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(starterPrefabPath);
            }
            if (rigPrefab == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab \"XR Origin Hands (XR Rig)\"");
                if (guids != null && guids.Length > 0)
                {
                    rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (rigPrefab == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab \"XR Origin (XR Rig)\"");
                if (guids != null && guids.Length > 0)
                {
                    rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            GameObject rigInstance;
            if (rigPrefab != null)
            {
                rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
                rigInstance.name = "XR Origin (VR Headset & Hands)";
                rigInstance.transform.position = Vector3.zero;
                rigInstance.transform.rotation = Quaternion.identity;
                Debug.Log($"[BMU VR Tour] Successfully instantiated XR Rig from '{AssetDatabase.GetAssetPath(rigPrefab)}'.");
            }
            else
            {
                Debug.LogError("[BMU VR Tour] CRITICAL: Could not locate XR Origin Prefab in project!");
                rigInstance = new GameObject("XR Origin (Fallback)");
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                camObj.transform.SetParent(rigInstance.transform);
                camObj.transform.localPosition = new Vector3(0, 1.6f, 0);
                Camera cam = camObj.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f;
                camObj.AddComponent<AudioListener>();
            }

            mainCam = rigInstance.GetComponentInChildren<Camera>(true);
            if (mainCam != null)
            {
                mainCam.nearClipPlane = 0.1f;
                mainCam.farClipPlane = 300f;
                var xrOriginComp = rigInstance.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
                if (xrOriginComp != null)
                {
                    xrOriginComp.Camera = mainCam;
                }
            }

            // 1. Configure all CurveVisualControllers for full visible light pointers
            var curveControllers = rigInstance.GetComponentsInChildren<CurveVisualController>(true);
            foreach (var cvc in curveControllers)
            {
                cvc.extendLineToEmptyHit = true;
                cvc.restingVisualLineLength = 10f;
                cvc.maxVisualCurveDistance = 25f;
                cvc.lineDynamicsMode = LineDynamicsMode.Traditional;

                var lr = cvc.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.enabled = true;
                }
            }

            // 1b. Attach VRPointerReticle (small blue circle) to NearFarInteractors
            var nearFarInteractors = rigInstance.GetComponentsInChildren<NearFarInteractor>(true);
            foreach (var nfi in nearFarInteractors)
            {
                if (nfi.GetComponent<VRPointerReticle>() == null)
                {
                    nfi.gameObject.AddComponent<VRPointerReticle>();
                }
            }

            // 2. Prevent falling into void: Disable Continuous Move & Gravity
            var moveProviders = rigInstance.GetComponentsInChildren<ContinuousMoveProvider>(true);
            foreach (var mp in moveProviders)
            {
#pragma warning disable CS0618
                mp.useGravity = false;
#pragma warning restore CS0618
                mp.enableFly = false;
                mp.moveSpeed = 0f;
                mp.enabled = false;
            }

            Transform locomotionTransform = rigInstance.transform.Find("Locomotion");
            if (locomotionTransform != null)
            {
                Transform moveTransform = locomotionTransform.Find("Move");
                if (moveTransform != null)
                {
                    moveTransform.gameObject.SetActive(false);
                }
                Transform grabMoveTransform = locomotionTransform.Find("Grab Move");
                if (grabMoveTransform != null)
                {
                    grabMoveTransform.gameObject.SetActive(false);
                }
                Transform climbTransform = locomotionTransform.Find("Climb");
                if (climbTransform != null)
                {
                    climbTransform.gameObject.SetActive(false);
                }
            }

            // 3. Ensure EventSystem with ONLY XRUIInputModule properly configured
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            XRUIInputModule uiInputModule = eventSystemObj.AddComponent<XRUIInputModule>();

            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/XRI Default Input Actions.inputactions");
            if (inputActions == null)
            {
                string[] actionGuids = AssetDatabase.FindAssets("t:InputActionAsset \"XRI Default Input Actions\"");
                if (actionGuids != null && actionGuids.Length > 0)
                {
                    inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(actionGuids[0]));
                }
            }
            if (inputActions != null)
            {
                var uiMap = inputActions.FindActionMap("XRI UI");
                if (uiMap != null)
                {
                    uiInputModule.pointAction = InputActionReference.Create(uiMap.FindAction("Point"));
                    uiInputModule.leftClickAction = InputActionReference.Create(uiMap.FindAction("Click"));
                    uiInputModule.middleClickAction = InputActionReference.Create(uiMap.FindAction("MiddleClick"));
                    uiInputModule.rightClickAction = InputActionReference.Create(uiMap.FindAction("RightClick"));
                    uiInputModule.scrollWheelAction = InputActionReference.Create(uiMap.FindAction("ScrollWheel"));
                    uiInputModule.navigateAction = InputActionReference.Create(uiMap.FindAction("Navigate"));
                    uiInputModule.submitAction = InputActionReference.Create(uiMap.FindAction("Submit"));
                    uiInputModule.cancelAction = InputActionReference.Create(uiMap.FindAction("Cancel"));
                }
            }
            uiInputModule.enableXRInput = true;
            uiInputModule.enableMouseInput = true;
            uiInputModule.enableTouchInput = true;
            uiInputModule.enableGamepadInput = true;
            uiInputModule.enableJoystickInput = true;
            uiInputModule.enableBuiltinActionsAsFallback = true;
            uiInputModule.uiCamera = mainCam;

            return rigInstance;
        }

        private static void SetupGroundFloor()
        {
            GameObject floor = new GameObject("[Campus_Floor]");
            floor.transform.position = Vector3.zero;
            floor.layer = 31; // Teleport layer

            BoxCollider col = floor.AddComponent<BoxCollider>();
            col.size = new Vector3(100f, 0.2f, 100f);
            col.center = new Vector3(0f, -0.1f, 0f); // Top surface at Y = 0

            TeleportationArea teleArea = floor.AddComponent<TeleportationArea>();
            teleArea.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
            teleArea.interactionLayers = 1 << 31;
        }

        private static void SetupEnvironment()
        {
            GameObject envObj = new GameObject("[Campus_Environment]");
            envObj.transform.position = Vector3.zero;
            envObj.AddComponent<CampusEnvironmentManager>();
        }

        private static void SetupScreenFade()
        {
            GameObject fadeObj = new GameObject("[ScreenFade_Root]");
            fadeObj.transform.position = Vector3.zero;

            Canvas canvas = fadeObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            CanvasGroup group = fadeObj.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            GameObject imgObj = new GameObject("FadeImage");
            imgObj.transform.SetParent(fadeObj.transform, false);
            Image img = imgObj.AddComponent<Image>();
            img.color = Color.black;

            RectTransform rt = img.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            fadeObj.AddComponent<ScreenFade>();
        }

        private static void SetupSpatialUI(Camera mainCam)
        {
            // Root Object with Lazy Follow
            GameObject uiRoot = new GameObject("[VR_Spatial_UI_Root]");
            uiRoot.transform.position = new Vector3(0f, 1.4f, 1.8f);
            LazyFollowUI lazyFollow = uiRoot.AddComponent<LazyFollowUI>();

            // Master Tour Manager on Root
            CampusTourManager tourManager = uiRoot.AddComponent<CampusTourManager>();

            // World Space Canvas
            GameObject canvasObj = new GameObject("MainSpatialCanvas");
            canvasObj.transform.SetParent(uiRoot.transform, false);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (mainCam != null)
            {
                canvas.worldCamera = mainCam;
            }
            else if (Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }
            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1400f, 850f);
            canvasRect.localScale = Vector3.one * 0.001f; // 1.4m x 0.85m in VR space

            canvasObj.AddComponent<CanvasScaler>();
            var raycaster = canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();
            raycaster.checkFor2DOcclusion = false;
            raycaster.checkFor3DOcclusion = false;

            CanvasGroup mainCanvasGroup = canvasObj.AddComponent<CanvasGroup>();

            BoxCollider canvasCol = canvasObj.AddComponent<BoxCollider>();
            canvasCol.isTrigger = true;
            canvasCol.size = new Vector3(1400f, 850f, 20f);
            canvasCol.center = new Vector3(0f, 0f, 10f);

            // Container for smooth scaling
            GameObject deckContainer = new GameObject("DeckContainer");
            deckContainer.transform.SetParent(canvasObj.transform, false);
            RectTransform deckRect = deckContainer.AddComponent<RectTransform>();
            deckRect.anchorMin = Vector2.zero;
            deckRect.anchorMax = Vector2.one;
            deckRect.offsetMin = Vector2.zero;
            deckRect.offsetMax = Vector2.zero;

            // No full-canvas pane and no full-width bars. The stage card is the hero; brand/location,
            // window controls and narration are small "ornaments" that float a few cm in front of it
            // at its corners and below it, so the UI reads as spatial objects rather than a web page.
            s_HaloMaterials.Clear();

            // ================================================================
            // TITLE ORNAMENT (top-left): logo + university name + live location
            // ================================================================
            GameObject titleOrnament = CreatePanel("TitleOrnament", deckContainer.transform, GlassOrnament, 320f, 54f, s_SpriteGlassPill);
            RectTransform titleOrnRt = titleOrnament.GetComponent<RectTransform>();
            titleOrnRt.anchorMin = new Vector2(0.5f, 1f);
            titleOrnRt.anchorMax = new Vector2(0.5f, 1f);
            titleOrnRt.pivot = new Vector2(0f, 1f);
            titleOrnRt.anchoredPosition3D = new Vector3(-560f, -22f, OrnamentDepth);
            titleOrnament.GetComponent<Image>().raycastTarget = false;

            if (s_SpriteLogo != null)
            {
                GameObject logoObj = new GameObject("LogoImage");
                logoObj.transform.SetParent(titleOrnament.transform, false);
                Image logoImg = logoObj.AddComponent<Image>();
                logoImg.sprite = s_SpriteLogo;
                logoImg.preserveAspect = true;
                logoImg.raycastTarget = false;
                RectTransform logoRt = logoObj.GetComponent<RectTransform>();
                logoRt.anchorMin = new Vector2(0f, 0.5f);
                logoRt.anchorMax = new Vector2(0f, 0.5f);
                logoRt.pivot = new Vector2(0f, 0.5f);
                logoRt.anchoredPosition = new Vector2(14f, 0f);
                logoRt.sizeDelta = new Vector2(34f, 34f);
            }

            TextMeshProUGUI brandTitle = CreateText("BrandTitle", titleOrnament.transform, "BML MUNJAL UNIVERSITY", 15f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            brandTitle.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform btRt = brandTitle.GetComponent<RectTransform>();
            btRt.anchorMin = new Vector2(0f, 0.5f);
            btRt.anchorMax = new Vector2(1f, 0.5f);
            btRt.pivot = new Vector2(0f, 0f);
            btRt.anchoredPosition = new Vector2(56f, 1f);   // left inset 56, right inset 14
            btRt.sizeDelta = new Vector2(-70f, 22f);

            // Live location line (wired to CampusTourManager.m_HeaderLocationText)
            TextMeshProUGUI locText = CreateText("LocationText", titleOrnament.transform, "FRONT GATE • WELCOME & INTRO", 12.5f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            locText.textWrappingMode = TextWrappingModes.NoWrap;
            locText.overflowMode = TextOverflowModes.Truncate;
            locText.enableAutoSizing = true;
            locText.fontSizeMin = 9.5f;
            locText.fontSizeMax = 12.5f;
            RectTransform ltRt = locText.GetComponent<RectTransform>();
            ltRt.anchorMin = new Vector2(0f, 0.5f);
            ltRt.anchorMax = new Vector2(1f, 0.5f);
            ltRt.pivot = new Vector2(0f, 1f);
            ltRt.anchoredPosition = new Vector2(56f, -1f);
            ltRt.sizeDelta = new Vector2(-70f, 20f);

            // ================================================================
            // CONTROLS ORNAMENT (top-right): [Sound] [Recenter] [Restart] [Move] [Minimize]
            // ================================================================
            GameObject controlsOrnament = CreatePanel("ControlsOrnament", deckContainer.transform, GlassOrnament, 330f, 54f, s_SpriteGlassPill);
            RectTransform ctrlOrnRt = controlsOrnament.GetComponent<RectTransform>();
            ctrlOrnRt.anchorMin = new Vector2(0.5f, 1f);
            ctrlOrnRt.anchorMax = new Vector2(0.5f, 1f);
            ctrlOrnRt.pivot = new Vector2(1f, 1f);
            ctrlOrnRt.anchoredPosition3D = new Vector3(560f, -22f, OrnamentDepth);
            controlsOrnament.GetComponent<Image>().raycastTarget = false;

            // 1. Minimize Icon Button (- Icon)
            Button btnHeaderMinimize = CreateIconButton("BtnHeaderMinimize", controlsOrnament.transform, s_SpriteMinimizeDash, Color.white, ColorNavy, 38f, s_SpriteGlassCircle, "Minimize to Dock");
            RectTransform minRt = btnHeaderMinimize.GetComponent<RectTransform>();
            minRt.anchorMin = new Vector2(1f, 0.5f);
            minRt.anchorMax = new Vector2(1f, 0.5f);
            minRt.pivot = new Vector2(1f, 0.5f);
            minRt.anchoredPosition = new Vector2(-8f, 0f);
            ConfigureHeaderButtonFeedback(btnHeaderMinimize, true);

            // 2. Drag to Move Icon Button (+ Directional Move Icon)
            Button btnHeaderDragMove = CreateIconButton("BtnHeaderDragMove", controlsOrnament.transform, s_SpriteMoveDirectional, Color.white, ColorNavy, 38f, s_SpriteGlassCircle, "Drag to Move");
            RectTransform dragRt = btnHeaderDragMove.GetComponent<RectTransform>();
            dragRt.anchorMin = new Vector2(1f, 0.5f);
            dragRt.anchorMax = new Vector2(1f, 0.5f);
            dragRt.pivot = new Vector2(1f, 0.5f);
            dragRt.anchoredPosition = new Vector2(-54f, 0f);
            ConfigureHeaderButtonFeedback(btnHeaderDragMove, true);

            var dragHandle = btnHeaderDragMove.gameObject.AddComponent<VRUIDraggableHandle>();
            SetPrivateField(dragHandle, "m_UIRoot", uiRoot.transform);
            SetPrivateField(dragHandle, "m_LazyFollowUI", lazyFollow);
            SetPrivateField(dragHandle, "m_HandleImage", btnHeaderDragMove.GetComponent<Image>());

            // 3. Restart Tour Icon Button (Circular Loop Arrow)
            Button btnRestart = CreateIconButton("BtnHeaderRestart", controlsOrnament.transform, s_SpriteRestartTour, Color.white, ColorAccent, 38f, s_SpriteGlassCircle, "Restart Tour");
            RectTransform restartRt = btnRestart.GetComponent<RectTransform>();
            restartRt.anchorMin = new Vector2(1f, 0.5f);
            restartRt.anchorMax = new Vector2(1f, 0.5f);
            restartRt.pivot = new Vector2(1f, 0.5f);
            restartRt.anchoredPosition = new Vector2(-100f, 0f);
            ConfigureHeaderButtonFeedback(btnRestart, true);

            // 4. Recenter UI Pill Button
            Button btnRecenter = CreateButton("BtnRecenterUI", controlsOrnament.transform, "Recenter", ColorNavy, 120f, 36f, 15f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
            RectTransform recRt = btnRecenter.GetComponent<RectTransform>();
            recRt.anchorMin = new Vector2(1f, 0.5f);
            recRt.anchorMax = new Vector2(1f, 0.5f);
            recRt.pivot = new Vector2(1f, 0.5f);
            recRt.anchoredPosition = new Vector2(-146f, 0f);
            ConfigureHeaderButtonFeedback(btnRecenter, false);

            // 5. Sound Toggle Icon Button (Circular Frosted Glass with Speaker Icons & Tooltip)
            Button btnAudioToggle = CreateButton("BtnAudioToggle", controlsOrnament.transform, "", Color.white, 38f, 38f, 14f, s_SpriteGlassCircle);
            RectTransform batRt = btnAudioToggle.GetComponent<RectTransform>();
            batRt.anchorMin = new Vector2(1f, 0.5f);
            batRt.anchorMax = new Vector2(1f, 0.5f);
            batRt.pivot = new Vector2(1f, 0.5f);
            batRt.anchoredPosition = new Vector2(-274f, 0f);
            ConfigureHeaderButtonFeedback(btnAudioToggle, true);

            GameObject speakerOn = new GameObject("SpeakerIconOn");
            speakerOn.transform.SetParent(btnAudioToggle.transform, false);
            Image spkOnImg = speakerOn.AddComponent<Image>();
            spkOnImg.sprite = s_SpriteSpeakerOn;
            spkOnImg.color = ColorNavy;
            spkOnImg.preserveAspect = true;
            spkOnImg.raycastTarget = false;
            RectTransform sonRt = speakerOn.GetComponent<RectTransform>();
            sonRt.anchorMin = new Vector2(0.5f, 0.5f);
            sonRt.anchorMax = new Vector2(0.5f, 0.5f);
            sonRt.pivot = new Vector2(0.5f, 0.5f);
            sonRt.sizeDelta = new Vector2(22f, 22f);

            GameObject speakerOff = new GameObject("SpeakerIconOff");
            speakerOff.transform.SetParent(btnAudioToggle.transform, false);
            Image spkOffImg = speakerOff.AddComponent<Image>();
            spkOffImg.sprite = s_SpriteSpeakerOff;
            spkOffImg.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            spkOffImg.preserveAspect = true;
            spkOffImg.raycastTarget = false;
            speakerOff.SetActive(false);
            RectTransform soffRt = speakerOff.GetComponent<RectTransform>();
            soffRt.anchorMin = new Vector2(0.5f, 0.5f);
            soffRt.anchorMax = new Vector2(0.5f, 0.5f);
            soffRt.pivot = new Vector2(0.5f, 0.5f);
            soffRt.sizeDelta = new Vector2(22f, 22f);

            VRHeaderTooltip audioTooltip = btnAudioToggle.gameObject.AddComponent<VRHeaderTooltip>();
            audioTooltip.TooltipText = "Mute Audio";

            // ================================================================
            // STAGE 1: INTRO 3D CAROUSEL PANEL
            // ================================================================
            GameObject introPanel = new GameObject("Stage1_IntroCarouselPanel");
            introPanel.transform.SetParent(deckContainer.transform, false);
            RectTransform introRt = introPanel.AddComponent<RectTransform>();
            introRt.anchorMin = Vector2.zero;
            introRt.anchorMax = Vector2.one;
            introRt.offsetMin = new Vector2(20f, 24f);  // rebalanced vertical margins
            introRt.offsetMax = new Vector2(-20f, -96f); // clear of the top ornaments

            // Carousel Card (High Fidelity 1120 x 540 with 50% alpha acrylic glass)
            GameObject cardObj = CreatePanel("CarouselSlideCard", introPanel.transform, GlassCard, 1120f, 540f, s_SpriteGlassPanel26);
            RectTransform cardRt = cardObj.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = Vector2.zero;

            // --- LEFT CONTAINER (58% width, ~650px) ---
            GameObject leftContainer = new GameObject("LeftContentContainer");
            leftContainer.transform.SetParent(cardObj.transform, false);
            RectTransform leftRt = leftContainer.AddComponent<RectTransform>();
            leftRt.anchorMin = new Vector2(0f, 0f);
            leftRt.anchorMax = new Vector2(0.58f, 1f);
            leftRt.offsetMin = Vector2.zero;
            leftRt.offsetMax = Vector2.zero;

            // Slide Badge Pill (tinted in theme sky blue)
            GameObject badgePill = CreatePanel("SlideBadgePill", leftContainer.transform, new Color(0.01f, 0.52f, 0.78f, 0.12f), 220f, 28f, s_SpriteGlassPill);
            RectTransform badgeRt = badgePill.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(0f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.anchoredPosition = new Vector2(50f, -32f);

            TextMeshProUGUI slideBadge = CreateText("SlideBadge", badgePill.transform, "HERO GROUP LEGACY", 13.5f, ColorPrimary, FontStyles.Bold, 0f, s_FontPlusJakartaSansBold);
            slideBadge.alignment = TextAlignmentOptions.Center;
            SetFullStretch(slideBadge.GetComponent<RectTransform>());
            MakePillFitText(badgePill, slideBadge, 14);

            // Title / description / stats stack: a VerticalLayoutGroup sizes each block to its
            // wrapped text, so a 2-line title pushes the description down instead of overlapping it.
            GameObject slideTextStack = CreateTextStack("SlideTextStack", leftContainer.transform, 14f);
            RectTransform stackRt = slideTextStack.GetComponent<RectTransform>();
            stackRt.anchorMin = Vector2.zero;
            stackRt.anchorMax = Vector2.one;
            stackRt.offsetMin = new Vector2(50f, 88f);   // stays above slide index + skip button
            stackRt.offsetMax = new Vector2(-20f, -70f); // starts below the badge pill

            TextMeshProUGUI slideTitle = CreateText("SlideTitle", slideTextStack.transform, "Welcome to BML Munjal University", 38f, ColorPitchBlack, FontStyles.Bold);
            TextMeshProUGUI slideDesc = CreateText("SlideDesc", slideTextStack.transform, "Description text goes here...", BodyTextSize, ColorPitchBlack, FontStyles.Bold, 14f, s_FontPlusJakartaSansBold);
            TextMeshProUGUI slideStats = CreateText("SlideStats", slideTextStack.transform, "• Founded by Hero Group\n• Mentored by Imperial College London\n• Experiential 3-2-1 Curriculum", 20f, ColorPitchBlack, FontStyles.Bold, 18f, s_FontPlusJakartaSansBold);

            // Slide Index (Bottom-Left inside LeftContainer, strictly bounded)
            TextMeshProUGUI slideIndex = CreateText("SlideIndex", leftContainer.transform, "SLIDE 1 OF 5", 18f, ColorPitchBlack, FontStyles.Bold);
            slideIndex.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform idxRt = slideIndex.GetComponent<RectTransform>();
            idxRt.anchorMin = new Vector2(0f, 0f);
            idxRt.anchorMax = new Vector2(0f, 0f);
            idxRt.pivot = new Vector2(0f, 0f);
            idxRt.anchoredPosition = new Vector2(50f, 32f);
            idxRt.sizeDelta = new Vector2(180f, 40f);

            // Skip Button (Bottom-Right inside LeftContainer - BMU Primary Blue with bold white text)
            Button btnSkip = CreateButton("BtnSkipIntro", leftContainer.transform, "Skip Intro", ColorPrimary, 180f, 48f, 17f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
            RectTransform skipRt = btnSkip.GetComponent<RectTransform>();
            skipRt.anchorMin = new Vector2(1f, 0f);
            skipRt.anchorMax = new Vector2(1f, 0f);
            skipRt.pivot = new Vector2(1f, 0f);
            skipRt.anchoredPosition = new Vector2(-20f, 28f);

            // --- RIGHT CONTAINER (42% width, ~470px) ---
            GameObject rightContainer = new GameObject("RightCollageContainer");
            rightContainer.transform.SetParent(cardObj.transform, false);
            RectTransform rightRt = rightContainer.AddComponent<RectTransform>();
            rightRt.anchorMin = new Vector2(0.58f, 0f);
            rightRt.anchorMax = new Vector2(1f, 1f);
            rightRt.offsetMin = Vector2.zero;
            rightRt.offsetMax = Vector2.zero;

            // 3 Staggered / Tilted Polaroid Image Cards with Unsplash Photos (Figma Parity: -3.5°, +2.8°, -1.8°)
            CreatePolaroidPlaceholder("Polaroid_A", rightContainer.transform, new Vector2(250f, 180f), new Vector2(-65f, 90f), -3.5f, "BMU FRONT ENTRANCE", s_PhotoSlide1_Gate, out Image imgA, out TextMeshProUGUI capA);
            CreatePolaroidPlaceholder("Polaroid_C", rightContainer.transform, new Vector2(260f, 180f), new Vector2(-55f, -95f), -1.8f, "ACADEMIC CAMPUS SKYLINE", s_PhotoSlide1_Skyline, out Image imgC, out TextMeshProUGUI capC);
            CreatePolaroidPlaceholder("Polaroid_B", rightContainer.transform, new Vector2(285f, 200f), new Vector2(65f, 5f), 2.8f, "STUDENT COLLABORATIVE QUAD", s_PhotoSlide1_Quad, out Image imgB, out TextMeshProUGUI capB);

            // Stagger the polaroids in depth so the collage has real parallax in the headset
            SetDepth(imgA.transform.parent, -10f);
            SetDepth(imgC.transform.parent, -6f);
            SetDepth(imgB.transform.parent, -18f);

            Image[] polaroidImages = new Image[] { imgA, imgB, imgC };
            TextMeshProUGUI[] polaroidCaptions = new TextMeshProUGUI[] { capA, capB, capC };

            // Left / Right Circular Chevrons (58x58px frosted buttons with SVG vector arrows)
            Button btnPrev = CreateButton("BtnPrevSlide", introPanel.transform, "", Color.white, 58f, 58f, 16f, s_SpriteGlassCircle);
            RectTransform prevRt = btnPrev.GetComponent<RectTransform>();
            prevRt.anchorMin = new Vector2(0f, 0.5f);
            prevRt.anchorMax = new Vector2(0f, 0.5f);
            prevRt.pivot = new Vector2(0.5f, 0.5f);
            prevRt.anchoredPosition3D = new Vector3(50f, 0f, OrnamentDepth);
            if (s_SpriteArrowLeft != null)
            {
                GameObject icon = new GameObject("Icon");
                icon.transform.SetParent(btnPrev.transform, false);
                Image iconImg = icon.AddComponent<Image>();
                iconImg.sprite = s_SpriteArrowLeft;
                iconImg.color = ColorPitchBlack;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                RectTransform iRt = icon.GetComponent<RectTransform>();
                iRt.sizeDelta = new Vector2(24f, 24f);
            }

            Button btnNext = CreateButton("BtnNextSlide", introPanel.transform, "", Color.white, 58f, 58f, 16f, s_SpriteGlassCircle);
            RectTransform nextRt = btnNext.GetComponent<RectTransform>();
            nextRt.anchorMin = new Vector2(1f, 0.5f);
            nextRt.anchorMax = new Vector2(1f, 0.5f);
            nextRt.pivot = new Vector2(0.5f, 0.5f);
            nextRt.anchoredPosition3D = new Vector3(-50f, 0f, OrnamentDepth);
            if (s_SpriteArrowRight != null)
            {
                GameObject icon = new GameObject("Icon");
                icon.transform.SetParent(btnNext.transform, false);
                Image iconImg = icon.AddComponent<Image>();
                iconImg.sprite = s_SpriteArrowRight;
                iconImg.color = ColorPitchBlack;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                RectTransform iRt = icon.GetComponent<RectTransform>();
                iRt.sizeDelta = new Vector2(24f, 24f);
            }

            // ================================================================
            // STAGE 2: CAMPUS EXPLORATION HUB PANEL
            // ================================================================
            GameObject hubPanel = new GameObject("Stage2_CampusHubPanel");
            hubPanel.transform.SetParent(deckContainer.transform, false);
            RectTransform hubRt = hubPanel.AddComponent<RectTransform>();
            hubRt.anchorMin = Vector2.zero;
            hubRt.anchorMax = Vector2.one;
            hubRt.offsetMin = new Vector2(20f, 24f);  // rebalanced vertical margins
            hubRt.offsetMax = new Vector2(-20f, -96f); // clear of the top ornaments

            // Hub Heading (own glass card, since there is no background pane behind it)
            GameObject hubHeadingCard = CreatePanel("HubHeadingCard", hubPanel.transform, GlassCard, 1060f, 100f, s_SpriteGlassPanel16);
            RectTransform hubHeadingRt = hubHeadingCard.GetComponent<RectTransform>();
            hubHeadingRt.anchorMin = new Vector2(0.5f, 1f);
            hubHeadingRt.anchorMax = new Vector2(0.5f, 1f);
            hubHeadingRt.pivot = new Vector2(0.5f, 1f);
            hubHeadingRt.anchoredPosition = new Vector2(0f, -4f);
            hubHeadingCard.GetComponent<Image>().raycastTarget = false;

            TextMeshProUGUI hubTitle = CreateText("HubTitle", hubHeadingCard.transform, "Select a Destination to Begin Tour", 38f, ColorPitchBlack, FontStyles.Bold);
            hubTitle.alignment = TextAlignmentOptions.Center;
            RectTransform hubTitleRt = hubTitle.GetComponent<RectTransform>();
            hubTitleRt.anchorMin = new Vector2(0.5f, 1f);
            hubTitleRt.anchorMax = new Vector2(0.5f, 1f);
            hubTitleRt.pivot = new Vector2(0.5f, 1f);
            hubTitleRt.anchoredPosition = new Vector2(0f, -10f);
            hubTitleRt.sizeDelta = new Vector2(1000f, 45f);

            TextMeshProUGUI hubSubtitle = CreateText("HubSubtitle", hubHeadingCard.transform, "Choose one of the 3 primary university facilities to teleport directly to its exterior plaza.", 20f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontPlusJakartaSansBold);
            hubSubtitle.alignment = TextAlignmentOptions.Center;
            RectTransform hubSubRt = hubSubtitle.GetComponent<RectTransform>();
            hubSubRt.anchorMin = new Vector2(0.5f, 1f);
            hubSubRt.anchorMax = new Vector2(0.5f, 1f);
            hubSubRt.pivot = new Vector2(0.5f, 1f);
            hubSubRt.anchoredPosition = new Vector2(0f, -58f);
            hubSubRt.sizeDelta = new Vector2(1000f, 30f);

            // 3 Destination Cards (Interactive full cards with BMU Category photo banners & colored badges)
            GameObject cardsContainer = new GameObject("CardsContainer");
            cardsContainer.transform.SetParent(hubPanel.transform, false);
            RectTransform cardsRt = cardsContainer.AddComponent<RectTransform>();
            cardsRt.anchorMin = new Vector2(0.5f, 0.44f);
            cardsRt.anchorMax = new Vector2(0.5f, 0.44f);
            cardsRt.sizeDelta = new Vector2(1280f, 440f);
            ConfigureCardRow(cardsContainer, 28f);

            // Card 1: North Block
            GameObject card1 = CreateBuildingCard("Card_NorthBlock", cardsContainer.transform, "ACADEMIC HUB", "North Block Building", "School of Engineering & Tech, Management, AI labs, and faculty suites.", ColorNavy, s_PhotoNorthBlock, out Button btnNorthBlock);
            // Card 2: Gateway
            GameObject card2 = CreateBuildingCard("Card_Gateway", cardsContainer.transform, "ACADEMIC & ADMIN", "Gateway Building", "University Reception, Program Office, ACIC Incubator, and Sports Gymnasium.", ColorPrimary, s_PhotoGateway, out Button btnGateway);
            // Card 3: Mechanical Workshop
            GameObject card3 = CreateBuildingCard("Card_Workshop", cardsContainer.transform, "INDUSTRY & LABS", "Mechanical Workshop", "Siemens & Hero MotoCorp hangar, CNCs, 3D printing, and BAJA race bays.", ColorRed, s_PhotoWorkshop, out Button btnWorkshop);

            // ================================================================
            // STAGE 3: LOCATION SINGLE INFO PANEL
            // ================================================================
            GameObject locPanel = new GameObject("Stage3_LocationInfoPanel");
            locPanel.transform.SetParent(deckContainer.transform, false);
            RectTransform locPanelRt = locPanel.AddComponent<RectTransform>();
            locPanelRt.anchorMin = Vector2.zero;
            locPanelRt.anchorMax = Vector2.one;
            locPanelRt.offsetMin = new Vector2(20f, 24f);  // rebalanced vertical margins
            locPanelRt.offsetMax = new Vector2(-20f, -96f); // clear of the top ornaments

            // Glass Card for Location Info
            GameObject locCard = CreatePanel("LocInfoCard", locPanel.transform, GlassCard, 1120f, 540f, s_SpriteGlassPanel26);
            RectTransform lcRt = locCard.GetComponent<RectTransform>();
            lcRt.anchorMin = new Vector2(0.5f, 0.5f);
            lcRt.anchorMax = new Vector2(0.5f, 0.5f);
            lcRt.pivot = new Vector2(0.5f, 0.5f);
            lcRt.anchoredPosition = Vector2.zero;

            // Zone tag pill
            GameObject zonePill = CreatePanel("ZoneTagPill", locCard.transform, new Color(0f, 0.17f, 0.29f, 0.08f), 300f, 28f, s_SpriteGlassPill);
            RectTransform lzRt = zonePill.GetComponent<RectTransform>();
            lzRt.anchorMin = new Vector2(0f, 1f);
            lzRt.anchorMax = new Vector2(0f, 1f);
            lzRt.pivot = new Vector2(0f, 1f);
            lzRt.anchoredPosition = new Vector2(50f, -25f);

            TextMeshProUGUI locZone = CreateText("LocZoneTag", zonePill.transform, "ACADEMIC COMPLEX (EXTERIOR)", 13.5f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            locZone.alignment = TextAlignmentOptions.Center;
            SetFullStretch(locZone.GetComponent<RectTransform>());
            MakePillFitText(zonePill, locZone, 14);

            // Heading / summary / features stack in the 520px left column. Stacking by preferred
            // height stops long headings (e.g. "Mechanical Workshop & Vehicle Hangar") or 6-line
            // summaries from running into the block below.
            GameObject locTextStack = CreateTextStack("LocTextStack", locCard.transform, 14f);
            RectTransform locStackRt = locTextStack.GetComponent<RectTransform>();
            locStackRt.anchorMin = new Vector2(0f, 0f);
            locStackRt.anchorMax = new Vector2(0f, 1f);
            locStackRt.pivot = new Vector2(0f, 1f);
            locStackRt.offsetMin = new Vector2(50f, 100f);   // stays above the "Go Inside" button
            locStackRt.offsetMax = new Vector2(630f, -62f);  // 580px wide, below the zone pill

            TextMeshProUGUI locHeading = CreateText("LocHeading", locTextStack.transform, "North Block Academic Building", 38f, ColorPitchBlack, FontStyles.Bold);
            TextMeshProUGUI locSummary = CreateText("LocSummary", locTextStack.transform, "Location summary paragraph text...", BodyTextSize, ColorPitchBlack, FontStyles.Bold, 14f, s_FontPlusJakartaSansBold);
            TextMeshProUGUI locFeatures = CreateText("LocFeaturesList", locTextStack.transform, "• Feature bullet point 1\n• Feature bullet point 2", 20f, ColorPitchBlack, FontStyles.Bold, 18f, s_FontPlusJakartaSansBold);

            // Right Photo Card with Unsplash Photo (Width 460px with 40px clear gutter)
            GameObject photoCard = CreatePanel("PhotoCard", locCard.transform, GlassInner, 400f, 400f, s_SpriteGlassPanel16);
            RectTransform pcRt = photoCard.GetComponent<RectTransform>();
            pcRt.anchorMin = new Vector2(1f, 0.5f);
            pcRt.anchorMax = new Vector2(1f, 0.5f);
            pcRt.pivot = new Vector2(1f, 0.5f);
            pcRt.anchoredPosition3D = new Vector3(-50f, 25f, -12f); // photo lifts slightly off the card

            GameObject locPhotoArea = CreatePanel("PhotoArea", photoCard.transform, Color.white, 370f, 285f, s_SpriteRoundedRect8);
            RectTransform paRt = locPhotoArea.GetComponent<RectTransform>();
            paRt.anchorMin = new Vector2(0.5f, 1f);
            paRt.anchorMax = new Vector2(0.5f, 1f);
            paRt.pivot = new Vector2(0.5f, 1f);
            paRt.anchoredPosition = new Vector2(0f, -15f);
            Image locPhotoImg = locPhotoArea.GetComponent<Image>();
            Sprite gatePhoto = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_front_gate.jpg");
            if (gatePhoto != null)
            {
                locPhotoImg.sprite = gatePhoto;
                locPhotoImg.color = Color.white;
            }

            TextMeshProUGUI photoCaption = CreateText("PhotoCaption", photoCard.transform, "Front Gate & Main Academic Boulevard", 16f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            photoCaption.alignment = TextAlignmentOptions.Center;
            RectTransform capRt = photoCaption.GetComponent<RectTransform>();
            capRt.anchorMin = new Vector2(0f, 0f);
            capRt.anchorMax = new Vector2(1f, 0f);
            capRt.pivot = new Vector2(0.5f, 0f);
            capRt.anchoredPosition = new Vector2(0f, 20f);
            capRt.sizeDelta = new Vector2(-30f, 50f);

            // Footer Action Buttons
            // "ENTER" Primary Pill Button (center aligned text, matching Skip Intro styling & horizontally centered across entire panel)
            Button btnExploreInterior = CreateButton("BtnExploreInterior", locCard.transform, "ENTER", ColorPrimary, 180f, 48f, 17f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
            RectTransform biRt = btnExploreInterior.GetComponent<RectTransform>();
            biRt.anchorMin = new Vector2(0.5f, 0f);
            biRt.anchorMax = new Vector2(0.5f, 0f);
            biRt.pivot = new Vector2(0.5f, 0f);
            biRt.anchoredPosition = new Vector2(0f, 32f);

            // Right: Campus Hub Return Home Icon Button (48x48 frosted glass with home vector icon)
            Button btnCampHub = CreateIconButton("BtnCampusHubReturn", locCard.transform, s_SpriteHome, ColorNavy, Color.white, 48f, s_SpriteGlassCircle, "Campus Hub");
            RectTransform bhRt = btnCampHub.GetComponent<RectTransform>();
            bhRt.anchorMin = new Vector2(1f, 0f);
            bhRt.anchorMax = new Vector2(1f, 0f);
            bhRt.pivot = new Vector2(1f, 0f);
            bhRt.anchoredPosition = new Vector2(-50f, 35f);

            // ================================================================
            // STAGE 4: INTERNAL BUILDING HUB PANEL
            // ================================================================
            GameObject internalPanel = new GameObject("Stage4_InternalBuildingHubPanel");
            internalPanel.transform.SetParent(deckContainer.transform, false);
            RectTransform inRt = internalPanel.AddComponent<RectTransform>();
            inRt.anchorMin = Vector2.zero;
            inRt.anchorMax = Vector2.one;
            inRt.offsetMin = new Vector2(20f, 24f);  // rebalanced vertical margins
            inRt.offsetMax = new Vector2(-20f, -96f); // clear of the top ornaments

            // Heading card: [← Back to Plaza]  tag + title  [Campus Hub →] on one glass card
            GameObject inHeadingCard = CreatePanel("InternalHeadingCard", internalPanel.transform, GlassCard, 1200f, 106f, s_SpriteGlassPanel16);
            RectTransform inHeadingRt = inHeadingCard.GetComponent<RectTransform>();
            inHeadingRt.anchorMin = new Vector2(0.5f, 1f);
            inHeadingRt.anchorMax = new Vector2(0.5f, 1f);
            inHeadingRt.pivot = new Vector2(0.5f, 1f);
            inHeadingRt.anchoredPosition = new Vector2(0f, -4f);
            inHeadingCard.GetComponent<Image>().raycastTarget = false;

            Button btnBackPlaza = CreateButton("BtnBackToPlaza", inHeadingCard.transform, "Back to Exterior Plaza", ColorAccent, 240f, 42f, 16f, s_SpriteGlassPill, s_FontPlusJakartaSansBold);
            RectTransform bbpRt = btnBackPlaza.GetComponent<RectTransform>();
            bbpRt.anchorMin = new Vector2(0f, 0.5f);
            bbpRt.anchorMax = new Vector2(0f, 0.5f);
            bbpRt.pivot = new Vector2(0f, 0.5f);
            bbpRt.anchoredPosition = new Vector2(24f, 0f);

            Button btnBackHub = CreateButton("BtnBackToCampusHub", inHeadingCard.transform, "Campus Hub", ColorNavy, 160f, 42f, 16f, s_SpriteGlassPill, s_FontPlusJakartaSansBold);
            RectTransform bbhRt = btnBackHub.GetComponent<RectTransform>();
            bbhRt.anchorMin = new Vector2(1f, 0.5f);
            bbhRt.anchorMax = new Vector2(1f, 0.5f);
            bbhRt.pivot = new Vector2(1f, 0.5f);
            bbhRt.anchoredPosition = new Vector2(-24f, 0f);

            // Shorter phrase "Select a Facility" vertically centered in heading card (top-center location pill removed)
            TextMeshProUGUI inTitle = CreateText("InternalHubTitle", inHeadingCard.transform, "Select a Facility", 38f, ColorPitchBlack, FontStyles.Bold);
            inTitle.alignment = TextAlignmentOptions.Center;
            RectTransform inTitRt = inTitle.GetComponent<RectTransform>();
            inTitRt.anchorMin = new Vector2(0.5f, 0.5f);
            inTitRt.anchorMax = new Vector2(0.5f, 0.5f);
            inTitRt.pivot = new Vector2(0.5f, 0.5f);
            inTitRt.anchoredPosition = Vector2.zero;
            inTitRt.sizeDelta = new Vector2(600f, 45f);

            // Container for dynamic room cards
            GameObject roomCardsContainer = new GameObject("RoomCardsContainer");
            roomCardsContainer.transform.SetParent(internalPanel.transform, false);
            RectTransform rccRt = roomCardsContainer.AddComponent<RectTransform>();
            rccRt.anchorMin = new Vector2(0.5f, 0.40f);
            rccRt.anchorMax = new Vector2(0.5f, 0.40f);
            rccRt.sizeDelta = new Vector2(1280f, 440f);
            ConfigureCardRow(roomCardsContainer, 28f);

            // Template room card (Card is the button, unified with ColorPrimary)
            GameObject templateCard = CreateBuildingCard("RoomCardTemplate", roomCardsContainer.transform, "FACILITY", "Room Name", "Room description...", ColorNavy, s_PhotoNBClassroom, out Button btnTemplate);
            templateCard.SetActive(false);

            // Tour Audio Narrator (headless audio manager wired to header sound button)
            TourAudioNarrator narrator = uiRoot.AddComponent<TourAudioNarrator>();
            SetPrivateField(narrator, "m_CaptionText", null);
            SetPrivateField(narrator, "m_ActiveCaptionColor", ColorPitchBlack);
            SetPrivateField(narrator, "m_AudioToggleButton", btnAudioToggle);
            SetPrivateField(narrator, "m_ToggleButtonText", null);
            SetPrivateField(narrator, "m_ToggleButtonImage", btnAudioToggle.GetComponent<Image>());
            SetPrivateField(narrator, "m_SpeakerIconOn", speakerOn);
            SetPrivateField(narrator, "m_SpeakerIconOff", speakerOff);
            SetPrivateField(narrator, "m_WaveformBars", null);

            // ================================================================
            // MINIMIZED DOCK ORB
            // ================================================================
            // Parked top-right of the view (~17° right, ~9° above eye level at 1.8m). The UI root keeps
            // lazy-following while minimized, so the orb stays in the top-right periphery.
            GameObject dockOrbRoot = new GameObject("DockOrbRoot");
            dockOrbRoot.transform.SetParent(uiRoot.transform, false);
            dockOrbRoot.transform.localPosition = new Vector3(0.55f, 0.42f, 0f);

            // Visual container: MinimizedDockOrb scales this from 0 -> 1, so everything lives under it
            GameObject orbVisual = new GameObject("DockOrbVisual");
            orbVisual.transform.SetParent(dockOrbRoot.transform, false);

            // Glossy 3D bead behind the badge (its rim shows around the logo)
            GameObject orbSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orbSphere.name = "OrbBead";
            orbSphere.transform.SetParent(orbVisual.transform, false);
            orbSphere.transform.localScale = Vector3.one * 0.09f;
            Collider beadCol = orbSphere.GetComponent<Collider>();
            if (beadCol != null) UnityEngine.Object.DestroyImmediate(beadCol);

            MeshRenderer orbRend = orbSphere.GetComponent<MeshRenderer>();
            orbRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orbRend.receiveShadows = false;
            Material orbMat = GetOrCreateMaterial("Assets/Materials/M_DockOrb_Gloss.mat", ColorPrimary, 0.9f);
            if (orbMat.HasProperty("_Metallic")) orbMat.SetFloat("_Metallic", 0.15f);
            orbRend.sharedMaterial = orbMat;

            // Badge canvas just in front of the bead, facing the viewer (-Z is toward the user)
            GameObject orbCanvas = new GameObject("OrbCanvas");
            orbCanvas.transform.SetParent(orbVisual.transform, false);
            orbCanvas.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            Canvas oc = orbCanvas.AddComponent<Canvas>();
            oc.renderMode = RenderMode.WorldSpace;
            if (mainCam != null) oc.worldCamera = mainCam;
            orbCanvas.AddComponent<TrackedDeviceGraphicRaycaster>();
            RectTransform ocRt = orbCanvas.GetComponent<RectTransform>();
            ocRt.sizeDelta = new Vector2(140f, 140f);
            ocRt.localScale = Vector3.one * 0.00075f; // 100px badge = 75mm

            // Soft pulsing halo (MinimizedDockOrb drives its scale)
            GameObject orbHalo = CreatePanel("OrbHalo", orbCanvas.transform, new Color(0f, 0.53f, 1f, 0.30f), 128f, 128f, s_SpriteGlassCircle);
            orbHalo.GetComponent<Image>().raycastTarget = false;

            Button btnRestore = CreateButton("BtnRestoreOrb", orbCanvas.transform, "", Color.white, 100f, 100f, 16f, s_SpriteGlassCircle);
            if (s_SpriteLogo != null)
            {
                GameObject orbLogo = new GameObject("OrbLogo");
                orbLogo.transform.SetParent(btnRestore.transform, false);
                Image orbImg = orbLogo.AddComponent<Image>();
                orbImg.sprite = s_SpriteLogo;
                orbImg.preserveAspect = true;
                orbImg.raycastTarget = false;
                RectTransform olRt = orbLogo.GetComponent<RectTransform>();
                olRt.sizeDelta = new Vector2(64f, 64f);
            }
            VRHeaderTooltip orbTip = btnRestore.gameObject.AddComponent<VRHeaderTooltip>();
            orbTip.TooltipText = "Open Tour Panel";

            MinimizedDockOrb dockOrb = uiRoot.AddComponent<MinimizedDockOrb>();
            SetPrivateField(dockOrb, "m_MainCanvasContainer", deckContainer);
            SetPrivateField(dockOrb, "m_DockOrbVisual", orbVisual);
            SetPrivateField(dockOrb, "m_MainCanvasGroup", mainCanvasGroup);
            SetPrivateField(dockOrb, "m_PulseRing", orbHalo.transform);
            SetPrivateField(dockOrb, "m_PulseScaleAmount", 0.12f);

            // ================================================================
            // WIRE CAMPUS TOUR MANAGER REFERENCES
            // ================================================================
            SetPrivateField(tourManager, "m_HeaderLocationText", locText);
            SetPrivateField(tourManager, "m_BtnResetTour", btnRestart);
            SetPrivateField(tourManager, "m_BtnRecenterUI", btnRecenter);
            SetPrivateField(tourManager, "m_LazyFollowUI", lazyFollow);

            SetPrivateField(tourManager, "m_IntroCarouselPanel", introPanel);
            SetPrivateField(tourManager, "m_CampusHubPanel", hubPanel);
            SetPrivateField(tourManager, "m_LocationInfoPanel", locPanel);
            SetPrivateField(tourManager, "m_InternalBuildingHubPanel", internalPanel);

            // Intro elements
            SetPrivateField(tourManager, "m_SlideBadgeText", slideBadge);
            SetPrivateField(tourManager, "m_SlideTitleText", slideTitle);
            SetPrivateField(tourManager, "m_SlideDescText", slideDesc);
            SetPrivateField(tourManager, "m_SlideStatsText", slideStats);
            SetPrivateField(tourManager, "m_SlideIndexIndicator", slideIndex);
            SetPrivateField(tourManager, "m_BtnPrevSlide", btnPrev);
            SetPrivateField(tourManager, "m_BtnNextSlide", btnNext);
            SetPrivateField(tourManager, "m_BtnSkipIntro", btnSkip);
            SetPrivateField(tourManager, "m_PolaroidImages", polaroidImages);
            SetPrivateField(tourManager, "m_PolaroidCaptions", polaroidCaptions);
            PopulatePhotoSets(tourManager);

            // Hub buttons
            SetPrivateField(tourManager, "m_BtnSelectNorthBlock", btnNorthBlock);
            SetPrivateField(tourManager, "m_BtnSelectGateway", btnGateway);
            SetPrivateField(tourManager, "m_BtnSelectWorkshop", btnWorkshop);

            // Loc Info elements
            SetPrivateField(tourManager, "m_LocZoneTag", locZone);
            SetPrivateField(tourManager, "m_LocHeading", locHeading);
            SetPrivateField(tourManager, "m_LocSummary", locSummary);
            SetPrivateField(tourManager, "m_LocFeaturesList", locFeatures);
            SetPrivateField(tourManager, "m_LocPhotoCaption", photoCaption);
            locPhotoImg = photoCard.transform.Find("PhotoArea")?.GetComponent<Image>();
            SetPrivateField(tourManager, "m_LocPhotoCardImage", locPhotoImg);
            SetPrivateField(tourManager, "m_BtnExploreInterior", btnExploreInterior);
            SetPrivateField(tourManager, "m_BtnNextLocation", null);
            SetPrivateField(tourManager, "m_BtnCampusHubReturn", btnCampHub);
            SetPrivateField(tourManager, "m_BtnMinimize", btnHeaderMinimize);

            // Internal Hub elements
            SetPrivateField(tourManager, "m_InternalHubTag", null);
            SetPrivateField(tourManager, "m_InternalHubTitle", inTitle);
            SetPrivateField(tourManager, "m_BtnBackToPlaza", btnBackPlaza);
            SetPrivateField(tourManager, "m_BtnBackToCampusHub", btnBackHub);
            SetPrivateField(tourManager, "m_RoomCardsContainer", roomCardsContainer.transform);
            SetPrivateField(tourManager, "m_RoomCardTemplate", templateCard);

            // Dock Orb
            SetPrivateField(tourManager, "m_DockOrb", dockOrb);
            SetPrivateField(tourManager, "m_BtnRestoreOrb", btnRestore);

            // Set initial stage visibility in edit mode
            introPanel.SetActive(true);
            hubPanel.SetActive(false);
            locPanel.SetActive(false);
            internalPanel.SetActive(false);
        }

        // ====================================================================
        // UI FACTORY HELPERS
        // ====================================================================
        private static void LoadAssets()
        {
            s_FontSpaceGroteskBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/SpaceGrotesk-Bold SDF.asset");
            s_FontSpaceGroteskMedium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/SpaceGrotesk-Medium SDF.asset");
            s_FontPlusJakartaSansBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/PlusJakartaSans-Bold SDF.asset");
            s_FontPlusJakartaSansSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/PlusJakartaSans-SemiBold SDF.asset");
            s_FontPlusJakartaSansRegular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/PlusJakartaSans-Regular SDF.asset");
            s_FontPlusJakartaSansItalic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/PlusJakartaSans-Italic SDF.asset");

            s_SpriteLogo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/bmu_logo.png");
            s_SpriteArrowLeft = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_arrow_left.png");
            s_SpriteArrowRight = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_arrow_right.png");
            s_SpriteSpeakerOn = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_speaker_on.png");
            s_SpriteSpeakerOff = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_speaker_off.png");
            s_SpriteRestartTour = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_restart_tour.png");
            s_SpriteMinimizeDash = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_minimize_dash.png");
            s_SpriteMoveDirectional = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_move_directional.png");
            s_SpriteHome = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_home.png");
            s_SpriteGlassPanel26 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_panel_26.png");
            s_SpriteGlassPanel24 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_panel_24.png");
            s_SpriteGlassPanel20 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_panel_20.png");
            s_SpriteGlassPanel16 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_panel_16.png");
            s_SpriteGlassPanel12 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_panel_12.png");
            s_SpriteGlassPanel8 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_panel_8.png");
            s_SpriteGlassPill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_pill.png");
            s_SpriteGlassCircle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_glass_circle.png");
            s_SpriteHudChipPill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_hud_chip_pill.png");

            s_SpriteRoundedRect26 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_26.png");
            s_SpriteRoundedRect24 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_24.png");
            s_SpriteRoundedRect20 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_20.png");
            s_SpriteRoundedRect16 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_16.png");
            s_SpriteRoundedRect12 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_12.png");
            s_SpriteRoundedRect8 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_8.png");
            s_SpriteRoundedRectPill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_pill.png");
            s_SpriteRoundedRectCircle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_rounded_rect_circle.png");

            s_PhotoSlide1_Gate = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_1_photo_1_gate.jpg");
            s_PhotoSlide1_Quad = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_1_photo_2_quad.jpg");
            s_PhotoSlide1_Skyline = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_1_photo_3_skyline.jpg");

            // Diffused Backlight Aura Sprites
            s_SpriteBacklightGlow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_backlight_glow.png");
            s_SpriteBacklightGlowCircle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/ui_backlight_glow_circle.png");

            // Campus Building & Room Photos
            s_PhotoNorthBlock = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_outside_north_block.jpg");
            s_PhotoGateway = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_outside_gateway.jpg");
            s_PhotoWorkshop = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_outside_workshop.jpg");
            s_PhotoNBClassroom = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_nb_classroom.jpg");
            s_PhotoNBLab = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_nb_lab.jpg");
            s_PhotoNBFaculty = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_nb_faculty.jpg");
            s_PhotoGWClassroom = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_gw_classroom.jpg");
            s_PhotoGWLab = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_gw_lab.jpg");
        }

        private static void PopulatePhotoSets(CampusTourManager tourManager)
        {
            var sets = new CampusTourManager.SlidePhotoData[5];

            // Slide 1
            sets[0] = new CampusTourManager.SlidePhotoData
            {
                photos = new Sprite[] {
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_1_photo_1_gate.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_1_photo_2_quad.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_1_photo_3_skyline.jpg")
                },
                labels = new string[] { "BMU Front Entrance", "Student Collaborative Quad", "Academic Campus Skyline" }
            };

            // Slide 2
            sets[1] = new CampusTourManager.SlidePhotoData
            {
                photos = new Sprite[] {
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_2_photo_1_convocation.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_2_photo_2_industry.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_2_photo_3_research.jpg")
                },
                labels = new string[] { "Annual Convocation Hall", "Industry Collaboration", "Research Excellence Awards" }
            };

            // Slide 3
            sets[2] = new CampusTourManager.SlidePhotoData
            {
                photos = new Sprite[] {
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_3_photo_1_robotics.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_3_photo_2_management.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_3_photo_3_mootcourt.jpg")
                },
                labels = new string[] { "Advanced Robotics Testing", "Management Case Discussion", "Moot Courtroom Sessions" }
            };

            // Slide 4
            sets[3] = new CampusTourManager.SlidePhotoData
            {
                photos = new Sprite[] {
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_4_photo_1_library.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_4_photo_2_iot.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_4_photo_3_auditorium.jpg")
                },
                labels = new string[] { "Central Library & Atrium", "IoT & Electronics Studio", "Smart Tiered Auditorium" }
            };

            // Slide 5
            sets[4] = new CampusTourManager.SlidePhotoData
            {
                photos = new Sprite[] {
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_5_photo_1_rocknight.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_5_photo_2_sports.jpg"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/slide_5_photo_3_hackathon.jpg")
                },
                labels = new string[] { "67th Milestone Rock Night", "Inter-University Sports Arena", "Student Technical Hackathon" }
            };

            SetPrivateField(tourManager, "m_SlidePhotoSets", sets);

            // Location Photos
            Sprite[] locSprites = new Sprite[] {
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_front_gate.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_outside_north_block.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_nb_classroom.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_nb_lab.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_nb_faculty.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_outside_gateway.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_gw_classroom.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_gw_lab.jpg"),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Photos/zone_outside_workshop.jpg")
            };
            SetPrivateField(tourManager, "m_LocationPhotoSprites", locSprites);
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color, float width, float height, Sprite sprite = null)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            Image img = obj.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                if (sprite.border != Vector4.zero)
                {
                    img.type = Image.Type.Sliced;
                }
                else
                {
                    img.type = Image.Type.Simple;
                }
            }
            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);
            return obj;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string content, float fontSize, Color color, FontStyles style, float lineSpacing = 0f, TMP_FontAsset font = null)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            else if ((style & FontStyles.Bold) != 0 && s_FontPlusJakartaSansBold != null)
            {
                tmp.font = s_FontPlusJakartaSansBold;
            }
            else if (s_FontPlusJakartaSansSemiBold != null)
            {
                tmp.font = s_FontPlusJakartaSansSemiBold;
            }
            else if (s_FontPlusJakartaSansRegular != null)
            {
                tmp.font = s_FontPlusJakartaSansRegular;
            }
            else if (TMP_Settings.defaultFontAsset != null)
            {
                tmp.font = TMP_Settings.defaultFontAsset;
            }
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            if (lineSpacing > 0.01f)
            {
                tmp.lineSpacing = lineSpacing;
            }
            tmp.raycastTarget = false;

            // Dark text gets a soft light halo so it stays readable over the campus now that the
            // glass is more transparent (white text sits on solid buttons and needs none).
            if (color.grayscale < 0.5f)
            {
                Material halo = GetHaloMaterial(tmp.font);
                if (halo != null) tmp.fontSharedMaterial = halo;
            }
            return tmp;
        }

        /// <summary>
        /// One shared TMP material per font with a soft white underlay ("halo"). Saved under
        /// Assets/Materials/UI so scenes reference an asset rather than a per-object instance.
        /// </summary>
        private static Material GetHaloMaterial(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            if (s_HaloMaterials.TryGetValue(font, out Material cached) && cached != null) return cached;

            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/UI")) AssetDatabase.CreateFolder("Assets/Materials", "UI");

            string path = $"Assets/Materials/UI/TMP_Halo_{font.name.Replace(' ', '_')}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(font.material);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.CopyPropertiesFromMaterial(font.material); // keep atlas/shader in sync with the font
            }

            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(1f, 1f, 1f, 0.7f));
            mat.SetFloat("_UnderlayOffsetX", 0f);
            mat.SetFloat("_UnderlayOffsetY", 0f);
            mat.SetFloat("_UnderlayDilate", 0.12f);
            mat.SetFloat("_UnderlaySoftness", 0.25f);
            EditorUtility.SetDirty(mat);

            s_HaloMaterials[font] = mat;
            return mat;
        }

        /// <summary>
        /// Vertical stack that sizes each text child to its wrapped height, so blocks never overlap.
        /// </summary>
        private static GameObject CreateTextStack(string name, Transform parent, float spacing)
        {
            GameObject stack = new GameObject(name);
            stack.transform.SetParent(parent, false);
            stack.AddComponent<RectTransform>();
            VerticalLayoutGroup vlg = stack.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return stack;
        }

        /// <summary>
        /// Makes a pill grow horizontally to fit its single-line label (plus padding) instead of
        /// letting long labels spill past its edges.
        /// </summary>
        private static void MakePillFitText(GameObject pill, TextMeshProUGUI label, int padX)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            HorizontalLayoutGroup hlg = pill.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(padX, padX, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            ContentSizeFitter fitter = pill.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>
        /// Row of fixed-size cards, centred, curved onto a cylinder around the viewer.
        /// </summary>
        private static void ConfigureCardRow(GameObject container, float spacing)
        {
            HorizontalLayoutGroup row = container.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            UICardArc arc = container.AddComponent<UICardArc>();
            SetPrivateField(arc, "m_Radius", CardArcRadius);
        }

        private static void SetDepth(Transform t, float z)
        {
            if (t is RectTransform rt)
            {
                Vector3 p = rt.anchoredPosition3D;
                p.z = z;
                rt.anchoredPosition3D = p;
            }
        }

        private static void ConfigureHeaderButtonFeedback(Button btn, bool isCircle)
        {
            if (btn == null) return;
            var fb = btn.GetComponent<VRButtonFeedback>();
            if (fb == null) fb = btn.gameObject.AddComponent<VRButtonFeedback>();

            fb.HoverZOffset = -6f; // -6mm subtle hover float for mini header controls
            fb.HoverScale = 1.03f;
            fb.GlowPadding = 16f;
            fb.HighlightColor = ColorHighlightGlow;
            Sprite glow = isCircle ? s_SpriteBacklightGlowCircle : s_SpriteBacklightGlow;
            fb.CustomGlowSprite = glow;
            fb.EnsureBacklight(glow);
            fb.InitializeRestingPosition();
        }

        private static Button CreateButton(string name, Transform parent, string label, Color bgColor, float width, float height, float fontSize = 16f, Sprite sprite = null, TMP_FontAsset font = null)
        {
            if (sprite == null)
            {
                sprite = s_SpriteRoundedRect12;
            }

            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            Image img = obj.AddComponent<Image>();
            img.color = bgColor;
            if (sprite != null)
            {
                img.sprite = sprite;
                if (sprite.border != Vector4.zero)
                {
                    img.type = Image.Type.Sliced;
                }
                else
                {
                    img.type = Image.Type.Simple;
                }
            }
            Button btn = obj.AddComponent<Button>();

            ColorBlock cb = btn.colors;
            cb.normalColor = bgColor;
            cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.35f);
            cb.pressedColor = Color.Lerp(bgColor, Color.black, 0.25f);
            cb.selectedColor = cb.highlightedColor;
            btn.colors = cb;

            var feedback = obj.AddComponent<VRButtonFeedback>();
            feedback.HoverZOffset = -10f; // -10mm float
            feedback.HoverScale = 1.02f;  // 1.02x scale punch
            feedback.HighlightColor = ColorHighlightGlow;
            feedback.GlowPadding = 18f;
            Sprite glow = (sprite == s_SpriteGlassCircle || sprite == s_SpriteRoundedRectCircle) ? s_SpriteBacklightGlowCircle : s_SpriteBacklightGlow;
            feedback.CustomGlowSprite = glow;
            feedback.EnsureBacklight(glow);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);

            if (!string.IsNullOrEmpty(label))
            {
                GameObject textObj = new GameObject("Text");
                textObj.transform.SetParent(obj.transform, false);
                TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
                if (font != null)
                {
                    tmp.font = font;
                }
                else if (s_FontPlusJakartaSansBold != null)
                {
                    tmp.font = s_FontPlusJakartaSansBold;
                }
                else if (TMP_Settings.defaultFontAsset != null)
                {
                    tmp.font = TMP_Settings.defaultFontAsset;
                }
                tmp.text = label;
                tmp.fontSize = fontSize;
                tmp.color = Color.white;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
                SetFullStretch(textObj.GetComponent<RectTransform>());
            }

            feedback.InitializeRestingPosition();

            return btn;
        }

        private static Button CreateIconButton(string name, Transform parent, Sprite iconSprite, Color bgColor, Color iconColor, float size = 40f, Sprite bgSprite = null, string tooltipText = "")
        {
            if (bgSprite == null)
            {
                bgSprite = s_SpriteGlassCircle != null ? s_SpriteGlassCircle : s_SpriteRoundedRectCircle;
            }

            Button btn = CreateButton(name, parent, "", bgColor, size, size, 14f, bgSprite);

            if (iconSprite != null)
            {
                GameObject icon = new GameObject("Icon");
                icon.transform.SetParent(btn.transform, false);
                Image iconImg = icon.AddComponent<Image>();
                iconImg.sprite = iconSprite;
                iconImg.color = iconColor;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                RectTransform iRt = icon.GetComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0.5f, 0.5f);
                iRt.anchorMax = new Vector2(0.5f, 0.5f);
                iRt.pivot = new Vector2(0.5f, 0.5f);
                iRt.sizeDelta = new Vector2(size * 0.55f, size * 0.55f);
            }

            if (!string.IsNullOrEmpty(tooltipText))
            {
                VRHeaderTooltip tt = btn.gameObject.AddComponent<VRHeaderTooltip>();
                tt.TooltipText = tooltipText;
            }

            var fb = btn.GetComponent<VRButtonFeedback>();
            if (fb != null) fb.InitializeRestingPosition();

            return btn;
        }

        private static GameObject CreateBuildingCard(string name, Transform parent, string badge, string title, string desc, Color themeColor, out Button cardBtn)
        {
            return CreateBuildingCard(name, parent, badge, title, desc, themeColor, null, out cardBtn);
        }

        private static GameObject CreateBuildingCard(string name, Transform parent, string badge, string title, string desc, Color badgeColor, Sprite photoSprite, out Button cardBtn)
        {
            GameObject card = CreatePanel(name, parent, GlassCard, 380f, 430f, s_SpriteGlassPanel20);

            // Card itself is the interactive Button
            cardBtn = card.AddComponent<Button>();
            ColorBlock cb = cardBtn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            cb.selectedColor = Color.white;
            cardBtn.colors = cb;

            var feedback = card.AddComponent<VRButtonFeedback>();
            feedback.HoverZOffset = -10f; // -10mm float
            feedback.HoverScale = 1.02f;  // 1.02x scale punch
            feedback.HighlightColor = ColorHighlightGlow;
            feedback.GlowPadding = 24f;
            feedback.CustomGlowSprite = s_SpriteBacklightGlow;
            feedback.EnsureBacklight(s_SpriteBacklightGlow);

            // 1. Photo Container (380 x 180) at the top of the card
            GameObject photoContainer = new GameObject("PhotoContainer");
            photoContainer.transform.SetParent(card.transform, false);
            RectTransform pcRt = photoContainer.AddComponent<RectTransform>();
            pcRt.anchorMin = new Vector2(0f, 1f);
            pcRt.anchorMax = new Vector2(1f, 1f);
            pcRt.pivot = new Vector2(0.5f, 1f);
            pcRt.anchoredPosition = new Vector2(0f, 0f);
            pcRt.sizeDelta = new Vector2(0f, 180f);

            // Mask with rounded top corners matching the card
            Image maskImg = photoContainer.AddComponent<Image>();
            maskImg.sprite = s_SpriteRoundedRect20;
            maskImg.type = Image.Type.Sliced;
            maskImg.color = Color.white;
            maskImg.raycastTarget = false;
            Mask mask = photoContainer.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            // Photo Image inside mask
            GameObject photoObj = new GameObject("CardPhoto");
            photoObj.transform.SetParent(photoContainer.transform, false);
            Image photoImg = photoObj.AddComponent<Image>();
            photoImg.raycastTarget = false;
            if (photoSprite != null)
            {
                photoImg.sprite = photoSprite;
                photoImg.color = Color.white;
            }
            else
            {
                photoImg.color = new Color(0.85f, 0.88f, 0.92f, 1f);
            }
            RectTransform pRt = photoObj.GetComponent<RectTransform>();
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.offsetMin = Vector2.zero;
            pRt.offsetMax = Vector2.zero;

            // 2. Color Category Badge: Overlaid on photo top-left (position x = 16f, y = -16f)
            GameObject badgeObj = CreatePanel("BadgePill", card.transform, badgeColor, 150f, 28f, s_SpriteRoundedRect8);
            RectTransform bRt = badgeObj.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0f, 1f);
            bRt.anchorMax = new Vector2(0f, 1f);
            bRt.pivot = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(16f, -16f);
            badgeObj.GetComponent<Image>().raycastTarget = false;

            TextMeshProUGUI badgeText = CreateText("Badge", badgeObj.transform, badge, 13.5f, Color.white, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.raycastTarget = false;
            SetFullStretch(badgeText.GetComponent<RectTransform>());
            MakePillFitText(badgeObj, badgeText, 14);

            // 3. Text Stack: Vertically centered in lower card body (y = 0 to -250), with center-aligned text & zero overflow
            GameObject textStack = new GameObject("TextStack");
            textStack.transform.SetParent(card.transform, false);
            RectTransform tsRt = textStack.AddComponent<RectTransform>();
            tsRt.anchorMin = new Vector2(0f, 0f);
            tsRt.anchorMax = new Vector2(1f, 0f);
            tsRt.pivot = new Vector2(0.5f, 0.5f);
            tsRt.anchoredPosition = new Vector2(0f, 125f);
            tsRt.sizeDelta = new Vector2(-40f, 210f); // 20px padding left/right

            VerticalLayoutGroup vlg = textStack.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 10f; // Slightly increased gap between title and desc

            // Title: fontSize = 25f, FontStyles.Bold, height 34f, Center aligned
            TextMeshProUGUI titleText = CreateText("Title", textStack.transform, title, 25f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.raycastTarget = false;
            RectTransform titRt = titleText.GetComponent<RectTransform>();
            titRt.sizeDelta = new Vector2(0f, 34f);

            // Description: fontSize = 19f, FontStyles.Bold, lineSpacing = 4f, height 100f, Center aligned, Truncate overflow
            TextMeshProUGUI descText = CreateText("Description", textStack.transform, desc, 19f, ColorPitchBlack, FontStyles.Bold, 4f, s_FontPlusJakartaSansBold);
            descText.alignment = TextAlignmentOptions.Center;
            descText.raycastTarget = false;
            descText.overflowMode = TextOverflowModes.Truncate; // Prevents missing ellipsis glyph warnings
            RectTransform dRt = descText.GetComponent<RectTransform>();
            dRt.sizeDelta = new Vector2(0f, 100f);

            // (Action indicator "Explore Location →" / "Enter Room →" removed as requested)

            feedback.InitializeRestingPosition();

            return card;
        }

        private static GameObject CreatePolaroidPlaceholder(
            string name, Transform parent, Vector2 size, Vector2 anchoredPos, float tiltAngle,
            string caption, Sprite photoSprite, out Image outPhotoImage, out TextMeshProUGUI outCaptionText)
        {
            GameObject card = CreatePanel(name, parent, new Color(1f, 1f, 1f, 0.98f), size.x, size.y, s_SpriteRoundedRect12);
            RectTransform rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.localRotation = Quaternion.Euler(0f, 0f, tiltAngle);

            var outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.2f, 0.4f, 0.20f);
            outline.effectDistance = new Vector2(2f, -2f);

            float photoHeight = size.y * 0.74f;
            GameObject photoArea = CreatePanel("PhotoArea", card.transform, Color.white, size.x - 20f, photoHeight, s_SpriteRoundedRect8);
            RectTransform paRt = photoArea.GetComponent<RectTransform>();
            paRt.anchorMin = new Vector2(0.5f, 1f);
            paRt.anchorMax = new Vector2(0.5f, 1f);
            paRt.pivot = new Vector2(0.5f, 1f);
            paRt.anchoredPosition = new Vector2(0f, -10f);

            outPhotoImage = photoArea.GetComponent<Image>();
            if (photoSprite != null)
            {
                outPhotoImage.sprite = photoSprite;
                outPhotoImage.color = Color.white;
            }

            outCaptionText = CreateText("Caption", card.transform, caption, 13.5f, ColorPitchBlack, FontStyles.Bold, 0f, s_FontPlusJakartaSansBold);
            outCaptionText.alignment = TextAlignmentOptions.Center;
            RectTransform capRt = outCaptionText.GetComponent<RectTransform>();
            capRt.anchorMin = new Vector2(0f, 0f);
            capRt.anchorMax = new Vector2(1f, 0f);
            capRt.pivot = new Vector2(0.5f, 0f);
            capRt.anchoredPosition = new Vector2(0f, 8f);
            capRt.sizeDelta = new Vector2(-16f, 22f);

            return card;
        }

        private static void SetFullStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        // ====================================================================
        // MULTI-SCENE ARCHITECTURE BUILDERS
        // ====================================================================

        public static void BuildMasterTourCoreScene()
        {
            const string scenePath = "Assets/Scenes/Master_Tour_Core.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Lighting: Neutral global sun, low intensity for HUD clarity
            GameObject lightObj = new GameObject("Directional Light");
            Light dirLight = lightObj.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(0.95f, 0.95f, 1f);
            dirLight.intensity = 0.8f;
            dirLight.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Global Volume
            GameObject volObj = new GameObject("Global Volume");
            Volume volume = volObj.AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile == null) profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DefaultVolumeProfile.asset");
            volume.sharedProfile = profile;

            // 2. XR Rig (Persistent)
            GameObject xrRig = SetupXRRig(out Camera mainCam);

            // 3. ScreenFade Root
            SetupScreenFade();

            // 4. Spatial UI Root (LazyFollow + CampusTourManager + MinimizedDockOrb)
            SetupSpatialUI(mainCam);

            // 5. Tour Scene Controller (SceneTransitionManager)
            SetupTourSceneController(xrRig);

            // Save Master Scene
            EditorUtility.SetDirty(xrRig);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BMU VR Tour] Master_Tour_Core.unity built successfully.");
        }

        public static void BuildExteriorScene()
        {
            const string scenePath = "Assets/Scenes/Scene_Campus_Exterior.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Directional Sun Light
            GameObject sunObj = new GameObject("Exterior_Directional_Light");
            Light sun = sunObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.98f, 0.92f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sunObj.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

            // Custom High-Fidelity Campus Exterior Environment Prefab
            GameObject exteriorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Prefab_Exterior_Campus.prefab");
            if (exteriorPrefab != null)
            {
                GameObject envInstance = (GameObject)PrefabUtility.InstantiatePrefab(exteriorPrefab);
                envInstance.name = "Campus_Exterior_Environment";
                envInstance.transform.position = Vector3.zero;
                envInstance.transform.rotation = Quaternion.identity;
                envInstance.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f); // Halved scale per user request
            }
            else
            {
                // Fallback to primitive placeholder facades if prefab not generated yet
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Campus_Terrain_And_Roads";
                ground.transform.position = new Vector3(0f, -0.1f, 0f);
                ground.transform.localScale = new Vector3(140f, 0.2f, 140f);
                ground.layer = 0;
                Material groundMat = GetOrCreateMaterial("Assets/Materials/M_CampusPlazaGround.mat", new Color(0.74f, 0.76f, 0.78f), 0.3f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

                GameObject archParent = new GameObject("Architecture_Exteriors");
                CreateBuildingFacade(archParent.transform, "FrontGate_Monument", "BMU MAIN GATEWAY",
                    new Vector3(0f, 0f, 18f), new Vector3(16f, 7f, 3f), new Color(0.85f, 0.22f, 0.22f));
                CreateBuildingFacade(archParent.transform, "NorthBlock_Exterior_Facade", "NORTH BLOCK (ACADEMIC COMPLEX)",
                    new Vector3(35f, 0f, 25f), new Vector3(24f, 9f, 16f), new Color(0.68f, 0.25f, 0.22f));
                CreateBuildingFacade(archParent.transform, "Gateway_Exterior_Facade", "THE GATEWAY (ADMIN & INCUBATOR)",
                    new Vector3(-35f, 0f, 25f), new Vector3(22f, 11f, 16f), new Color(0.20f, 0.38f, 0.58f));
                CreateBuildingFacade(archParent.transform, "Workshop_Exterior_Facade", "MECHANICAL WORKSHOP",
                    new Vector3(35f, 0f, -25f), new Vector3(22f, 8f, 18f), new Color(0.35f, 0.38f, 0.42f));
            }

            // Reflection Probe (scaled to cover halved campus)
            GameObject probeObj = new GameObject("Campus_Exterior_ReflectionProbe");
            ReflectionProbe probe = probeObj.AddComponent<ReflectionProbe>();
            probe.size = new Vector3(800f, 200f, 800f);
            probe.center = new Vector3(-250f, 50f, -175f);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;

            // Exterior Navigation Anchors (Layer 31) - Calibrated for 0.5x Scale
            GameObject anchorsParent = new GameObject("Exterior_Navigation_Anchors");
            Color anchorPadColor = new Color(0f, 0.45f, 0.85f);

            // 1. Initial Point / Front Gate: moved backward along entry road (+14m) looking through gate (-90° / facing -X)
            CreateTeleportAnchor("Anchor_FrontGate", new Vector3(14.0f, 0.05f, 0f), Quaternion.Euler(0f, -90f, 0f), anchorsParent.transform, anchorPadColor);

            // 2. The Gateway (GA_GB): moved closer to building facade (-116.5m vs building at -114.9m), centered on width (Z = -87.33m)
            CreateTeleportAnchor("Anchor_Gateway_Plaza", new Vector3(-116.5f, 0.05f, -87.33f), Quaternion.Euler(0f, -90f, 0f), anchorsParent.transform, anchorPadColor);

            // 3. North Block (NB): halved scale position (X = -480.23m, Z = -127.5m), facing north into entrance (0°)
            CreateTeleportAnchor("Anchor_NorthBlock_Plaza", new Vector3(-480.23f, 0.05f, -127.5f), Quaternion.Euler(0f, 0f, 0f), anchorsParent.transform, anchorPadColor);

            // 4. Workshop (MechanicalWorkshop): halved scale position (X = -411.63m, Z = -275.0m), facing south into entrance (180°)
            CreateTeleportAnchor("Anchor_Workshop_Plaza", new Vector3(-411.63f, 0.05f, -275.0f), Quaternion.Euler(0f, 180f, 0f), anchorsParent.transform, anchorPadColor);

            // Safety Recovery Floor underneath (covers full outer campus bounds at 0.5x scale)
            GameObject safetyFloor = new GameObject("OutOfBounds_ResetFloor");
            safetyFloor.transform.position = new Vector3(-250f, -5f, -175f);
            BoxCollider safetyCol = safetyFloor.AddComponent<BoxCollider>();
            safetyCol.isTrigger = true;
            safetyCol.size = new Vector3(1500f, 1f, 1500f);
            safetyFloor.AddComponent<OutOfBoundsSafetyTrigger>();

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BMU VR Tour] Scene_Campus_Exterior.unity built successfully.");
        }

        public static void BuildNorthBlockInteriorsScene()
        {
            const string scenePath = "Assets/Scenes/Scene_NorthBlock_Interiors.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Interior Soft Fill Light
            GameObject fillLightObj = new GameObject("Interior_Ambient_Light");
            Light fillLight = fillLightObj.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.92f, 0.94f, 0.98f);
            fillLight.intensity = 0.5f;
            fillLight.shadows = LightShadows.None;
            fillLightObj.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            // 1. Smart Tiered Classroom (Pod 1 at 0, 0, 0)
            CreateCubeRoomPod(null, "RoomPod_SmartClassroom", "nb-classroom", "North Block • Smart Tiered Classroom",
                new Vector3(0f, 0f, 0f),
                new Color(0.94f, 0.92f, 0.88f),
                new Color(0.25f, 0.28f, 0.32f),
                new Vector3(14f, 4.2f, 12f),
                "Anchor_Classroom_Center", new Vector3(0f, 0f, -2f),
                "Anchor_Classroom_Podium", new Vector3(0f, 0f, 3.5f),
                RoomPropStyle.Classroom);

            // 2. AI & Robotics Lab (Pod 2 at 60, 0, 0)
            CreateCubeRoomPod(null, "RoomPod_RoboticsLab", "nb-lab", "North Block • AI & Robotics Lab",
                new Vector3(60f, 0f, 0f),
                new Color(0.88f, 0.90f, 0.94f),
                new Color(0.20f, 0.22f, 0.25f),
                new Vector3(14f, 4.2f, 12f),
                "Anchor_Lab_Center", new Vector3(0f, 0f, -2f),
                "Anchor_Lab_Workstation", new Vector3(3.5f, 0f, 2f),
                RoomPropStyle.Lab);

            // 3. Faculty Cabins (Pod 3 at 120, 0, 0)
            CreateCubeRoomPod(null, "RoomPod_FacultyCabins", "nb-faculty", "North Block • 4th Floor Faculty Cabins",
                new Vector3(120f, 0f, 0f),
                new Color(0.92f, 0.90f, 0.86f),
                new Color(0.35f, 0.28f, 0.22f),
                new Vector3(12f, 4.2f, 10f),
                "Anchor_Faculty_Entry", new Vector3(0f, 0f, -2f),
                "Anchor_Faculty_Desk", new Vector3(2.5f, 0f, 2f),
                RoomPropStyle.Faculty);

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BMU VR Tour] Scene_NorthBlock_Interiors.unity built successfully.");
        }

        public static void BuildGatewayInteriorsScene()
        {
            const string scenePath = "Assets/Scenes/Scene_Gateway_Interiors.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Interior Soft Fill Light
            GameObject fillLightObj = new GameObject("Interior_Ambient_Light");
            Light fillLight = fillLightObj.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.92f, 0.94f, 0.98f);
            fillLight.intensity = 0.5f;
            fillLight.shadows = LightShadows.None;
            fillLightObj.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            // 1. Executive MBA Seminar Room (Pod 1 at 0, 0, 0)
            CreateCubeRoomPod(null, "RoomPod_ExecutiveMBASeminar", "gw-classroom", "Gateway • Executive MBA Seminar Room",
                new Vector3(0f, 0f, 0f),
                new Color(0.92f, 0.92f, 0.95f),
                new Color(0.18f, 0.22f, 0.30f),
                new Vector3(14f, 4.2f, 12f),
                "Anchor_Seminar_Center", new Vector3(0f, 0f, -2f),
                "Anchor_Seminar_Boardroom", new Vector3(0f, 0f, 2.5f),
                RoomPropStyle.Seminar);

            // 2. ACIC Startup Incubator Lab (Pod 2 at 60, 0, 0)
            CreateCubeRoomPod(null, "RoomPod_ACICStartupIncubator", "gw-lab", "Gateway • ACIC Startup Incubator Lab",
                new Vector3(60f, 0f, 0f),
                new Color(0.95f, 0.95f, 0.95f),
                new Color(0.28f, 0.30f, 0.32f),
                new Vector3(14f, 4.2f, 12f),
                "Anchor_Incubator_Center", new Vector3(0f, 0f, -2f),
                "Anchor_Incubator_MakerBay", new Vector3(3.5f, 0f, 2.5f),
                RoomPropStyle.Incubator);

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BMU VR Tour] Scene_Gateway_Interiors.unity built successfully.");
        }

        public static void UpdateBuildSettings()
        {
            var scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Master_Tour_Core.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Scene_Campus_Exterior.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Scene_NorthBlock_Interiors.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Scene_Gateway_Interiors.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", false)
            };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("[BMU VR Tour] Configured EditorBuildSettings with 4 active tour scenes.");
        }

        private static GameObject SetupTourSceneController(GameObject xrRig)
        {
            GameObject controllerObj = new GameObject("[Tour_Scene_Controller]");
            var stm = controllerObj.AddComponent<SceneTransitionManager>();
            if (xrRig != null)
            {
                var origin = xrRig.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
                var cc = xrRig.GetComponentInChildren<CharacterController>(true);
                var sf = UnityEngine.Object.FindAnyObjectByType<ScreenFade>();

                SerializedObject so = new SerializedObject(stm);
                var originProp = so.FindProperty("m_XROrigin");
                if (originProp != null && origin != null) originProp.objectReferenceValue = origin;
                var ccProp = so.FindProperty("m_CharacterController");
                if (ccProp != null && cc != null) ccProp.objectReferenceValue = cc;
                var sfProp = so.FindProperty("m_ScreenFade");
                if (sfProp != null && sf != null) sfProp.objectReferenceValue = sf;
                so.ApplyModifiedProperties();
            }
            return controllerObj;
        }

        private enum RoomPropStyle
        {
            Classroom,
            Lab,
            Faculty,
            Seminar,
            Incubator
        }

        private static GameObject CreateCubeRoomPod(
            Transform parent,
            string podName,
            string podKey,
            string displayName,
            Vector3 podPosition,
            Color wallColor,
            Color floorColor,
            Vector3 roomSize,
            string primaryAnchorName,
            Vector3 primaryAnchorLocalPos,
            string secondaryAnchorName,
            Vector3 secondaryAnchorLocalPos,
            RoomPropStyle propStyle)
        {
            GameObject podObj = new GameObject(podName);
            if (parent != null) podObj.transform.SetParent(parent, false);
            podObj.transform.position = podPosition;

            RoomPodAnchorController controller = podObj.AddComponent<RoomPodAnchorController>();
            SetPrivateField(controller, "m_PodKey", podKey);
            SetPrivateField(controller, "m_PodDisplayName", displayName);

            // Geometry Container
            GameObject geoObj = new GameObject("Geometry");
            geoObj.transform.SetParent(podObj.transform, false);

            Material wallMat = GetOrCreateMaterial($"Assets/Materials/M_Wall_{podKey}.mat", wallColor, 0.15f);
            Material floorMat = GetOrCreateMaterial($"Assets/Materials/M_Floor_{podKey}.mat", floorColor, 0.35f);
            Material ceilingMat = GetOrCreateMaterial($"Assets/Materials/M_Ceiling_Clean.mat", new Color(0.96f, 0.96f, 0.97f), 0.1f);
            Material propMat = GetOrCreateMaterial($"Assets/Materials/M_Prop_{podKey}.mat", new Color(0.2f, 0.3f, 0.42f), 0.5f);

            // Floor (BoxCollider on Layer 0)
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(geoObj.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(roomSize.x, 0.2f, roomSize.z);
            floor.layer = 0;
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Ceiling (BoxCollider on Layer 0)
            GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Ceiling";
            ceiling.transform.SetParent(geoObj.transform, false);
            ceiling.transform.localPosition = new Vector3(0f, roomSize.y + 0.1f, 0f);
            ceiling.transform.localScale = new Vector3(roomSize.x, 0.2f, roomSize.z);
            ceiling.layer = 0;
            ceiling.GetComponent<MeshRenderer>().sharedMaterial = ceilingMat;

            // 4 Perimeter Boundary Enclosure Walls (BoxColliders prevent physically walking outside)
            GameObject wallNorth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallNorth.name = "Wall_North";
            wallNorth.transform.SetParent(geoObj.transform, false);
            wallNorth.transform.localPosition = new Vector3(0f, roomSize.y * 0.5f, roomSize.z * 0.5f + 0.1f);
            wallNorth.transform.localScale = new Vector3(roomSize.x, roomSize.y, 0.2f);
            wallNorth.layer = 0;
            wallNorth.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            GameObject wallSouth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallSouth.name = "Wall_South";
            wallSouth.transform.SetParent(geoObj.transform, false);
            wallSouth.transform.localPosition = new Vector3(0f, roomSize.y * 0.5f, -roomSize.z * 0.5f - 0.1f);
            wallSouth.transform.localScale = new Vector3(roomSize.x, roomSize.y, 0.2f);
            wallSouth.layer = 0;
            wallSouth.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            GameObject wallEast = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallEast.name = "Wall_East";
            wallEast.transform.SetParent(geoObj.transform, false);
            wallEast.transform.localPosition = new Vector3(roomSize.x * 0.5f + 0.1f, roomSize.y * 0.5f, 0f);
            wallEast.transform.localScale = new Vector3(0.2f, roomSize.y, roomSize.z);
            wallEast.layer = 0;
            wallEast.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            GameObject wallWest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallWest.name = "Wall_West";
            wallWest.transform.SetParent(geoObj.transform, false);
            wallWest.transform.localPosition = new Vector3(-roomSize.x * 0.5f - 0.1f, roomSize.y * 0.5f, 0f);
            wallWest.transform.localScale = new Vector3(0.2f, roomSize.y, roomSize.z);
            wallWest.layer = 0;
            wallWest.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            // Void Shielding: Frosted Window & Closed Door
            GameObject voidShielding = new GameObject("Void_Shielding");
            voidShielding.transform.SetParent(podObj.transform, false);

            GameObject windowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            windowQuad.name = "Window_Backdrop_Quad";
            windowQuad.transform.SetParent(voidShielding.transform, false);
            windowQuad.transform.localPosition = new Vector3(roomSize.x * 0.5f - 0.05f, 2.0f, 0f);
            windowQuad.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            windowQuad.transform.localScale = new Vector3(4.5f, 2.0f, 1f);
            Material frostedMat = GetOrCreateMaterial("Assets/Materials/M_FrostedWindowGlass.mat", new Color(0.85f, 0.92f, 0.98f, 0.75f), 0.9f);
            windowQuad.GetComponent<MeshRenderer>().sharedMaterial = frostedMat;
            Collider wCol = windowQuad.GetComponent<Collider>();
            if (wCol != null) UnityEngine.Object.DestroyImmediate(wCol);

            GameObject doorQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            doorQuad.name = "Closed_Door_Mesh";
            doorQuad.transform.SetParent(voidShielding.transform, false);
            doorQuad.transform.localPosition = new Vector3(0f, 1.25f, -roomSize.z * 0.5f + 0.05f);
            doorQuad.transform.localRotation = Quaternion.identity;
            doorQuad.transform.localScale = new Vector3(1.6f, 2.5f, 1f);
            Material doorMat = GetOrCreateMaterial("Assets/Materials/M_ClosedDoor_Wood.mat", new Color(0.28f, 0.18f, 0.12f), 0.4f);
            doorQuad.GetComponent<MeshRenderer>().sharedMaterial = doorMat;
            Collider dCol = doorQuad.GetComponent<Collider>();
            if (dCol != null) UnityEngine.Object.DestroyImmediate(dCol);

            // Props
            CreateRoomProps(geoObj.transform, propStyle, propMat, roomSize);

            // Ceiling Downlight
            GameObject lightObj = new GameObject("Interior_Downlight");
            lightObj.transform.SetParent(podObj.transform, false);
            lightObj.transform.localPosition = new Vector3(0f, roomSize.y - 0.3f, 0f);
            Light pLight = lightObj.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = new Color(1.0f, 0.97f, 0.90f);
            pLight.intensity = 2.2f;
            pLight.range = 14f;
            pLight.shadows = LightShadows.Soft;

            GameObject probeObj = new GameObject("Classroom_ReflectionProbe");
            probeObj.transform.SetParent(podObj.transform, false);
            probeObj.transform.localPosition = new Vector3(0f, roomSize.y * 0.5f, 0f);
            ReflectionProbe probe = probeObj.AddComponent<ReflectionProbe>();
            probe.size = roomSize;
            probe.boxProjection = true;
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;

            // Curated Teleportation Anchors (Layer 31)
            GameObject anchorsParent = new GameObject("Anchors");
            anchorsParent.transform.SetParent(podObj.transform, false);

            GameObject primaryAnchor = CreateTeleportAnchor(
                primaryAnchorName,
                podPosition + primaryAnchorLocalPos,
                Quaternion.identity,
                anchorsParent.transform,
                new Color(0f, 0.35f, 0.70f));

            GameObject secondaryAnchor = CreateTeleportAnchor(
                secondaryAnchorName,
                podPosition + secondaryAnchorLocalPos,
                Quaternion.identity,
                anchorsParent.transform,
                new Color(0f, 0.60f, 0.90f));

            SetPrivateField(controller, "m_PrimarySpawnAnchor", primaryAnchor.transform);
            SetPrivateField(controller, "m_AlternateAnchors", new Transform[] { secondaryAnchor.transform });
            SetPrivateField(controller, "m_FallbackRecoveryAnchor", primaryAnchor.transform);

            // Out of Bounds Safety Recovery Floor Trigger (Y = -2.0m)
            GameObject safetyFloor = new GameObject("OutOfBounds_ResetFloor");
            safetyFloor.transform.SetParent(podObj.transform, false);
            safetyFloor.transform.localPosition = new Vector3(0f, -2.0f, 0f);
            BoxCollider safetyCol = safetyFloor.AddComponent<BoxCollider>();
            safetyCol.isTrigger = true;
            safetyCol.size = new Vector3(roomSize.x * 2.5f, 0.5f, roomSize.z * 2.5f);
            OutOfBoundsSafetyTrigger safetyTrigger = safetyFloor.AddComponent<OutOfBoundsSafetyTrigger>();
            SetPrivateField(safetyTrigger, "m_FallbackAnchor", primaryAnchor.transform);
            SetPrivateField(safetyTrigger, "m_PodController", controller);

            return podObj;
        }

        private static void CreateRoomProps(Transform parent, RoomPropStyle style, Material propMat, Vector3 roomSize)
        {
            GameObject propsParent = new GameObject("Room_Props");
            propsParent.transform.SetParent(parent, false);

            switch (style)
            {
                case RoomPropStyle.Classroom:
                    GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    board.name = "Smartboard";
                    board.transform.SetParent(propsParent.transform, false);
                    board.transform.localPosition = new Vector3(0f, 2.0f, roomSize.z * 0.5f - 0.2f);
                    board.transform.localScale = new Vector3(4.0f, 1.8f, 0.08f);
                    board.layer = 0;
                    board.GetComponent<MeshRenderer>().sharedMaterial = propMat;

                    GameObject podium = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    podium.name = "TeacherPodium";
                    podium.transform.SetParent(propsParent.transform, false);
                    podium.transform.localPosition = new Vector3(0f, 0.55f, roomSize.z * 0.5f - 2.0f);
                    podium.transform.localScale = new Vector3(1.6f, 1.1f, 0.9f);
                    podium.layer = 0;
                    podium.GetComponent<MeshRenderer>().sharedMaterial = propMat;
                    break;

                case RoomPropStyle.Lab:
                    GameObject bench1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bench1.name = "HardwareLabBench_Left";
                    bench1.transform.SetParent(propsParent.transform, false);
                    bench1.transform.localPosition = new Vector3(-3.0f, 0.45f, 1.5f);
                    bench1.transform.localScale = new Vector3(2.5f, 0.9f, 4.0f);
                    bench1.layer = 0;
                    bench1.GetComponent<MeshRenderer>().sharedMaterial = propMat;

                    GameObject bench2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bench2.name = "HardwareLabBench_Right";
                    bench2.transform.SetParent(propsParent.transform, false);
                    bench2.transform.localPosition = new Vector3(3.0f, 0.45f, 1.5f);
                    bench2.transform.localScale = new Vector3(2.5f, 0.9f, 4.0f);
                    bench2.layer = 0;
                    bench2.GetComponent<MeshRenderer>().sharedMaterial = propMat;

                    GameObject serverRack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    serverRack.name = "GPUServerRack";
                    serverRack.transform.SetParent(propsParent.transform, false);
                    serverRack.transform.localPosition = new Vector3(-roomSize.x * 0.5f + 1.0f, 1.1f, -2.0f);
                    serverRack.transform.localScale = new Vector3(0.9f, 2.2f, 1.2f);
                    serverRack.layer = 0;
                    serverRack.GetComponent<MeshRenderer>().sharedMaterial = propMat;
                    break;

                case RoomPropStyle.Faculty:
                    GameObject desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    desk.name = "FacultyDesk";
                    desk.transform.SetParent(propsParent.transform, false);
                    desk.transform.localPosition = new Vector3(2.0f, 0.42f, 1.5f);
                    desk.transform.localScale = new Vector3(2.2f, 0.84f, 1.2f);
                    desk.layer = 0;
                    desk.GetComponent<MeshRenderer>().sharedMaterial = propMat;

                    GameObject bookcase = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bookcase.name = "ResearchBookcase";
                    bookcase.transform.SetParent(propsParent.transform, false);
                    bookcase.transform.localPosition = new Vector3(roomSize.x * 0.5f - 0.4f, 1.2f, 1.5f);
                    bookcase.transform.localScale = new Vector3(0.6f, 2.4f, 2.5f);
                    bookcase.layer = 0;
                    bookcase.GetComponent<MeshRenderer>().sharedMaterial = propMat;
                    break;

                case RoomPropStyle.Seminar:
                    GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    table.name = "ExecutiveConferenceTable";
                    table.transform.SetParent(propsParent.transform, false);
                    table.transform.localPosition = new Vector3(0f, 0.42f, 1.0f);
                    table.transform.localScale = new Vector3(4.5f, 0.84f, 2.5f);
                    table.layer = 0;
                    table.GetComponent<MeshRenderer>().sharedMaterial = propMat;
                    break;

                case RoomPropStyle.Incubator:
                    GameObject makerBay = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    makerBay.name = "StartupPrototypingBay";
                    makerBay.transform.SetParent(propsParent.transform, false);
                    makerBay.transform.localPosition = new Vector3(3.0f, 0.48f, 1.5f);
                    makerBay.transform.localScale = new Vector3(2.8f, 0.96f, 3.2f);
                    makerBay.layer = 0;
                    makerBay.GetComponent<MeshRenderer>().sharedMaterial = propMat;
                    break;
            }
        }

        private static void CreateBuildingFacade(Transform parent, string name, string label, Vector3 position, Vector3 size, Color color)
        {
            GameObject bldg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bldg.name = name;
            bldg.transform.SetParent(parent, false);
            bldg.transform.position = position + new Vector3(0f, size.y * 0.5f, 0f);
            bldg.transform.localScale = size;
            bldg.layer = 0;

            Material bldgMat = GetOrCreateMaterial($"Assets/Materials/M_Facade_{name}.mat", color, 0.25f);
            bldg.GetComponent<MeshRenderer>().sharedMaterial = bldgMat;
        }

        private static GameObject CreateTeleportAnchor(string name, Vector3 position, Quaternion rotation, Transform parent, Color padColor)
        {
            GameObject anchorObj = new GameObject(name);
            if (parent != null) anchorObj.transform.SetParent(parent, false);
            anchorObj.transform.position = position;
            anchorObj.transform.rotation = rotation;
            anchorObj.layer = 31; // Teleport layer

            // Visual pad on floor: cylinder of height 0.02, diameter 1.4m
            GameObject padVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            padVisual.name = "AnchorVisualPad";
            padVisual.transform.SetParent(anchorObj.transform, false);
            padVisual.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            padVisual.transform.localScale = new Vector3(1.4f, 0.01f, 1.4f);
            padVisual.layer = 31;

            Collider padCol = padVisual.GetComponent<Collider>();

            TeleportationAnchor anchor = anchorObj.AddComponent<TeleportationAnchor>();
            anchor.teleportAnchorTransform = anchorObj.transform;
            anchor.matchOrientation = MatchOrientation.TargetUpAndForward;
            anchor.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
            anchor.interactionLayers = 1 << 31;
            if (padCol != null)
            {
                anchor.colliders.Add(padCol);
            }

            string matPath = $"Assets/Materials/M_AnchorPad_{ColorUtility.ToHtmlStringRGB(padColor)}.mat";
            Material padMat = GetOrCreateMaterial(matPath, padColor, 0.2f);
            if (padMat.HasProperty("_EmissionColor"))
            {
                padMat.EnableKeyword("_EMISSION");
                padMat.SetColor("_EmissionColor", padColor * 0.65f);
            }
            padVisual.GetComponent<MeshRenderer>().sharedMaterial = padMat;

            return anchorObj;
        }

        private static Material GetOrCreateMaterial(string assetPath, Color color, float smoothness = 0.5f)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                mat.color = color;
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            return mat;
        }
    }
}
