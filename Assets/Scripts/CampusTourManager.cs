using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace BMU.VRTour
{
    /// <summary>
    /// Master VR Tour Controller for BML Munjal University Exterior Free-Roam Walkthrough.
    /// Shows the Main Gate 5-slide intro carousel (Prev / Next / Skip Intro) or the simplified
    /// exterior Location Info panel when the user enters UI_popup* / UI popup* trigger zones.
    /// </summary>
    public class CampusTourManager : MonoBehaviour
    {
        private static CampusTourManager s_Instance;
        public static CampusTourManager Instance => s_Instance;

        // ====================================================================
        // DATA MODELS
        // ====================================================================
        [System.Serializable]
        public class CarouselSlideData
        {
            public string badge;
            public string title;
            [TextArea(3, 6)] public string description;
            public string[] stats;
            public Sprite[] photos;
            public string[] photoLabels;
        }

        [System.Serializable]
        public class LocationData
        {
            public string key;
            public string name;
            public string zoneTag;
            public string heading;
            [TextArea(3, 6)] public string summary;
            public string[] features;
            public string caption;
            public string narration;
            public Sprite photo;
            public Sprite[] galleryPhotos;
        }

        // ====================================================================
        // INSPECTOR REFERENCES
        // ====================================================================
        [Header("Locomotion")]
        [SerializeField] private float m_WalkSpeed = 16.0f;

        [Header("Audio")]
        [SerializeField] private AudioClip m_AmbientClip;
        [SerializeField, Range(0f, 1f)] private float m_AmbientVolume = 0.25f;
        [SerializeField] private AudioClip m_ButtonClickClip;
        [SerializeField, Range(0f, 1f)] private float m_ButtonClickVolume = 0.85f;

        [Header("Follow & Stage Panels")]
        [SerializeField] private LazyFollowUI m_LazyFollowUI;
        [SerializeField] private GameObject m_IntroCarouselPanel;
        [SerializeField] private GameObject m_LocationInfoPanel;

        [Header("Intro Carousel Elements (Main Gate)")]
        [SerializeField] private TextMeshProUGUI m_SlideBadgeText;
        [SerializeField] private TextMeshProUGUI m_SlideTitleText;
        [SerializeField] private TextMeshProUGUI m_SlideDescText;
        [SerializeField] private TextMeshProUGUI m_SlideStatsText;
        [SerializeField] private TextMeshProUGUI[] m_SlidePointerTexts;
        [SerializeField] private TextMeshProUGUI m_SlideIndexIndicator;
        [SerializeField] private Button m_BtnPrevSlide;
        [SerializeField] private Button m_BtnNextSlide;
        [SerializeField] private Button m_BtnSkipIntro;
        [SerializeField] private Image[] m_PolaroidImages;
        [SerializeField] private TextMeshProUGUI[] m_PolaroidCaptions;

        [System.Serializable]
        public class SlidePhotoData
        {
            public Sprite[] photos = new Sprite[3];
            public string[] labels = new string[3];
        }
        [SerializeField] private SlidePhotoData[] m_SlidePhotoSets = new SlidePhotoData[5];

        [Header("Location Info Panel Elements (Exterior Buildings)")]
        [SerializeField] private TextMeshProUGUI m_LocZoneTag;
        [SerializeField] private TextMeshProUGUI m_LocHeading;
        [SerializeField] private TextMeshProUGUI m_LocSummary;
        [SerializeField] private TextMeshProUGUI m_LocFeaturesList;
        [SerializeField] private TextMeshProUGUI m_LocPhotoCaption;
        [SerializeField] private Image m_LocPhotoCardImage;
        [SerializeField] private Sprite[] m_LocationPhotoSprites = new Sprite[11];
        [SerializeField] private Sprite[] m_FoodOutletGallerySprites = new Sprite[4];

        // ====================================================================
        // STATE VARIABLES
        // ====================================================================
        private int m_CurrentSlideIndex = 0;
        private string m_CurrentActiveZoneKey = "";
        private Coroutine m_GalleryCycleRoutine;
        private AudioSource m_AmbientSource;
        private AudioSource m_UiSfxSource;
        private bool m_ConfiguredSimulatorSpeed = false;
        private XROrigin m_CachedXROrigin;
        private CharacterController m_CachedCC;

        private readonly List<CarouselSlideData> m_Slides = new List<CarouselSlideData>();
        private readonly Dictionary<string, LocationData> m_Locations = new Dictionary<string, LocationData>();

        private void Awake()
        {
            if (s_Instance == null) s_Instance = this;
            else { Destroy(gameObject); return; }

            EnsureRuntimeSafeguards();
            InitializeTourData();
            SetupAudioSources();
            HideAllPanels();
        }

        private void SetupAudioSources()
        {
            if (m_AmbientClip != null && m_AmbientSource == null)
            {
                GameObject ambObj = new GameObject("Ambient_Audio_Source");
                ambObj.transform.SetParent(transform, false);
                m_AmbientSource = ambObj.AddComponent<AudioSource>();
                m_AmbientSource.clip = m_AmbientClip;
                m_AmbientSource.loop = true;
                m_AmbientSource.playOnAwake = false;
                m_AmbientSource.spatialBlend = 0f;
                m_AmbientSource.volume = m_AmbientVolume;
                m_AmbientSource.Play();
            }

            if (m_UiSfxSource == null)
            {
                GameObject sfxObj = new GameObject("UI_SFX_Audio_Source");
                sfxObj.transform.SetParent(transform, false);
                m_UiSfxSource = sfxObj.AddComponent<AudioSource>();
                m_UiSfxSource.loop = false;
                m_UiSfxSource.playOnAwake = false;
                m_UiSfxSource.spatialBlend = 0f;
            }
        }

        public void PlayButtonClickSound()
        {
            if (m_ButtonClickClip == null) return;
            if (m_UiSfxSource == null) SetupAudioSources();
            if (m_UiSfxSource != null)
            {
                m_UiSfxSource.PlayOneShot(m_ButtonClickClip, m_ButtonClickVolume);
            }
        }

        private void Start()
        {
            EnsureRuntimeSafeguards();
            WireButtonListeners();
            HideAllPanels();
            TryConfigureXRSimulatorSpeed();
        }

        private void Update()
        {
            if (!m_ConfiguredSimulatorSpeed)
            {
                TryConfigureXRSimulatorSpeed();
            }

            HandleGamepadJoystickMovement();
        }

        private void TryConfigureXRSimulatorSpeed()
        {
            var sim = Object.FindAnyObjectByType<XRInteractionSimulator>();
            if (sim != null)
            {
                float baseSpeed = Mathf.Max(0.01f, sim.translateZSpeed);
                sim.bodyTranslateMultiplier = m_WalkSpeed / baseSpeed;
                m_ConfiguredSimulatorSpeed = true;
                Debug.Log($"[CampusTourManager] Configured XRInteractionSimulator bodyTranslateMultiplier={sim.bodyTranslateMultiplier:F1} (effective speed={m_WalkSpeed:F1} m/s).");
            }
        }

        private void HandleGamepadJoystickMovement()
        {
            if (Gamepad.current == null) return;

            Vector2 left = Gamepad.current.leftStick.ReadValue();
            Vector2 right = Gamepad.current.rightStick.ReadValue();
            Vector2 combined = Vector2.ClampMagnitude(left + right, 1f);
            if (combined.sqrMagnitude < 0.02f) return;

            if (m_CachedXROrigin == null) m_CachedXROrigin = Object.FindAnyObjectByType<XROrigin>();
            if (m_CachedXROrigin == null) return;
            if (m_CachedCC == null) m_CachedCC = m_CachedXROrigin.GetComponent<CharacterController>();

            Transform cam = m_CachedXROrigin.Camera != null ? m_CachedXROrigin.Camera.transform : Camera.main?.transform;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : m_CachedXROrigin.transform.forward;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            Vector3 rightVec = new Vector3(fwd.z, 0f, -fwd.x);

            Vector3 motion = (rightVec * combined.x + fwd * combined.y) * (m_WalkSpeed * Time.deltaTime);
            if (m_CachedCC != null && m_CachedCC.enabled)
            {
                m_CachedCC.Move(motion);
            }
            else
            {
                m_CachedXROrigin.transform.position += motion;
            }
        }

        private void EnsureRuntimeSafeguards()
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null && canvas.worldCamera == null && Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }

            var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
            foreach (var es in eventSystems)
            {
                var inputSysUI = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if (inputSysUI != null) inputSysUI.enabled = false;

                var xrui = es.GetComponent<XRUIInputModule>();
                if (xrui != null && xrui.uiCamera == null && Camera.main != null)
                {
                    xrui.uiCamera = Camera.main;
                }
            }

            // Free-roam VR: keep InputActionManager, LocomotionMediator, and ContinuousMoveProvider enabled (both sticks walk)
            foreach (var iam in Object.FindObjectsByType<InputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                iam.enabled = true;
                iam.EnableInput();
            }

            foreach (var med in Object.FindObjectsByType<LocomotionMediator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                med.gameObject.SetActive(true);
                med.enabled = true;
            }

            var moveProviders = Object.FindObjectsByType<ContinuousMoveProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mp in moveProviders)
            {
                mp.gameObject.SetActive(true);
                mp.enabled = true;
                mp.enableFly = false;
                mp.enableStrafe = true;
                mp.inAirControlModifier = 1.0f;
                mp.moveSpeed = m_WalkSpeed;
                mp.leftHandMoveInput.EnableDirectActionIfModeUsed();
                mp.rightHandMoveInput.EnableDirectActionIfModeUsed();
            }

            foreach (var tp in Object.FindObjectsByType<TeleportationProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                tp.enabled = false;
            }
            foreach (var st in Object.FindObjectsByType<SnapTurnProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                st.enabled = false;
            }
            foreach (var ct in Object.FindObjectsByType<ContinuousTurnProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ct.enabled = false;
            }

            var curveControllers = Object.FindObjectsByType<CurveVisualController>(FindObjectsSortMode.None);
            foreach (var cvc in curveControllers)
            {
                cvc.extendLineToEmptyHit = true;
                cvc.restingVisualLineLength = 10f;
                cvc.maxVisualCurveDistance = 25f;
                cvc.lineDynamicsMode = LineDynamicsMode.Traditional;

                var lr = cvc.GetComponent<LineRenderer>();
                if (lr != null) lr.enabled = true;
            }

            var nearFarInteractors = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            foreach (var nfi in nearFarInteractors)
            {
                if (nfi.GetComponent<VRPointerReticle>() == null)
                {
                    nfi.gameObject.AddComponent<VRPointerReticle>();
                }
            }

            var buttons = GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                if (btn.GetComponent<VRButtonFeedback>() == null)
                {
                    btn.gameObject.AddComponent<VRButtonFeedback>();
                }
            }
        }

        // ====================================================================
        // INITIALIZATION & EXTERIOR-ONLY CONTENT MAPPING
        // ====================================================================
        private void InitializeTourData()
        {
            // 1. Main Gate 5-Slide Intro Carousel (from ui-fixes-shrey)
            m_Slides.Add(new CarouselSlideData
            {
                badge = "HERO GROUP LEGACY",
                title = "Welcome to BML Munjal University",
                description = "Named after Shri Brijmohan Lall Munjal, the visionary founder of the Hero Group, BML Munjal University was established with a vision to transform higher education. The university emphasises ethical leadership, innovation and preparing students to make meaningful contributions to society.",
                stats = new[] { "Established in 2014", "Founded by the Hero Group", "Inspired by the Legacy of Shri Brijmohan Lall Munjal" }
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "ACADEMIC EXCELLENCE",
                title = "Achievements & Global Recognition",
                description = "BMU's commitment to academic quality is reflected in its institutional accreditations, programme assessments and national recognition. Its focus on academic rigour, research and industry engagement continues to strengthen its position in higher education.",
                stats = new[] { "NAAC A Grade Accreditation", "QS I-GAUGE Diamond Rating", "77th in NIRF 2025 – Management Category" }
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "CURRICULUM & SCHOOLS",
                title = "Schools & Experiential Learning",
                description = "BMU promotes an interdisciplinary approach to education, encouraging students to connect concepts across different fields of study. Learning extends beyond textbooks through practical assignments, industry interactions, research and opportunities to apply classroom knowledge to real-world challenges.",
                stats = new[] { "School of Engineering & Technology", "School of Management, Law & Liberal Studies", "Undergraduate, Postgraduate & Doctoral Programmes" }
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "INFRASTRUCTURE & CAMPUS",
                title = "A 50-Acre Campus for Learning & Discovery",
                description = "Designed to support academic, residential and recreational activities, BMU's campus brings together spaces for learning, research, collaboration and student interaction. Its environment encourages students to explore their interests while balancing academics with personal development and campus life.",
                stats = new[] { "Dedicated Academic & Innovation Complexes", "Residential Hostels & Student Amenities", "Sports, Recreation & Collaborative Plazas" }
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "STUDENT LIFE & CULTURE",
                title = "Vibrant Campus Life, Events & Sports",
                description = "BMU's campus comes alive through the annual 67th Milestone festival, TEDxBMU, hackathons, and cultural events. With 24+ student clubs, outreach drives, and inter-university sports tournaments, students enjoy dynamic teamwork and vibrant campus life.",
                stats = new[] { "24 Student-Run Clubs & Societies", "67th Milestone – 50+ Colleges Participated in 2026", "Basketball, Tennis, Volleyball & Outdoor Grounds" }
            });

            // 2. Exterior Building Locations (Hostels/ primary source + ui-fixes-shrey fallback; NO interior/room-level info)
            m_Locations["front-gate"] = new LocationData
            {
                key = "front-gate",
                name = "Front Gate & Main Entrance",
                zoneTag = "CAMPUS ENTRYWAY (EXTERIOR)",
                heading = "BML Munjal University Front Gate",
                summary = "The grand entrance to BMU welcomes visitors into a 50-acre modern academic campus designed for innovation, experiential learning, and holistic student life.",
                features = new[] { "• Hero Group Foundation Arch", "• Landscaped Central Boulevard", "• 24/7 Campus Security Entryway" },
                caption = "Front Gate & Main Academic Boulevard",
                narration = "Standing at the front gate, visitors can sense the grandeur and energy of BMU's 50-acre campus."
            };

            m_Locations["outside-north-block"] = new LocationData
            {
                key = "outside-north-block",
                name = "North Block Building",
                zoneTag = "ACADEMIC COMPLEX (EXTERIOR)",
                heading = "North Block Academic Building",
                summary = "The North Block is the premier academic complex for the School of Engineering & Technology and School of Management, anchored by its signature red-brick exterior and central plaza.",
                features = new[] { "• School of Engineering & Technology", "• School of Management", "• Red-Brick Academic Façade", "• Central Student Plaza" },
                caption = "North Block Red-Brick Academic Façade",
                narration = "You are outside the North Block, the academic heartbeat of BMU."
            };

            m_Locations["outside-gateway"] = new LocationData
            {
                key = "outside-gateway",
                name = "Gateway Building",
                zoneTag = "ADMINISTRATIVE & STUDENT HUB (EXTERIOR)",
                heading = "The Gateway Monumental Complex",
                summary = "The Gateway is BMU's iconic architectural centerpiece along the central boulevard, anchoring the university's administrative core, student reception, innovation hub, and campus courtyard.",
                features = new[] { "• Iconic Monumental Central Arch", "• University Administrative Core", "• ACIC Innovation & Startup Hub", "• Landscaped Gateway Courtyard" },
                caption = "The Monumental Gateway Central Arch & Courtyard",
                narration = "The Gateway building stands as BMU's iconic architectural and administrative centerpiece."
            };

            m_Locations["outside-workshop"] = new LocationData
            {
                key = "outside-workshop",
                name = "Mechanical Workshop",
                zoneTag = "EXPERIENTIAL INDUSTRIAL FACILITY (EXTERIOR)",
                heading = "Mechanical Workshop & Engineering Hangar",
                summary = "A dedicated heavy-engineering building showcasing BMU's industry alliances with Siemens and Hero MotoCorp, supporting hands-on manufacturing, robotics, and student motorsports.",
                features = new[] { "• Siemens & Hero MotoCorp Partnerships", "• Heavy Engineering Hangar Exterior", "• Formula Student & SAE BAJA Hub", "• Direct Service & Logistics Apron" },
                caption = "Mechanical Workshop & Industrial Hangar Exterior",
                narration = "Outside the Mechanical Workshop, where students build industrial engineering projects and student racecars."
            };

            // Primary source: Hostels/scripts.docx & Hostels/Gargi/gargi_apt.png (exterior-only scope)
            m_Locations["outside-gargi"] = new LocationData
            {
                key = "outside-gargi",
                name = "Gargi Hostel",
                zoneTag = "GIRLS' RESIDENCE (EXTERIOR)",
                heading = "Gargi Hostel",
                summary = "Welcome to Gargi Hostel, a dedicated residential facility for female students at BMU. Designed with open balcony fronts, landscaped pathways, and secure controlled access, Gargi Hostel brings comfort and convenience to campus living.",
                features = new[] { "• Dedicated Girls' Residential Complex", "• Open Balconies with Campus Views", "• Controlled Entry & 24/7 Campus Security", "• Easy Access to Dining & Academic Blocks" },
                caption = "Gargi Hostel Exterior Facade & Courtyard",
                narration = "Welcome to Gargi Hostel, a dedicated residential facility for female students bringing comfort and convenience to campus life."
            };

            // Primary source: Hostels/scripts.docx & Hostels/Maggi Point/ (exterior-only scope)
            m_Locations["outside-food-outlets"] = new LocationData
            {
                key = "outside-food-outlets",
                name = "Campus Food Outlets",
                zoneTag = "GATEWAY–NORTH BLOCK PATHWAY (EXTERIOR)",
                heading = "Campus Food Outlets",
                summary = "Located along the vibrant walkway between the Gateway Building and North Block, this stretch features Burger Singh, Domino's, Maggi Point, and Café Coffee Day Xpress — giving students convenient outdoor spots to grab a bite and recharge between classes.",
                features = new[] { "• Burger Singh & Domino's Pizza", "• Maggi Point (Wraps, Maggi & Refreshments)", "• Café Coffee Day Xpress Beverage Stop", "• Gateway–North Block Social Plaza" },
                caption = "Campus Food Outlets Along Gateway–North Block Pathway",
                narration = "Located between the Gateway Building and North Block, this stretch of campus features Burger Singh, Domino's, Maggi Point, and Café Coffee Day Xpress."
            };

            // Exterior-only summaries for remaining scene trigger zones
            m_Locations["outside-vsh"] = new LocationData
            {
                key = "outside-vsh",
                name = "Vikram Sarabhai Hostel",
                zoneTag = "STUDENT RESIDENCE (EXTERIOR)",
                heading = "Vikram Sarabhai Hostel (VSH)",
                summary = "Part of BMU's residential hostel sector, Vikram Sarabhai Hostel stands beside landscaped student courtyards with direct walkways connecting to the sports grounds, dining areas, and academic complexes.",
                features = new[] { "• Residential Hostel Complex", "• Landscaped Courtyard & Walkways", "• Close to Sports & Recreation Grounds", "• 24/7 Secured Residential Zone" },
                caption = "Vikram Sarabhai Hostel Exterior",
                narration = "You are outside Vikram Sarabhai Hostel in BMU's residential campus sector, connected to student courtyards and sports grounds."
            };

            m_Locations["outside-bsh"] = new LocationData
            {
                key = "outside-bsh",
                name = "Bhagat Singh Hostel",
                zoneTag = "STUDENT RESIDENCE (EXTERIOR)",
                heading = "Bhagat Singh Hostel (BSH)",
                summary = "Situated in the campus residential zone, Bhagat Singh Hostel provides a vibrant student residential community with pedestrian paths linking directly to campus dining, recreation areas, and academic plazas.",
                features = new[] { "• Multi-Wing Student Residence", "• Central Pedestrian Pathways", "• Adjacent to Campus Amenities & Dining", "• Secured Residential Campus Sector" },
                caption = "Bhagat Singh Hostel Exterior",
                narration = "You are outside Bhagat Singh Hostel, part of BMU's vibrant residential community with walkways linking to campus dining and plazas."
            };

            m_Locations["outside-library"] = new LocationData
            {
                key = "outside-library",
                name = "Library & Medical Centre",
                zoneTag = "ACADEMIC & WELLNESS HUB (EXTERIOR)",
                heading = "Central Library & Medical Centre",
                summary = "Positioned along the main campus corridor near the Gateway complex, this building anchors BMU's Central Library and campus Medical Centre, serving as a quiet academic and wellness landmark.",
                features = new[] { "• Central University Library Building", "• On-Campus Medical & Wellness Centre", "• Gateway Corridor Pedestrian Plaza", "• Accessible Student Support Landmark" },
                caption = "Central Library & Medical Centre Exterior",
                narration = "You are outside BMU's Central Library and Medical Centre, a key academic and student wellness landmark along the Gateway corridor."
            };

            // Additional Hostels/ entries ready if triggers are placed for KCH or APJ (D)
            m_Locations["outside-kch"] = new LocationData
            {
                key = "outside-kch",
                name = "Kalpana Chawla Hostel",
                zoneTag = "GIRLS' RESIDENCE (EXTERIOR)",
                heading = "Kalpana Chawla Hostel (KCH)",
                summary = "Kalpana Chawla Hostel is a dedicated residential facility for female students, featuring end-to-end balconies, an attached tuck shop, and a dedicated security system ensuring a safe, welcoming residential campus environment.",
                features = new[] { "• Girls' Residential Tower", "• Balcony Spaces at Both Ends", "• Attached Campus Tuck Shop", "• Dedicated Controlled Entry Security" },
                caption = "Kalpana Chawla Hostel Exterior Tower",
                narration = ""
            };

            m_Locations["outside-apj"] = new LocationData
            {
                key = "outside-apj",
                name = "APJ Abdul Kalam Hostel",
                zoneTag = "GIRLS' RESIDENCE (EXTERIOR)",
                heading = "APJ Abdul Kalam Hostel",
                summary = "A dedicated girls' residential facility at BML Munjal University featuring open balconies, landscaped surroundings, and convenient access to on-site dining and everyday student amenities.",
                features = new[] { "• Dedicated Girls' Residence", "• Open Outdoor Balconies", "• Ground-Level Dining & Mess Access", "• Safe Landscaped Residential Zone" },
                caption = "APJ Abdul Kalam Hostel Exterior",
                narration = ""
            };

            // Bind Slide Photos
            for (int i = 0; i < m_Slides.Count; i++)
            {
                if (m_SlidePhotoSets != null && i < m_SlidePhotoSets.Length && m_SlidePhotoSets[i] != null)
                {
                    m_Slides[i].photos = m_SlidePhotoSets[i].photos;
                    m_Slides[i].photoLabels = m_SlidePhotoSets[i].labels;
                }
            }

            // Bind Exterior Location Photos (index-aligned with BMUTourSceneBuilder.LoadLocationPhotos)
            string[] locKeys = new string[]
            {
                "front-gate",
                "outside-north-block",
                "outside-gateway",
                "outside-workshop",
                "outside-gargi",
                "outside-food-outlets",
                "outside-vsh",
                "outside-bsh",
                "outside-library",
                "outside-kch",
                "outside-apj"
            };

            for (int i = 0; i < locKeys.Length; i++)
            {
                if (m_LocationPhotoSprites != null && i < m_LocationPhotoSprites.Length && m_LocationPhotoSprites[i] != null)
                {
                    if (m_Locations.TryGetValue(locKeys[i], out LocationData ld))
                    {
                        ld.photo = m_LocationPhotoSprites[i];
                    }
                }
            }

            if (m_Locations.TryGetValue("outside-food-outlets", out LocationData foodData) && m_FoodOutletGallerySprites != null)
            {
                foodData.galleryPhotos = m_FoodOutletGallerySprites;
            }
        }

        private void WireButtonListeners()
        {
            if (m_BtnPrevSlide != null) m_BtnPrevSlide.onClick.AddListener(PreviousSlide);
            if (m_BtnNextSlide != null) m_BtnNextSlide.onClick.AddListener(NextSlide);
            if (m_BtnSkipIntro != null) m_BtnSkipIntro.onClick.AddListener(() => HideAllPanels());
        }

        // ====================================================================
        // TRIGGER ZONE PUBLIC API
        // ====================================================================
        public void ShowMainGateIntro()
        {
            StopGalleryCycle();
            m_CurrentActiveZoneKey = "main-gate";
            m_CurrentSlideIndex = 0;

            ActivateStage(m_IntroCarouselPanel);
            RecenterUI();
            UpdateCarouselDisplay();
        }

        public void ShowExteriorZone(string locKey)
        {
            if (!m_Locations.TryGetValue(locKey, out LocationData data))
            {
                Debug.LogWarning($"[CampusTourManager] Unknown exterior location key: '{locKey}'");
                return;
            }

            StopGalleryCycle();
            m_CurrentActiveZoneKey = locKey;

            ActivateStage(m_LocationInfoPanel);
            RecenterUI();

            if (m_LocZoneTag != null) m_LocZoneTag.text = data.zoneTag;
            if (m_LocHeading != null) m_LocHeading.text = data.heading;
            if (m_LocSummary != null) m_LocSummary.text = data.summary;
            if (m_LocFeaturesList != null && data.features != null)
            {
                m_LocFeaturesList.text = string.Join("\n", data.features);
            }
            if (m_LocPhotoCaption != null) m_LocPhotoCaption.text = data.caption;

            if (m_LocPhotoCardImage != null && data.photo != null)
            {
                m_LocPhotoCardImage.sprite = data.photo;
                m_LocPhotoCardImage.preserveAspect = true;
                m_LocPhotoCardImage.color = Color.white;
            }

            if (data.galleryPhotos != null && data.galleryPhotos.Length > 1)
            {
                m_GalleryCycleRoutine = StartCoroutine(CycleGalleryPhotosRoutine(data.galleryPhotos));
            }

            PlayVoice(m_LocationInfoPanel, locKey, data.narration);
        }

        public void HideAllPanels(string expectedKey = null)
        {
            if (!string.IsNullOrEmpty(expectedKey) && m_CurrentActiveZoneKey != expectedKey)
            {
                return;
            }

            StopGalleryCycle();
            m_CurrentActiveZoneKey = "";
            ActivateStage(null);

            if (TourAudioNarrator.Instance != null)
            {
                TourAudioNarrator.Instance.StopNarration();
            }
        }

        // ====================================================================
        // MAIN GATE CAROUSEL NAVIGATION
        // ====================================================================
        public void NextSlide()
        {
            if (m_CurrentSlideIndex < m_Slides.Count - 1)
            {
                m_CurrentSlideIndex++;
                UpdateCarouselDisplay();
            }
            else
            {
                HideAllPanels();
            }
        }

        public void PreviousSlide()
        {
            if (m_CurrentSlideIndex > 0)
            {
                m_CurrentSlideIndex--;
                UpdateCarouselDisplay();
            }
        }

        private void UpdateCarouselDisplay()
        {
            if (m_Slides == null || m_Slides.Count == 0) return;
            CarouselSlideData slide = m_Slides[m_CurrentSlideIndex];
            PlayVoice(m_IntroCarouselPanel, $"slide-{m_CurrentSlideIndex}", slide.title);

            if (m_SlideBadgeText != null)
            {
                m_SlideBadgeText.text = slide.badge;
                m_SlideBadgeText.color = new Color(0.01f, 0.52f, 0.78f);
            }
            if (m_SlideTitleText != null) m_SlideTitleText.text = slide.title;
            if (m_SlideDescText != null) m_SlideDescText.text = slide.description;

            if (m_SlidePointerTexts != null && m_SlidePointerTexts.Length > 0 && slide.stats != null)
            {
                for (int i = 0; i < m_SlidePointerTexts.Length; i++)
                {
                    if (m_SlidePointerTexts[i] == null) continue;
                    if (i < slide.stats.Length && !string.IsNullOrEmpty(slide.stats[i]))
                    {
                        var parentObj = m_SlidePointerTexts[i].transform.parent.gameObject;
                        parentObj.SetActive(true);
                        m_SlidePointerTexts[i].text = slide.stats[i].TrimStart('•', ' ', '-');

                        var tagRt = parentObj.GetComponent<RectTransform>();
                        if (tagRt != null) tagRt.pivot = new Vector2(0f, 0.5f);
                    }
                    else
                    {
                        m_SlidePointerTexts[i].transform.parent.gameObject.SetActive(false);
                    }
                }

                if (m_SlidePointerTexts[0] != null && m_SlidePointerTexts[0].transform.parent != null)
                {
                    var containerRt = m_SlidePointerTexts[0].transform.parent.parent as RectTransform;
                    if (containerRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(containerRt);
                }
            }
            else if (m_SlideStatsText != null && slide.stats != null)
            {
                m_SlideStatsText.text = string.Join("\n", slide.stats);
            }

            if (m_SlideIndexIndicator != null)
            {
                m_SlideIndexIndicator.text = $"SLIDE {m_CurrentSlideIndex + 1} OF {m_Slides.Count}";
            }

            if (m_PolaroidImages != null && slide.photos != null)
            {
                for (int i = 0; i < m_PolaroidImages.Length; i++)
                {
                    if (m_PolaroidImages[i] != null && i < slide.photos.Length && slide.photos[i] != null)
                    {
                        m_PolaroidImages[i].sprite = slide.photos[i];
                        m_PolaroidImages[i].color = Color.white;
                    }
                }
            }

            if (m_PolaroidCaptions != null && slide.photoLabels != null)
            {
                for (int i = 0; i < m_PolaroidCaptions.Length; i++)
                {
                    if (m_PolaroidCaptions[i] != null && i < slide.photoLabels.Length)
                    {
                        m_PolaroidCaptions[i].text = slide.photoLabels[i];
                    }
                }
            }
        }

        // ====================================================================
        // HELPERS
        // ====================================================================
        private void ActivateStage(GameObject activePanel)
        {
            if (m_IntroCarouselPanel != null) m_IntroCarouselPanel.SetActive(m_IntroCarouselPanel == activePanel);
            if (m_LocationInfoPanel != null) m_LocationInfoPanel.SetActive(m_LocationInfoPanel == activePanel);
        }

        private void RecenterUI()
        {
            if (m_LazyFollowUI == null) m_LazyFollowUI = GetComponent<LazyFollowUI>();
            if (m_LazyFollowUI != null) m_LazyFollowUI.RecenterToCurrentGaze();
        }

        private void PlayVoice(GameObject panel, string key, string caption)
        {
            if (TourAudioNarrator.Instance == null) return;
            AudioClip clip = null;
            float delay = 0.3f;
            PanelVoiceover pv = panel != null ? panel.GetComponent<PanelVoiceover>() : null;
            if (pv != null) pv.TryGet(key, out clip, out delay);
            TourAudioNarrator.Instance.PlayNarration(caption, clip, delay);
        }

        private void StopGalleryCycle()
        {
            if (m_GalleryCycleRoutine != null)
            {
                StopCoroutine(m_GalleryCycleRoutine);
                m_GalleryCycleRoutine = null;
            }
        }

        private IEnumerator CycleGalleryPhotosRoutine(Sprite[] photos)
        {
            var valid = new List<Sprite>();
            foreach (var s in photos)
            {
                if (s != null) valid.Add(s);
            }
            if (valid.Count <= 1 || m_LocPhotoCardImage == null) yield break;

            int idx = 0;
            while (true)
            {
                yield return new WaitForSeconds(4.0f);
                idx = (idx + 1) % valid.Count;
                if (m_LocPhotoCardImage != null)
                {
                    m_LocPhotoCardImage.sprite = valid[idx];
                    m_LocPhotoCardImage.preserveAspect = true;
                    m_LocPhotoCardImage.color = Color.white;
                }
            }
        }

        /// <summary>Assigns a sprite to an Image inside a masked container and fill-crops it.</summary>
        public static void ApplyFillCropSprite(Image img, Sprite sprite)
        {
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = false;
            var fitter = img.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = img.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
        }
    }
}
