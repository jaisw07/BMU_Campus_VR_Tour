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
        private static readonly Color ColorPrimary = new Color(0f, 0.29f, 0.53f);    // #004B87
        private static readonly Color ColorAccent = new Color(0f, 0.40f, 0.70f);     // #0066B3
        private static readonly Color ColorRed = new Color(0.89f, 0.11f, 0.14f);     // #E31B23
        private static readonly Color ColorGlassBg = new Color(1f, 1f, 1f, 0.48f);   // 48% Alpha (Figma parity)
        private static readonly Color ColorGlassCard = new Color(1f, 1f, 1f, 0.56f); // 56% Alpha internal cards
        private static readonly Color ColorGlassHUD = new Color(1f, 1f, 1f, 0.65f);  // 65% Alpha HUDs
        private static readonly Color ColorGlassRim = new Color(1f, 1f, 1f, 0.85f);  // 85% Alpha outer rim
        private static readonly Color ColorGlassInactive = new Color(1f, 1f, 1f, 0.32f); // 32% Alpha inactive
        private static readonly Color ColorWhite = Color.white;

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
            // Try to load XR Origin Hands prefab or Starter Assets XR Origin
            string handsPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.0.7/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";
            string starterPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.0.7/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(handsPrefabPath);
            if (rigPrefab == null)
            {
                rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(starterPrefabPath);
            }

            GameObject rigInstance;
            if (rigPrefab != null)
            {
                rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
                rigInstance.name = "XR Origin (VR Headset & Hands)";
                rigInstance.transform.position = Vector3.zero;
                rigInstance.transform.rotation = Quaternion.identity;
            }
            else
            {
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
                var xrOriginComp = rigInstance.GetComponent<Unity.XR.CoreUtils.XROrigin>();
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
                mp.useGravity = false;
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

            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Samples/XR Interaction Toolkit/3.0.7/Starter Assets/XRI Default Input Actions.inputactions");
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

            // Background Glass Panel
            GameObject bgPanel = CreatePanel("GlassBackgroundPanel", deckContainer.transform, Color.white, 1400f, 850f, s_SpriteGlassPanel26);
            bgPanel.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            bgPanel.GetComponent<RectTransform>().anchorMax = Vector2.one;
            bgPanel.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            bgPanel.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            Image bgImg = bgPanel.GetComponent<Image>();
            if (bgImg != null)
            {
                bgImg.raycastTarget = false;
            }

            // ================================================================
            // TOP HEADER HUD
            // ================================================================
            GameObject header = CreatePanel("TopHeaderHUD", deckContainer.transform, Color.white, 1360f, 64f, s_SpriteGlassPanel16);
            RectTransform headerRt = header.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -15f);
            headerRt.sizeDelta = new Vector2(-40f, 64f);

            // Brand Badge container with Logo and Dual-line Typography
            GameObject brandBadge = new GameObject("HeaderBrandBadge");
            brandBadge.transform.SetParent(header.transform, false);
            RectTransform bbRt = brandBadge.AddComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0f, 0.5f);
            bbRt.anchorMax = new Vector2(0f, 0.5f);
            bbRt.pivot = new Vector2(0f, 0.5f);
            bbRt.anchoredPosition = new Vector2(25f, 0f);
            bbRt.sizeDelta = new Vector2(380f, 50f);

            if (s_SpriteLogo != null)
            {
                GameObject logoObj = new GameObject("LogoImage");
                logoObj.transform.SetParent(brandBadge.transform, false);
                Image logoImg = logoObj.AddComponent<Image>();
                logoImg.sprite = s_SpriteLogo;
                logoImg.preserveAspect = true;
                logoImg.raycastTarget = false;
                RectTransform logoRt = logoObj.GetComponent<RectTransform>();
                logoRt.anchorMin = new Vector2(0f, 0.5f);
                logoRt.anchorMax = new Vector2(0f, 0.5f);
                logoRt.pivot = new Vector2(0f, 0.5f);
                logoRt.anchoredPosition = new Vector2(0f, 0f);
                logoRt.sizeDelta = new Vector2(42f, 42f);
            }

            GameObject textCol = new GameObject("TextColumn");
            textCol.transform.SetParent(brandBadge.transform, false);
            RectTransform tcRt = textCol.AddComponent<RectTransform>();
            tcRt.anchorMin = new Vector2(0f, 0.5f);
            tcRt.anchorMax = new Vector2(0f, 0.5f);
            tcRt.pivot = new Vector2(0f, 0.5f);
            tcRt.anchoredPosition = new Vector2(50f, 0f);
            tcRt.sizeDelta = new Vector2(320f, 46f);

            TextMeshProUGUI brandTitle = CreateText("BrandTitle", textCol.transform, "BML MUNJAL UNIVERSITY", 14.5f, ColorNavy, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            RectTransform btRt = brandTitle.GetComponent<RectTransform>();
            btRt.anchorMin = new Vector2(0f, 1f);
            btRt.anchorMax = new Vector2(1f, 1f);
            btRt.pivot = new Vector2(0f, 1f);
            btRt.anchoredPosition = new Vector2(0f, 0f);
            btRt.sizeDelta = new Vector2(320f, 24f);

            TextMeshProUGUI brandSub = CreateText("BrandSubtitle", textCol.transform, "VR GUIDED CAMPUS WALKTHROUGH", 10f, ColorPrimary, FontStyles.Bold, 0f, s_FontPlusJakartaSansSemiBold);
            RectTransform bsRt = brandSub.GetComponent<RectTransform>();
            bsRt.anchorMin = new Vector2(0f, 0f);
            bsRt.anchorMax = new Vector2(1f, 0f);
            bsRt.pivot = new Vector2(0f, 0f);
            bsRt.anchoredPosition = new Vector2(0f, 2f);
            bsRt.sizeDelta = new Vector2(320f, 18f);

            // Location Chip Pill in center (Crisp white pill with navy border, centered location text, no leading dot)
            GameObject chipPill = CreatePanel("LocationChipPill", header.transform, Color.white, 380f, 38f, s_SpriteHudChipPill != null ? s_SpriteHudChipPill : s_SpriteGlassPill);
            RectTransform chipRt = chipPill.GetComponent<RectTransform>();
            chipRt.anchorMin = new Vector2(0.5f, 0.5f);
            chipRt.anchorMax = new Vector2(0.5f, 0.5f);
            chipRt.pivot = new Vector2(0.5f, 0.5f);
            chipRt.anchoredPosition = new Vector2(0f, -2f);

            TextMeshProUGUI locText = CreateText("LocationText", chipPill.transform, "FRONT GATE • WELCOME & INTRO", 12f, ColorNavy, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            locText.alignment = TextAlignmentOptions.Center;
            RectTransform ltRt = locText.GetComponent<RectTransform>();
            ltRt.anchorMin = Vector2.zero;
            ltRt.anchorMax = Vector2.one;
            ltRt.pivot = new Vector2(0.5f, 0.5f);
            ltRt.offsetMin = Vector2.zero;
            ltRt.offsetMax = Vector2.zero;

            // --- Top-Right Header Cluster: [Recenter UI] -> [Restart] -> [Move (+)] -> [Minimize (-)] ---
            // 1. Minimize Icon Button (- Icon)
            Button btnHeaderMinimize = CreateIconButton("BtnHeaderMinimize", header.transform, s_SpriteMinimizeDash, Color.white, ColorNavy, 40f, s_SpriteGlassCircle, "Minimize to 360° Dock");
            RectTransform minRt = btnHeaderMinimize.GetComponent<RectTransform>();
            minRt.anchorMin = new Vector2(1f, 0.5f);
            minRt.anchorMax = new Vector2(1f, 0.5f);
            minRt.pivot = new Vector2(1f, 0.5f);
            minRt.anchoredPosition = new Vector2(-20f, 0f);

            // 2. Drag to Move Icon Button (+ Directional Move Icon)
            Button btnHeaderDragMove = CreateIconButton("BtnHeaderDragMove", header.transform, s_SpriteMoveDirectional, Color.white, ColorNavy, 40f, s_SpriteGlassCircle, "Drag to Move");
            RectTransform dragRt = btnHeaderDragMove.GetComponent<RectTransform>();
            dragRt.anchorMin = new Vector2(1f, 0.5f);
            dragRt.anchorMax = new Vector2(1f, 0.5f);
            dragRt.pivot = new Vector2(1f, 0.5f);
            dragRt.anchoredPosition = new Vector2(-70f, 0f);

            var dragHandle = btnHeaderDragMove.gameObject.AddComponent<VRUIDraggableHandle>();
            SetPrivateField(dragHandle, "m_UIRoot", uiRoot.transform);
            SetPrivateField(dragHandle, "m_LazyFollowUI", lazyFollow);
            SetPrivateField(dragHandle, "m_HandleImage", btnHeaderDragMove.GetComponent<Image>());

            // 3. Restart Tour Icon Button (Circular Loop Arrow)
            Button btnRestart = CreateIconButton("BtnHeaderRestart", header.transform, s_SpriteRestartTour, Color.white, ColorAccent, 40f, s_SpriteGlassCircle, "Restart Tour");
            RectTransform restartRt = btnRestart.GetComponent<RectTransform>();
            restartRt.anchorMin = new Vector2(1f, 0.5f);
            restartRt.anchorMax = new Vector2(1f, 0.5f);
            restartRt.pivot = new Vector2(1f, 0.5f);
            restartRt.anchoredPosition = new Vector2(-120f, 0f);

            // 4. Recenter UI Pill Button
            Button btnRecenter = CreateButton("BtnRecenterUI", header.transform, "Recenter UI", ColorNavy, 130f, 38f, 13.5f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
            RectTransform recRt = btnRecenter.GetComponent<RectTransform>();
            recRt.anchorMin = new Vector2(1f, 0.5f);
            recRt.anchorMax = new Vector2(1f, 0.5f);
            recRt.pivot = new Vector2(1f, 0.5f);
            recRt.anchoredPosition = new Vector2(-175f, 0f);

            // ================================================================
            // STAGE 1: INTRO 3D CAROUSEL PANEL
            // ================================================================
            GameObject introPanel = new GameObject("Stage1_IntroCarouselPanel");
            introPanel.transform.SetParent(deckContainer.transform, false);
            RectTransform introRt = introPanel.AddComponent<RectTransform>();
            introRt.anchorMin = Vector2.zero;
            introRt.anchorMax = Vector2.one;
            introRt.offsetMin = new Vector2(20f, 95f);
            introRt.offsetMax = new Vector2(-20f, -85f);

            // Carousel Card (High Fidelity 1120 x 540 with 50% alpha acrylic glass)
            GameObject cardObj = CreatePanel("CarouselSlideCard", introPanel.transform, Color.white, 1120f, 540f, s_SpriteGlassPanel26);
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

            // Slide Badge Pill
            GameObject badgePill = CreatePanel("SlideBadgePill", leftContainer.transform, new Color(0f, 0.17f, 0.29f, 0.08f), 220f, 28f, s_SpriteGlassPill);
            RectTransform badgeRt = badgePill.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(0f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.anchoredPosition = new Vector2(50f, -32f);

            TextMeshProUGUI slideBadge = CreateText("SlideBadge", badgePill.transform, "HERO GROUP LEGACY", 11.5f, ColorPrimary, FontStyles.Bold, 0f, s_FontPlusJakartaSansBold);
            slideBadge.alignment = TextAlignmentOptions.Center;
            SetFullStretch(slideBadge.GetComponent<RectTransform>());

            // Slide Title (Spaced 10px below badge)
            TextMeshProUGUI slideTitle = CreateText("SlideTitle", leftContainer.transform, "Welcome to BML Munjal University", 30f, ColorNavy, FontStyles.Bold);
            RectTransform titleRt = slideTitle.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(50f, -68f);
            titleRt.sizeDelta = new Vector2(-70f, 52f);

            // Slide Description (Spaced 15px below title, 18pt font with 16% line spacing)
            TextMeshProUGUI slideDesc = CreateText("SlideDesc", leftContainer.transform, "Description text goes here...", 18f, ColorNavy, FontStyles.Normal, 16f);
            RectTransform descRt = slideDesc.GetComponent<RectTransform>();
            descRt.anchorMin = new Vector2(0f, 1f);
            descRt.anchorMax = new Vector2(1f, 1f);
            descRt.pivot = new Vector2(0f, 1f);
            descRt.anchoredPosition = new Vector2(50f, -135f);
            descRt.sizeDelta = new Vector2(-70f, 115f);

            // Slide Stats (Spaced 18px below description, 17pt font with 20% line spacing)
            TextMeshProUGUI slideStats = CreateText("SlideStats", leftContainer.transform, "• Founded by Hero Group\n• Mentored by Imperial College London\n• Experiential 3-2-1 Curriculum", 17f, ColorPrimary, FontStyles.Bold, 20f);
            RectTransform statsRt = slideStats.GetComponent<RectTransform>();
            statsRt.anchorMin = new Vector2(0f, 1f);
            statsRt.anchorMax = new Vector2(1f, 1f);
            statsRt.pivot = new Vector2(0f, 1f);
            statsRt.anchoredPosition = new Vector2(50f, -265f);
            statsRt.sizeDelta = new Vector2(-70f, 130f);

            // Slide Index (Bottom-Left inside LeftContainer, strictly bounded)
            TextMeshProUGUI slideIndex = CreateText("SlideIndex", leftContainer.transform, "SLIDE 1 OF 5", 16f, ColorAccent, FontStyles.Bold);
            slideIndex.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform idxRt = slideIndex.GetComponent<RectTransform>();
            idxRt.anchorMin = new Vector2(0f, 0f);
            idxRt.anchorMax = new Vector2(0f, 0f);
            idxRt.pivot = new Vector2(0f, 0f);
            idxRt.anchoredPosition = new Vector2(50f, 32f);
            idxRt.sizeDelta = new Vector2(180f, 40f);

            // Skip Button (Bottom-Right inside LeftContainer)
            Button btnSkip = CreateButton("BtnSkipIntro", leftContainer.transform, "Explore Campus Hub →", ColorRed, 240f, 48f, 15f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
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

            Image[] polaroidImages = new Image[] { imgA, imgB, imgC };
            TextMeshProUGUI[] polaroidCaptions = new TextMeshProUGUI[] { capA, capB, capC };

            // Left / Right Circular Chevrons (58x58px frosted buttons with SVG vector arrows)
            Button btnPrev = CreateButton("BtnPrevSlide", introPanel.transform, "", Color.white, 58f, 58f, 16f, s_SpriteGlassCircle);
            RectTransform prevRt = btnPrev.GetComponent<RectTransform>();
            prevRt.anchorMin = new Vector2(0f, 0.5f);
            prevRt.anchorMax = new Vector2(0f, 0.5f);
            prevRt.pivot = new Vector2(0.5f, 0.5f);
            prevRt.anchoredPosition = new Vector2(50f, 0f);
            if (s_SpriteArrowLeft != null)
            {
                GameObject icon = new GameObject("Icon");
                icon.transform.SetParent(btnPrev.transform, false);
                Image iconImg = icon.AddComponent<Image>();
                iconImg.sprite = s_SpriteArrowLeft;
                iconImg.color = ColorNavy;
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
            nextRt.anchoredPosition = new Vector2(-50f, 0f);
            if (s_SpriteArrowRight != null)
            {
                GameObject icon = new GameObject("Icon");
                icon.transform.SetParent(btnNext.transform, false);
                Image iconImg = icon.AddComponent<Image>();
                iconImg.sprite = s_SpriteArrowRight;
                iconImg.color = ColorNavy;
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
            hubRt.offsetMin = new Vector2(20f, 95f);
            hubRt.offsetMax = new Vector2(-20f, -85f);

            // Hub Heading
            TextMeshProUGUI hubTitle = CreateText("HubTitle", hubPanel.transform, "Select a Destination to Begin Tour", 32f, ColorNavy, FontStyles.Bold);
            hubTitle.alignment = TextAlignmentOptions.Center;
            RectTransform hubTitleRt = hubTitle.GetComponent<RectTransform>();
            hubTitleRt.anchorMin = new Vector2(0.5f, 1f);
            hubTitleRt.anchorMax = new Vector2(0.5f, 1f);
            hubTitleRt.pivot = new Vector2(0.5f, 1f);
            hubTitleRt.anchoredPosition = new Vector2(0f, -10f);
            hubTitleRt.sizeDelta = new Vector2(1000f, 45f);

            TextMeshProUGUI hubSubtitle = CreateText("HubSubtitle", hubPanel.transform, "Choose one of the 3 primary university facilities to teleport directly to its exterior plaza.", 17f, ColorAccent, FontStyles.Normal);
            hubSubtitle.alignment = TextAlignmentOptions.Center;
            RectTransform hubSubRt = hubSubtitle.GetComponent<RectTransform>();
            hubSubRt.anchorMin = new Vector2(0.5f, 1f);
            hubSubRt.anchorMax = new Vector2(0.5f, 1f);
            hubSubRt.pivot = new Vector2(0.5f, 1f);
            hubSubRt.anchoredPosition = new Vector2(0f, -62f);
            hubSubRt.sizeDelta = new Vector2(1000f, 30f);

            // 3 Destination Cards
            GameObject cardsContainer = new GameObject("CardsContainer");
            cardsContainer.transform.SetParent(hubPanel.transform, false);
            RectTransform cardsRt = cardsContainer.AddComponent<RectTransform>();
            cardsRt.anchorMin = new Vector2(0.5f, 0.44f);
            cardsRt.anchorMax = new Vector2(0.5f, 0.44f);
            cardsRt.sizeDelta = new Vector2(1280f, 440f);

            HorizontalLayoutGroup cardLayout = cardsContainer.AddComponent<HorizontalLayoutGroup>();
            cardLayout.spacing = 25f;
            cardLayout.childAlignment = TextAnchor.MiddleCenter;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = true;

            // Card 1: North Block
            GameObject card1 = CreateBuildingCard("Card_NorthBlock", cardsContainer.transform, "ACADEMIC HUB", "North Block Building", "School of Engineering & Tech, Management, AI labs, and faculty suites.", "Teleport Outside →", ColorPrimary, out Button btnNorthBlock);
            // Card 2: Gateway
            GameObject card2 = CreateBuildingCard("Card_Gateway", cardsContainer.transform, "ACADEMIC & ADMIN", "Gateway Building", "University Reception, Program Office, ACIC Incubator, and Sports Gymnasium.", "Teleport Outside →", ColorNavy, out Button btnGateway);
            // Card 3: Mechanical Workshop
            GameObject card3 = CreateBuildingCard("Card_Workshop", cardsContainer.transform, "INDUSTRY & LABS", "Mechanical Workshop", "Siemens & Hero MotoCorp hangar, CNCs, 3D printing, and BAJA race bays.", "Teleport Outside →", ColorRed, out Button btnWorkshop);

            // ================================================================
            // STAGE 3: LOCATION SINGLE INFO PANEL
            // ================================================================
            GameObject locPanel = new GameObject("Stage3_LocationInfoPanel");
            locPanel.transform.SetParent(deckContainer.transform, false);
            RectTransform locPanelRt = locPanel.AddComponent<RectTransform>();
            locPanelRt.anchorMin = Vector2.zero;
            locPanelRt.anchorMax = Vector2.one;
            locPanelRt.offsetMin = new Vector2(20f, 95f);
            locPanelRt.offsetMax = new Vector2(-20f, -85f);

            // Glass Card for Location Info
            GameObject locCard = CreatePanel("LocInfoCard", locPanel.transform, Color.white, 1120f, 540f, s_SpriteGlassPanel26);
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

            TextMeshProUGUI locZone = CreateText("LocZoneTag", zonePill.transform, "ACADEMIC COMPLEX (EXTERIOR)", 11.5f, ColorRed, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            locZone.alignment = TextAlignmentOptions.Center;
            SetFullStretch(locZone.GetComponent<RectTransform>());

            // Heading (Building Name) - Left column constrained to 520px
            TextMeshProUGUI locHeading = CreateText("LocHeading", locCard.transform, "North Block Academic Building", 32f, ColorNavy, FontStyles.Bold);
            locHeading.textWrappingMode = TextWrappingModes.Normal;
            RectTransform lhRt = locHeading.GetComponent<RectTransform>();
            lhRt.anchorMin = new Vector2(0f, 1f);
            lhRt.anchorMax = new Vector2(0f, 1f);
            lhRt.pivot = new Vector2(0f, 1f);
            lhRt.anchoredPosition = new Vector2(50f, -60f);
            lhRt.sizeDelta = new Vector2(520f, 48f);

            // Summary - Left column constrained to 520px
            TextMeshProUGUI locSummary = CreateText("LocSummary", locCard.transform, "Location summary paragraph text...", 18f, ColorNavy, FontStyles.Normal, 16f);
            locSummary.textWrappingMode = TextWrappingModes.Normal;
            RectTransform lsRt = locSummary.GetComponent<RectTransform>();
            lsRt.anchorMin = new Vector2(0f, 1f);
            lsRt.anchorMax = new Vector2(0f, 1f);
            lsRt.pivot = new Vector2(0f, 1f);
            lsRt.anchoredPosition = new Vector2(50f, -144f);
            lsRt.sizeDelta = new Vector2(520f, 125f);

            // Features List - Left column constrained to 520px
            TextMeshProUGUI locFeatures = CreateText("LocFeaturesList", locCard.transform, "• Feature bullet point 1\n• Feature bullet point 2", 17f, ColorPrimary, FontStyles.Bold, 20f);
            locFeatures.textWrappingMode = TextWrappingModes.Normal;
            RectTransform lfRt = locFeatures.GetComponent<RectTransform>();
            lfRt.anchorMin = new Vector2(0f, 1f);
            lfRt.anchorMax = new Vector2(0f, 1f);
            lfRt.pivot = new Vector2(0f, 1f);
            lfRt.anchoredPosition = new Vector2(50f, -295f);
            lfRt.sizeDelta = new Vector2(520f, 140f);

            // Right Photo Card with Unsplash Photo (Width 460px with 40px clear gutter)
            GameObject photoCard = CreatePanel("PhotoCard", locCard.transform, Color.white, 460f, 430f, s_SpriteGlassPanel16);
            RectTransform pcRt = photoCard.GetComponent<RectTransform>();
            pcRt.anchorMin = new Vector2(1f, 0.5f);
            pcRt.anchorMax = new Vector2(1f, 0.5f);
            pcRt.pivot = new Vector2(1f, 0.5f);
            pcRt.anchoredPosition = new Vector2(-50f, 25f);

            GameObject locPhotoArea = CreatePanel("PhotoArea", photoCard.transform, Color.white, 430f, 310f, s_SpriteRoundedRect8);
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

            TextMeshProUGUI photoCaption = CreateText("PhotoCaption", photoCard.transform, "Front Gate & Main Academic Boulevard", 15f, ColorNavy, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            photoCaption.alignment = TextAlignmentOptions.Center;
            RectTransform capRt = photoCaption.GetComponent<RectTransform>();
            capRt.anchorMin = new Vector2(0f, 0f);
            capRt.anchorMax = new Vector2(1f, 0f);
            capRt.pivot = new Vector2(0.5f, 0f);
            capRt.anchoredPosition = new Vector2(0f, 20f);
            capRt.sizeDelta = new Vector2(-30f, 50f);

            // Footer Action Buttons
            // Left: "Go Inside" Primary Pill Button
            Button btnExploreInterior = CreateButton("BtnExploreInterior", locCard.transform, "Go Inside", ColorPrimary, 180f, 48f, 14.5f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
            RectTransform biRt = btnExploreInterior.GetComponent<RectTransform>();
            biRt.anchorMin = new Vector2(0f, 0f);
            biRt.anchorMax = new Vector2(0f, 0f);
            biRt.pivot = new Vector2(0f, 0f);
            biRt.anchoredPosition = new Vector2(50f, 35f);

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
            inRt.offsetMin = new Vector2(20f, 95f);
            inRt.offsetMax = new Vector2(-20f, -85f);

            Button btnBackPlaza = CreateButton("BtnBackToPlaza", internalPanel.transform, "← Back to Exterior Plaza", ColorAccent, 250f, 42f, 13.5f, s_SpriteGlassPill, s_FontPlusJakartaSansBold);
            RectTransform bbpRt = btnBackPlaza.GetComponent<RectTransform>();
            bbpRt.anchorMin = new Vector2(0f, 1f);
            bbpRt.anchorMax = new Vector2(0f, 1f);
            bbpRt.pivot = new Vector2(0f, 1f);
            bbpRt.anchoredPosition = new Vector2(50f, -25f);

            Button btnBackHub = CreateButton("BtnBackToCampusHub", internalPanel.transform, "Campus Hub →", ColorNavy, 170f, 42f, 13.5f, s_SpriteGlassPill, s_FontPlusJakartaSansBold);
            RectTransform bbhRt = btnBackHub.GetComponent<RectTransform>();
            bbhRt.anchorMin = new Vector2(1f, 1f);
            bbhRt.anchorMax = new Vector2(1f, 1f);
            bbhRt.pivot = new Vector2(1f, 1f);
            bbhRt.anchoredPosition = new Vector2(-50f, -25f);

            GameObject inTagPill = CreatePanel("InternalTagPill", internalPanel.transform, new Color(0f, 0.17f, 0.29f, 0.08f), 340f, 30f, s_SpriteGlassPill);
            RectTransform inTagRt = inTagPill.GetComponent<RectTransform>();
            inTagRt.anchorMin = new Vector2(0.5f, 1f);
            inTagRt.anchorMax = new Vector2(0.5f, 1f);
            inTagRt.pivot = new Vector2(0.5f, 1f);
            inTagRt.anchoredPosition = new Vector2(0f, -25f);

            TextMeshProUGUI inTag = CreateText("InternalHubTag", inTagPill.transform, "NORTH BLOCK • INTERIOR SPACES", 11.5f, ColorRed, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            inTag.alignment = TextAlignmentOptions.Center;
            SetFullStretch(inTag.GetComponent<RectTransform>());

            TextMeshProUGUI inTitle = CreateText("InternalHubTitle", internalPanel.transform, "Select an Interior Facility to Enter", 30f, ColorNavy, FontStyles.Bold);
            inTitle.alignment = TextAlignmentOptions.Center;
            RectTransform inTitRt = inTitle.GetComponent<RectTransform>();
            inTitRt.anchorMin = new Vector2(0.5f, 1f);
            inTitRt.anchorMax = new Vector2(0.5f, 1f);
            inTitRt.pivot = new Vector2(0.5f, 1f);
            inTitRt.anchoredPosition = new Vector2(0f, -62f);
            inTitRt.sizeDelta = new Vector2(800f, 40f);

            // Container for dynamic room cards
            GameObject roomCardsContainer = new GameObject("RoomCardsContainer");
            roomCardsContainer.transform.SetParent(internalPanel.transform, false);
            RectTransform rccRt = roomCardsContainer.AddComponent<RectTransform>();
            rccRt.anchorMin = new Vector2(0.5f, 0.40f);
            rccRt.anchorMax = new Vector2(0.5f, 0.40f);
            rccRt.sizeDelta = new Vector2(1280f, 380f);

            HorizontalLayoutGroup rccLayout = roomCardsContainer.AddComponent<HorizontalLayoutGroup>();
            rccLayout.spacing = 25f;
            rccLayout.childAlignment = TextAnchor.MiddleCenter;
            rccLayout.childForceExpandWidth = true;
            rccLayout.childForceExpandHeight = true;

            // Template room card
            GameObject templateCard = CreateBuildingCard("RoomCardTemplate", roomCardsContainer.transform, "BADGE", "Room Name", "Room description...", "Enter 360 Room →", ColorPrimary, out Button btnTemplate);
            templateCard.SetActive(false);

            // ================================================================
            // BOTTOM AUDIO VOICEOVER HUD
            // ================================================================
            GameObject audioHud = CreatePanel("BottomAudioVoiceoverHUD", deckContainer.transform, Color.white, 1360f, 65f, s_SpriteGlassPanel16);
            RectTransform audioRt = audioHud.GetComponent<RectTransform>();
            audioRt.anchorMin = new Vector2(0f, 0f);
            audioRt.anchorMax = new Vector2(1f, 0f);
            audioRt.pivot = new Vector2(0.5f, 0f);
            audioRt.anchoredPosition = new Vector2(0f, 15f);
            audioRt.sizeDelta = new Vector2(-40f, 65f);

            Button btnAudioToggle = CreateButton("BtnAudioToggle", audioHud.transform, "", ColorPrimary, 44f, 44f, 16f, s_SpriteGlassCircle);
            RectTransform batRt = btnAudioToggle.GetComponent<RectTransform>();
            batRt.anchorMin = new Vector2(0.02f, 0.5f);
            batRt.anchorMax = new Vector2(0.02f, 0.5f);
            batRt.pivot = new Vector2(0f, 0.5f);

            GameObject speakerOn = new GameObject("SpeakerIconOn");
            speakerOn.transform.SetParent(btnAudioToggle.transform, false);
            Image spkOnImg = speakerOn.AddComponent<Image>();
            spkOnImg.sprite = s_SpriteSpeakerOn;
            spkOnImg.color = Color.white;
            spkOnImg.preserveAspect = true;
            spkOnImg.raycastTarget = false;
            RectTransform sonRt = speakerOn.GetComponent<RectTransform>();
            sonRt.sizeDelta = new Vector2(24f, 24f);

            GameObject speakerOff = new GameObject("SpeakerIconOff");
            speakerOff.transform.SetParent(btnAudioToggle.transform, false);
            Image spkOffImg = speakerOff.AddComponent<Image>();
            spkOffImg.sprite = s_SpriteSpeakerOff;
            spkOffImg.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            spkOffImg.preserveAspect = true;
            spkOffImg.raycastTarget = false;
            speakerOff.SetActive(false);
            RectTransform soffRt = speakerOff.GetComponent<RectTransform>();
            soffRt.sizeDelta = new Vector2(24f, 24f);

            // 5 Waveform bars
            GameObject waveformContainer = new GameObject("WaveformBars");
            waveformContainer.transform.SetParent(audioHud.transform, false);
            RectTransform wcRt = waveformContainer.AddComponent<RectTransform>();
            wcRt.anchorMin = new Vector2(0.08f, 0.5f);
            wcRt.anchorMax = new Vector2(0.08f, 0.5f);
            wcRt.pivot = new Vector2(0f, 0.5f);
            wcRt.sizeDelta = new Vector2(70f, 40f);

            HorizontalLayoutGroup waveLayout = waveformContainer.AddComponent<HorizontalLayoutGroup>();
            waveLayout.spacing = 5f;
            waveLayout.childAlignment = TextAnchor.MiddleCenter;
            waveLayout.childForceExpandWidth = false;
            waveLayout.childForceExpandHeight = false;

            RectTransform[] bars = new RectTransform[5];
            for (int i = 0; i < 5; i++)
            {
                GameObject bar = new GameObject($"Bar_{i}");
                bar.transform.SetParent(waveformContainer.transform, false);
                Image barImg = bar.AddComponent<Image>();
                barImg.sprite = s_SpriteRoundedRect8;
                barImg.type = Image.Type.Sliced;
                barImg.color = ColorAccent;
                RectTransform brt = bar.GetComponent<RectTransform>();
                brt.sizeDelta = new Vector2(6f, 16f);
                bars[i] = brt;
            }

            // Caption speech bubble text: Pure black (#000000) when active as specified in Feature 2
            TextMeshProUGUI captionText = CreateText("CaptionText", audioHud.transform, "\"Welcome to BML Munjal University. Founded by the Hero Group...\"", 16f, Color.black, FontStyles.Italic);
            RectTransform ctRt = captionText.GetComponent<RectTransform>();
            ctRt.anchorMin = new Vector2(0.16f, 0.5f);
            ctRt.anchorMax = new Vector2(0.98f, 0.5f);
            ctRt.pivot = new Vector2(0f, 0.5f);
            ctRt.sizeDelta = new Vector2(0f, 45f);

            TourAudioNarrator narrator = uiRoot.AddComponent<TourAudioNarrator>();
            SetPrivateField(narrator, "m_CaptionText", captionText);
            SetPrivateField(narrator, "m_AudioToggleButton", btnAudioToggle);
            SetPrivateField(narrator, "m_ToggleButtonText", btnAudioToggle.GetComponentInChildren<TextMeshProUGUI>());
            SetPrivateField(narrator, "m_ToggleButtonImage", btnAudioToggle.GetComponent<Image>());
            SetPrivateField(narrator, "m_SpeakerIconOn", speakerOn);
            SetPrivateField(narrator, "m_SpeakerIconOff", speakerOff);
            SetPrivateField(narrator, "m_WaveformBars", bars);

            // ================================================================
            // MINIMIZED DOCK ORB
            // ================================================================
            GameObject dockOrbRoot = new GameObject("DockOrbRoot");
            dockOrbRoot.transform.SetParent(uiRoot.transform, false);
            dockOrbRoot.transform.localPosition = new Vector3(0f, -0.45f, 1.4f);

            // 3D Visual Sphere for Orb
            GameObject orbSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orbSphere.name = "DockOrbVisual";
            orbSphere.transform.SetParent(dockOrbRoot.transform, false);
            orbSphere.transform.localScale = Vector3.one * 0.22f;

            MeshRenderer orbRend = orbSphere.GetComponent<MeshRenderer>();
            orbRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orbRend.receiveShadows = false;

            string matPath = "Assets/Materials/DockOrbMaterial.mat";
            Material orbMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (orbMat == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                {
                    AssetDatabase.CreateFolder("Assets", "Materials");
                }
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                orbMat = new Material(unlit != null ? unlit : Shader.Find("Standard"));
                orbMat.color = ColorPrimary;
                AssetDatabase.CreateAsset(orbMat, matPath);
            }
            orbRend.sharedMaterial = orbMat;

            // Halo pulse ring
            GameObject pulseRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulseRing.name = "OrbPulseRing";
            pulseRing.transform.SetParent(orbSphere.transform, false);
            pulseRing.transform.localScale = new Vector3(1.3f, 0.01f, 1.3f);
            Collider pCol = pulseRing.GetComponent<Collider>();
            if (pCol != null) UnityEngine.Object.DestroyImmediate(pCol);

            MeshRenderer ringRend = pulseRing.GetComponent<MeshRenderer>();
            if (ringRend != null)
            {
                ringRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ringRend.receiveShadows = false;
                ringRend.sharedMaterial = orbMat;
            }

            // Orb Canvas with Restore Button
            GameObject orbCanvas = new GameObject("OrbCanvas");
            orbCanvas.transform.SetParent(orbSphere.transform, false);
            Canvas oc = orbCanvas.AddComponent<Canvas>();
            oc.renderMode = RenderMode.WorldSpace;
            orbCanvas.AddComponent<GraphicRaycaster>();
            orbCanvas.AddComponent<TrackedDeviceGraphicRaycaster>();
            RectTransform ocRt = orbCanvas.GetComponent<RectTransform>();
            ocRt.sizeDelta = new Vector2(300f, 300f);
            ocRt.localScale = Vector3.one * 0.001f;

            Button btnRestore = CreateButton("BtnRestoreOrb", orbCanvas.transform, "", Color.white, 250f, 250f, 16f, s_SpriteGlassCircle);
            if (s_SpriteLogo != null)
            {
                GameObject orbLogo = new GameObject("OrbLogo");
                orbLogo.transform.SetParent(btnRestore.transform, false);
                Image orbImg = orbLogo.AddComponent<Image>();
                orbImg.sprite = s_SpriteLogo;
                orbImg.preserveAspect = true;
                orbImg.raycastTarget = false;
                RectTransform olRt = orbLogo.GetComponent<RectTransform>();
                olRt.sizeDelta = new Vector2(170f, 170f);
            }

            MinimizedDockOrb dockOrb = uiRoot.AddComponent<MinimizedDockOrb>();
            SetPrivateField(dockOrb, "m_MainCanvasContainer", deckContainer);
            SetPrivateField(dockOrb, "m_DockOrbVisual", orbSphere);
            SetPrivateField(dockOrb, "m_MainCanvasGroup", mainCanvasGroup);
            SetPrivateField(dockOrb, "m_PulseRing", pulseRing.transform);

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
            SetPrivateField(tourManager, "m_InternalHubTag", inTag);
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
            return tmp;
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
            feedback.EnsureHighlightBorder();

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

            return btn;
        }

        private static GameObject CreateBuildingCard(string name, Transform parent, string badge, string title, string desc, string actionLabel, Color themeColor, out Button actionBtn)
        {
            GameObject card = CreatePanel(name, parent, Color.white, 390f, 430f, s_SpriteGlassPanel20);

            // Card Badge Pill
            GameObject badgeObj = CreatePanel("BadgePill", card.transform, new Color(1f, 1f, 1f, 0.90f), 150f, 26f, s_SpriteRoundedRect8);
            RectTransform bRt = badgeObj.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0f, 1f);
            bRt.anchorMax = new Vector2(0f, 1f);
            bRt.pivot = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(24f, -22f);

            TextMeshProUGUI badgeText = CreateText("Badge", badgeObj.transform, badge, 11f, themeColor, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            badgeText.alignment = TextAlignmentOptions.Center;
            SetFullStretch(badgeText.GetComponent<RectTransform>());

            // Card Title
            TextMeshProUGUI titleText = CreateText("Title", card.transform, title, 22f, ColorNavy, FontStyles.Bold, 0f, s_FontSpaceGroteskBold);
            RectTransform tRt = titleText.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(24f, -56f);
            tRt.sizeDelta = new Vector2(-48f, 36f);

            // Card Description
            TextMeshProUGUI descText = CreateText("Description", card.transform, desc, 15f, ColorNavy, FontStyles.Normal, 14f, s_FontPlusJakartaSansRegular);
            RectTransform dRt = descText.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0f, 1f);
            dRt.anchorMax = new Vector2(1f, 1f);
            dRt.pivot = new Vector2(0f, 1f);
            dRt.anchoredPosition = new Vector2(24f, -100f);
            dRt.sizeDelta = new Vector2(-48f, 210f);

            // Action Button
            actionBtn = CreateButton("BtnAction", card.transform, actionLabel, themeColor, 330f, 48f, 15f, s_SpriteRoundedRect12, s_FontPlusJakartaSansBold);
            RectTransform btnRt = actionBtn.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0f);
            btnRt.anchorMax = new Vector2(0.5f, 0f);
            btnRt.pivot = new Vector2(0.5f, 0f);
            btnRt.anchoredPosition = new Vector2(0f, 28f);

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

            outCaptionText = CreateText("Caption", card.transform, caption, 11f, ColorNavy, FontStyles.Bold, 0f, s_FontPlusJakartaSansBold);
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
            SetupTourSceneController();

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

            // Campus Plaza Ground Floor (140m x 140m on Layer 0; locomotion via curated anchors only)
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Campus_Terrain_And_Roads";
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(140f, 0.2f, 140f);
            ground.layer = 0;
            Material groundMat = GetOrCreateMaterial("Assets/Materials/M_CampusPlazaGround.mat", new Color(0.74f, 0.76f, 0.78f), 0.3f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            // Architecture Facades Parent
            GameObject archParent = new GameObject("Architecture_Exteriors");

            // 1. Front Gate Monument
            CreateBuildingFacade(archParent.transform, "FrontGate_Monument", "BMU MAIN GATEWAY",
                new Vector3(0f, 0f, 18f), new Vector3(16f, 7f, 3f), new Color(0.85f, 0.22f, 0.22f));

            // 2. North Block Exterior Facade (Academic Complex)
            CreateBuildingFacade(archParent.transform, "NorthBlock_Exterior_Facade", "NORTH BLOCK (ACADEMIC COMPLEX)",
                new Vector3(35f, 0f, 25f), new Vector3(24f, 9f, 16f), new Color(0.68f, 0.25f, 0.22f));

            // 3. Gateway Exterior Facade (Admin & Incubator)
            CreateBuildingFacade(archParent.transform, "Gateway_Exterior_Facade", "THE GATEWAY (ADMIN & INCUBATOR)",
                new Vector3(-35f, 0f, 25f), new Vector3(22f, 11f, 16f), new Color(0.20f, 0.38f, 0.58f));

            // 4. Workshop Exterior Facade (Heavy Engineering)
            CreateBuildingFacade(archParent.transform, "Workshop_Exterior_Facade", "MECHANICAL WORKSHOP",
                new Vector3(35f, 0f, -25f), new Vector3(22f, 8f, 18f), new Color(0.35f, 0.38f, 0.42f));

            // Reflection Probe
            GameObject probeObj = new GameObject("Campus_Exterior_ReflectionProbe");
            ReflectionProbe probe = probeObj.AddComponent<ReflectionProbe>();
            probe.size = new Vector3(150f, 40f, 150f);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;

            // Exterior Navigation Anchors (Layer 31)
            GameObject anchorsParent = new GameObject("Exterior_Navigation_Anchors");
            Color anchorPadColor = new Color(0f, 0.45f, 0.85f);

            CreateTeleportAnchor("Anchor_FrontGate", new Vector3(0f, 0f, 0f), Quaternion.identity, anchorsParent.transform, anchorPadColor);
            CreateTeleportAnchor("Anchor_NorthBlock_Plaza", new Vector3(25f, 0f, 15f), Quaternion.Euler(0f, 45f, 0f), anchorsParent.transform, anchorPadColor);
            CreateTeleportAnchor("Anchor_Gateway_Plaza", new Vector3(-25f, 0f, 15f), Quaternion.Euler(0f, -45f, 0f), anchorsParent.transform, anchorPadColor);
            CreateTeleportAnchor("Anchor_Workshop_Plaza", new Vector3(25f, 0f, -15f), Quaternion.Euler(0f, 120f, 0f), anchorsParent.transform, anchorPadColor);

            // Safety Recovery Floor underneath
            GameObject safetyFloor = new GameObject("OutOfBounds_ResetFloor");
            safetyFloor.transform.position = new Vector3(0f, -5f, 0f);
            BoxCollider safetyCol = safetyFloor.AddComponent<BoxCollider>();
            safetyCol.isTrigger = true;
            safetyCol.size = new Vector3(200f, 1f, 200f);
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

        private static GameObject SetupTourSceneController()
        {
            GameObject controllerObj = new GameObject("[Tour_Scene_Controller]");
            controllerObj.AddComponent<SceneTransitionManager>();
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
