using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace BMU.VRTour
{
    /// <summary>
    /// Master VR Tour Controller for BML Munjal University Campus Walkthrough.
    /// Manages the tour state machine, 3D carousel, destination selector, interior hubs,
    /// location info panels, VR transitions, and thumbstick navigation.
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
            public string caption;
            public string narration;
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
            public bool hasInterior;
            public string buildingKey; // e.g. "north-block", "gateway", "workshop"
            public Sprite photo;
        }

        [System.Serializable]
        public class RoomData
        {
            public string key;
            public string buildingKey;
            public string name;
            public string badge;
            [TextArea(2, 4)] public string description;
            public string[] highlights;
            public string narration;
            public Sprite photo;
        }

        // ====================================================================
        // INSPECTOR REFERENCES
        // ====================================================================
        [Header("Header HUD")]
        [SerializeField] private TextMeshProUGUI m_HeaderLocationText;
        [SerializeField] private Button m_BtnResetTour;
        [SerializeField] private Button m_BtnRecenterUI;
        [SerializeField] private LazyFollowUI m_LazyFollowUI;

        [Header("Stage Panels")]
        [SerializeField] private GameObject m_IntroCarouselPanel;
        [SerializeField] private GameObject m_CampusHubPanel;
        [SerializeField] private GameObject m_LocationInfoPanel;
        [SerializeField] private GameObject m_InternalBuildingHubPanel;

        [Header("Intro Carousel Elements")]
        [SerializeField] private TextMeshProUGUI m_SlideBadgeText;
        [SerializeField] private TextMeshProUGUI m_SlideTitleText;
        [SerializeField] private TextMeshProUGUI m_SlideDescText;
        [SerializeField] private TextMeshProUGUI m_SlideStatsText;
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

        [Header("Campus Hub Elements")]
        [SerializeField] private Button m_BtnSelectNorthBlock;
        [SerializeField] private Button m_BtnSelectGateway;
        [SerializeField] private Button m_BtnSelectWorkshop;

        [Header("Location Info Panel Elements")]
        [SerializeField] private TextMeshProUGUI m_LocZoneTag;
        [SerializeField] private TextMeshProUGUI m_LocHeading;
        [SerializeField] private TextMeshProUGUI m_LocSummary;
        [SerializeField] private TextMeshProUGUI m_LocFeaturesList;
        [SerializeField] private TextMeshProUGUI m_LocPhotoCaption;
        [SerializeField] private Image m_LocPhotoCardImage;
        [SerializeField] private Button m_BtnExploreInterior;
        [SerializeField] private Button m_BtnNextLocation;
        [SerializeField] private Button m_BtnCampusHubReturn;
        [SerializeField] private Button m_BtnMinimize;
        [SerializeField] private Sprite[] m_LocationPhotoSprites = new Sprite[9];

        [Header("Internal Building Hub Elements")]
        [SerializeField] private TextMeshProUGUI m_InternalHubTag;
        [SerializeField] private TextMeshProUGUI m_InternalHubTitle;
        [SerializeField] private Button m_BtnBackToPlaza;
        [SerializeField] private Button m_BtnBackToCampusHub;
        [SerializeField] private Transform m_RoomCardsContainer;
        [SerializeField] private GameObject m_RoomCardTemplate;

        [Header("Minimized Dock Orb")]
        [SerializeField] private MinimizedDockOrb m_DockOrb;
        [SerializeField] private Button m_BtnRestoreOrb;

        [Header("Thumbstick Input")]
        [SerializeField] private float m_ThumbstickThreshold = 0.65f;
        [SerializeField] private float m_ThumbstickCooldown = 0.35f;

        // ====================================================================
        // STATE VARIABLES
        // ====================================================================
        private int m_CurrentSlideIndex = 0;
        private string m_CurrentLocationKey = "front-gate";
        private string m_CurrentBuildingContext = "north-block";
        private float m_LastThumbstickTime = 0f;

        private List<CarouselSlideData> m_Slides = new List<CarouselSlideData>();
        private Dictionary<string, LocationData> m_Locations = new Dictionary<string, LocationData>();
        private List<RoomData> m_Rooms = new List<RoomData>();

        private void Awake()
        {
            if (s_Instance == null) s_Instance = this;
            else { Destroy(gameObject); return; }

            EnsureRuntimeSafeguards();
            InitializeTourData();
        }

        private void Start()
        {
            EnsureRuntimeSafeguards();
            WireButtonListeners();
            StartTourFromBeginning();
        }

        private void EnsureRuntimeSafeguards()
        {
            // 1. Ensure worldCamera is bound on Canvas (which automatically sets raycaster.eventCamera)
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null && canvas.worldCamera == null && Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }

            // 2. Ensure XRUIInputModule is configured and conflicting InputSystemUIInputModule is disabled
            var eventSystems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
            foreach (var es in eventSystems)
            {
                var inputSysUI = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if (inputSysUI != null)
                {
                    inputSysUI.enabled = false;
                }

                var xrui = es.GetComponent<XRUIInputModule>();
                if (xrui != null)
                {
                    if (xrui.uiCamera == null && Camera.main != null)
                    {
                        xrui.uiCamera = Camera.main;
                    }
                }
            }

            // 3. Ensure gravity and continuous movement cannot pull player into void
            var moveProviders = UnityEngine.Object.FindObjectsByType<ContinuousMoveProvider>(FindObjectsSortMode.None);
            foreach (var mp in moveProviders)
            {
                mp.useGravity = false;
                mp.enableFly = false;
                mp.enabled = false;
            }

            // 4. Ensure light pointer line visuals stay extended
            var curveControllers = UnityEngine.Object.FindObjectsByType<CurveVisualController>(FindObjectsSortMode.None);
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

            // 5. Ensure ground floor exists at Y=0 (Physics boundary; locomotion handled via TeleportationAnchor)
            GameObject floor = GameObject.Find("[Campus_Floor]");
            if (floor == null)
            {
                floor = new GameObject("[Campus_Floor]");
                floor.transform.position = Vector3.zero;
                floor.layer = 0;
                BoxCollider col = floor.AddComponent<BoxCollider>();
                col.size = new Vector3(100f, 0.2f, 100f);
                col.center = new Vector3(0f, -0.1f, 0f);
            }

            // 6. Ensure VRPointerReticle (small blue circle) is attached to all interactors
            var nearFarInteractors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            foreach (var nfi in nearFarInteractors)
            {
                if (nfi.GetComponent<VRPointerReticle>() == null)
                {
                    nfi.gameObject.AddComponent<VRPointerReticle>();
                }
            }

            // 7. Ensure VRButtonFeedback (glowing outline, hover scale & feedback) is on all UI buttons
            var buttons = GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                if (btn.GetComponent<VRButtonFeedback>() == null)
                {
                    btn.gameObject.AddComponent<VRButtonFeedback>();
                }
            }

            // 8. Ensure VRUIDraggableHandle is attached to TopDragHandle
            var dragHandleObj = GameObject.Find("TopDragHandle");
            if (dragHandleObj != null && dragHandleObj.GetComponent<VRUIDraggableHandle>() == null)
            {
                dragHandleObj.AddComponent<VRUIDraggableHandle>();
            }
        }

        private void Update()
        {
            HandleThumbstickInput();
        }

        // ====================================================================
        // INITIALIZATION & DATA DEFINITION
        // ====================================================================
        private void InitializeTourData()
        {
            // 5-Slide Intro Carousel (matching Figma prototype)
            m_Slides.Add(new CarouselSlideData
            {
                badge = "HERO GROUP LEGACY",
                title = "Welcome to BML Munjal University",
                description = "Named after the visionary founder of the Hero Group, Shri Brijmohan Lall Munjal, BMU was established to transform higher education through hands-on experiential learning, ethical leadership, and real-world industrial impact.",
                stats = new[] { "• Founded by Hero Group", "• Mentored by Imperial College London", "• Experiential 3-2-1 Curriculum" },
                caption = "Hero Legacy & Main Campus Gate",
                narration = "Welcome to BML Munjal University. Founded by the Hero Group, BMU nurtures ethical leaders equipped with experiential knowledge."
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "ACADEMIC EXCELLENCE",
                title = "Achievements & Global Accreditations",
                description = "BMU is ranked among the top 100 universities nationally in the NIRF rankings. With a QS I-GAUGE Diamond rating, patent programs, and academic collaboration with Imperial College London, our graduates enter top global corporations.",
                stats = new[] { "• Top 100 NIRF Ranked", "• QS I-GAUGE Diamond Rated", "• 100+ Corporate Recruiting Partners" },
                caption = "Honoring Innovation & Convocation",
                narration = "Ranked among India's top 100 by NIRF, BMU delivers globally benchmarked academic rigor and prestigious international partnerships."
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "CURRICULUM & SCHOOLS",
                title = "Schools & Experiential Programs",
                description = "Our schools of Engineering & Technology, Management, Law, and Liberal Studies emphasize interdisciplinary learning. 45% of student time is dedicated to hands-on lab work, maker studios, live venture creation, and internships.",
                stats = new[] { "• SOET • SOM • SOL • SOLS", "• 45% Practical Hands-on Labs", "• B.Tech • MBA • Law • PhD" },
                caption = "Experiential Learning & Studios",
                narration = "Through the School of Engineering, Management, and Law, students spend over 45% of their curriculum in live, practical laboratories."
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "INFRASTRUCTURE & LABS",
                title = "World-Class 50-Acre Campus",
                description = "A fully residential, green, and sustainable campus equipped with cutting-edge academic spaces, dedicated Maker's spaces, IoT and Siemens engineering labs, campus-wide Wi-Fi, and 24/7 student activity hubs.",
                stats = new[] { "• 50-Acre Green Campus", "• Siemens Centre of Excellence", "• 100% Residential Community" },
                caption = "Sustainable Green Living & High-Tech Labs",
                narration = "Our 50-acre green residential campus provides cutting-edge research facilities, modern student residences, and continuous mentorship."
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "STUDENT LIFE & CULTURE",
                title = "Vibrant Campus Life, Fests & Sports",
                description = "Life at BMU thrives beyond academics. With over 25 student clubs, the flagship annual cultural fest '67th Milestone', national hackathons, and world-class athletic facilities, students develop holistic character.",
                stats = new[] { "• '67th Milestone' Annual Fest", "• 25+ Student-Run Clubs", "• Olympic-Grade Sports Complex" },
                caption = "Fests, Athletics & Student Clubs",
                narration = "From our signature festival 67th Milestone to 25+ student-led clubs and sports, campus life at BMU is full of energy and creativity."
            });

            // Tour Locations (Exterior Plazas)
            m_Locations["front-gate"] = new LocationData
            {
                key = "front-gate",
                name = "Front Gate & Main Entrance",
                zoneTag = "CAMPUS ENTRYWAY",
                heading = "BML Munjal University Front Gate",
                summary = "The grand entrance to BMU welcomes visitors into a 50-acre sprawling, modern academic ecosystem designed for innovation, experiential learning, and holistic student development.",
                features = new[] { "• Hero Group Foundation Arch", "• Landscaped Central Boulevard", "• 24/7 Security Command Center" },
                caption = "Front Gate & Main Academic Boulevard",
                narration = "Standing at the front gate, visitors can sense the grandeur and energy of BMU's 50-acre campus.",
                hasInterior = false,
                buildingKey = "none"
            };

            m_Locations["outside-north-block"] = new LocationData
            {
                key = "outside-north-block",
                name = "Outside North Block Building",
                zoneTag = "ACADEMIC COMPLEX (EXTERIOR)",
                heading = "North Block Academic Building",
                summary = "The North Block is the premier academic complex for the School of Engineering & Technology and School of Management. Its modern red-brick architecture houses multi-tiered smart classrooms, computational labs, and faculty research suites.",
                features = new[] { "• School of Engineering & Tech", "• School of Management", "• Faculty Research Suites", "• Smart Pedagogy Halls" },
                caption = "North Block Red-Brick Academic Façade",
                narration = "You are outside the North Block, the academic heartbeat of BMU housing engineering, management, and faculty cabins.",
                hasInterior = true,
                buildingKey = "north-block"
            };

            m_Locations["outside-gateway"] = new LocationData
            {
                key = "outside-gateway",
                name = "Outside Gateway Building",
                zoneTag = "ADMINISTRATIVE & STUDENT HUB",
                heading = "The Gateway Monumental Complex",
                summary = "The Gateway is BMU's iconic architectural centerpiece, anchoring the university's administrative core, program offices, student reception, ACIC startup incubation center, fitness gymnasium, and conference hubs.",
                features = new[] { "• University Reception & Dean's Office", "• ACIC Startup Incubator (NITI Aayog)", "• State-of-the-Art Fitness Center", "• Executive Meeting Hubs" },
                caption = "The Monumental Gateway Central Arch & Courtyard",
                narration = "The Gateway building stands as BMU's administrative center, housing our incubator, executive offices, and student fitness centers.",
                hasInterior = true,
                buildingKey = "gateway"
            };

            m_Locations["outside-workshop"] = new LocationData
            {
                key = "outside-workshop",
                name = "Outside Mechanical Workshop",
                zoneTag = "EXPERIENTIAL INDUSTRIAL FACILITY",
                heading = "Mechanical Workshop & Vehicle Hangar",
                summary = "A heavy-engineering industrial facility showcasing BMU's industry alliances with Siemens and Hero MotoCorp. Houses heavy industrial machinery, 3D printing labs, and the workshop where students build Formula Student racecars and all-terrain BAJA buggies.",
                features = new[] { "• Siemens & Hero MotoCorp Partnerships", "• Industrial CNC & Lathe Machinery", "• Additive Manufacturing 3D Printing Lab", "• Formula Student & SAE BAJA Race Bays" },
                caption = "Heavy Machinery, 3D Printing & Motorsports Garage",
                narration = "The Mechanical Workshop houses industrial CNCs, 3D printing equipment, and the student garage for Formula and BAJA racing vehicles.",
                hasInterior = false,
                buildingKey = "workshop"
            };

            // Interior Rooms
            m_Rooms.Add(new RoomData
            {
                key = "nb-classroom",
                buildingKey = "north-block",
                name = "Smart Tiered Classroom",
                badge = "INTERACTIVE PEDAGOGY",
                description = "Tiered amphitheater seating with dual smartboards, acoustic treatment, and hybrid broadcast cameras for collaborative lectures.",
                highlights = new[] { "• Tiered Amphitheater Seating", "• Dual Interactive Smartboards", "• Lecture Capture Studio" },
                narration = "Inside the North Block smart classroom, amphitheater seating and multimedia displays encourage dynamic faculty-student discussions."
            });

            m_Rooms.Add(new RoomData
            {
                key = "nb-lab",
                buildingKey = "north-block",
                name = "AI & Robotics Laboratory",
                badge = "HIGH-PERFORMANCE COMPUTING",
                description = "Equipped with high-performance GPU workstations, robotics testing tracks, and embedded hardware benches for student research.",
                highlights = new[] { "• NVIDIA GPU Workstations", "• Robotics Prototyping Track", "• IoT Development Benches" },
                narration = "This engineering laboratory offers GPU computing clusters, robotics benches, and IoT stations for student capstone projects."
            });

            m_Rooms.Add(new RoomData
            {
                key = "nb-faculty",
                buildingKey = "north-block",
                name = "4th Floor Faculty Cabins",
                badge = "OPEN-DOOR MENTORSHIP",
                description = "Dedicated faculty consultation suites facilitating 1-on-1 mentorship, thesis guidance, and collaborative student-professor research.",
                highlights = new[] { "• 1-on-1 Mentorship Suites", "• Open-Door Policy Culture", "• Research Consultation Lounges" },
                narration = "On the 4th floor, professors maintain an accessible open-door policy, providing dedicated 1-on-1 mentorship to students."
            });

            m_Rooms.Add(new RoomData
            {
                key = "gw-classroom",
                buildingKey = "gateway",
                name = "Executive MBA Seminar Room",
                badge = "MANAGEMENT & LEADERSHIP",
                description = "Horseshoe boardroom layout tailored for case studies, executive masterclasses, and international video conferences.",
                highlights = new[] { "• Horseshoe Conference Layout", "• International Video Conferencing", "• Syndicate Breakout Lounges" },
                narration = "This executive classroom hosts corporate guest lectures, MBA case discussions, and international seminars."
            });

            m_Rooms.Add(new RoomData
            {
                key = "gw-lab",
                buildingKey = "gateway",
                name = "ACIC Startup Incubator Lab",
                badge = "NITI AAYOG INCUBATION",
                description = "BMU's government-backed startup incubator providing seed capital, patent guidance, and maker spaces for student ventures.",
                highlights = new[] { "• Seed Capital & Venture Grants", "• Rapid Prototyping 3D Printers", "• Hero Group Executive Network" },
                narration = "At the ACIC startup incubation center, student founders receive seed grants, patent advice, and corporate mentorship."
            });

            // Bind Photographic Sprites to Carousel Slides
            for (int i = 0; i < m_Slides.Count; i++)
            {
                if (m_SlidePhotoSets != null && i < m_SlidePhotoSets.Length && m_SlidePhotoSets[i] != null)
                {
                    m_Slides[i].photos = m_SlidePhotoSets[i].photos;
                    m_Slides[i].photoLabels = m_SlidePhotoSets[i].labels;
                }
            }

            // Bind Photographic Sprites to Locations & Rooms
            string[] locKeys = new string[] {
                "front-gate", "outside-north-block", "nb-classroom", "nb-lab", "nb-faculty",
                "outside-gateway", "gw-classroom", "gw-lab", "outside-workshop"
            };
            for (int i = 0; i < locKeys.Length; i++)
            {
                if (m_LocationPhotoSprites != null && i < m_LocationPhotoSprites.Length && m_LocationPhotoSprites[i] != null)
                {
                    if (m_Locations.TryGetValue(locKeys[i], out LocationData ld))
                    {
                        ld.photo = m_LocationPhotoSprites[i];
                    }
                    RoomData rd = m_Rooms.Find(r => r.key == locKeys[i]);
                    if (rd != null)
                    {
                        rd.photo = m_LocationPhotoSprites[i];
                    }
                }
            }
        }

        private void WireButtonListeners()
        {
            if (m_BtnResetTour != null) m_BtnResetTour.onClick.AddListener(StartTourFromBeginning);
            if (m_BtnRecenterUI != null)
            {
                m_BtnRecenterUI.onClick.AddListener(() =>
                {
                    if (m_LazyFollowUI == null) m_LazyFollowUI = GetComponent<LazyFollowUI>();
                    if (m_LazyFollowUI != null) m_LazyFollowUI.RecenterToCurrentGaze();
                });
            }

            // Intro Carousel buttons
            if (m_BtnPrevSlide != null) m_BtnPrevSlide.onClick.AddListener(PreviousSlide);
            if (m_BtnNextSlide != null) m_BtnNextSlide.onClick.AddListener(NextSlide);
            if (m_BtnSkipIntro != null) m_BtnSkipIntro.onClick.AddListener(OpenCampusHub);

            // Campus Hub destination buttons
            if (m_BtnSelectNorthBlock != null) m_BtnSelectNorthBlock.onClick.AddListener(() => TeleportToLocation("outside-north-block"));
            if (m_BtnSelectGateway != null) m_BtnSelectGateway.onClick.AddListener(() => TeleportToLocation("outside-gateway"));
            if (m_BtnSelectWorkshop != null) m_BtnSelectWorkshop.onClick.AddListener(() => TeleportToLocation("outside-workshop"));

            // Location Info buttons
            if (m_BtnExploreInterior != null) m_BtnExploreInterior.onClick.AddListener(OpenInteriorHubForCurrentBuilding);
            if (m_BtnNextLocation != null) m_BtnNextLocation.onClick.AddListener(NavigateToNextBuilding);
            if (m_BtnCampusHubReturn != null) m_BtnCampusHubReturn.onClick.AddListener(OpenCampusHub);
            if (m_BtnMinimize != null && m_DockOrb != null) m_BtnMinimize.onClick.AddListener(m_DockOrb.MinimizeToDock);

            // Internal Hub buttons
            if (m_BtnBackToPlaza != null) m_BtnBackToPlaza.onClick.AddListener(() => TeleportToLocation(m_CurrentBuildingContext));
            if (m_BtnBackToCampusHub != null) m_BtnBackToCampusHub.onClick.AddListener(OpenCampusHub);
            if (m_BtnRestoreOrb != null && m_DockOrb != null) m_BtnRestoreOrb.onClick.AddListener(m_DockOrb.RestoreFromDock);
        }

        // ====================================================================
        // STAGE 1: INTRO 3D CAROUSEL
        // ====================================================================
        public void StartTourFromBeginning()
        {
            m_CurrentSlideIndex = 0;
            m_CurrentLocationKey = "front-gate";

            Action onEntered = () =>
            {
                ActivateStage(m_IntroCarouselPanel);
                UpdateHeaderLocation("FRONT GATE • WELCOME & INTRO");
                UpdateCarouselDisplay();

                if (CampusEnvironmentManager.Instance != null)
                {
                    CampusEnvironmentManager.Instance.SwitchToZone("front-gate");
                }
            };

            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.TransitionToExterior("front-gate", onEntered);
            }
            else if (ScreenFade.Instance != null)
            {
                ScreenFade.Instance.FadeOutAndIn(onEntered);
            }
            else
            {
                onEntered();
            }
        }

        public void NextSlide()
        {
            if (m_CurrentSlideIndex < m_Slides.Count - 1)
            {
                m_CurrentSlideIndex++;
                UpdateCarouselDisplay();
            }
            else
            {
                OpenCampusHub();
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

            if (m_SlideBadgeText != null) m_SlideBadgeText.text = slide.badge;
            if (m_SlideTitleText != null) m_SlideTitleText.text = slide.title;
            if (m_SlideDescText != null) m_SlideDescText.text = slide.description;

            if (m_SlideStatsText != null && slide.stats != null)
            {
                m_SlideStatsText.text = string.Join("\n", slide.stats);
            }

            if (m_SlideIndexIndicator != null)
            {
                m_SlideIndexIndicator.text = $"SLIDE {m_CurrentSlideIndex + 1} OF {m_Slides.Count}";
            }

            // Update Dynamic Polaroid Images and Captions
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

            if (TourAudioNarrator.Instance != null)
            {
                TourAudioNarrator.Instance.PlayNarration(slide.narration);
            }
        }

        // ====================================================================
        // STAGE 2: CAMPUS EXPLORATION HUB
        // ====================================================================
        public void OpenCampusHub()
        {
            Action showHubAction = () =>
            {
                ActivateStage(m_CampusHubPanel);
                UpdateHeaderLocation("CAMPUS EXPLORATION HUB • CHOOSE DESTINATION");

                if (TourAudioNarrator.Instance != null)
                {
                    TourAudioNarrator.Instance.PlayNarration("Welcome to the Campus Exploration Hub. Choose one of our three primary university complexes to begin your tour.");
                }
            };

            // If indoors, return to exterior front gate per architecture plan
            bool isIndoors = m_CurrentLocationKey.StartsWith("nb-") || m_CurrentLocationKey.StartsWith("gw-");
            if (SceneTransitionManager.Instance != null && isIndoors)
            {
                SceneTransitionManager.Instance.TransitionToExterior("front-gate", showHubAction);
            }
            else if (ScreenFade.Instance != null)
            {
                ScreenFade.Instance.FadeOutAndIn(showHubAction);
            }
            else
            {
                showHubAction();
            }
        }

        // ====================================================================
        // STAGE 3: LOCATION EXTERIOR PLAZAS & INTERIORS
        // ====================================================================
        public void TeleportToLocation(string locKey)
        {
            m_CurrentLocationKey = locKey;
            if (locKey.StartsWith("outside-"))
            {
                m_CurrentBuildingContext = locKey;
            }

            Action onArrived = () =>
            {
                ShowLocationPanel(locKey);
                if (CampusEnvironmentManager.Instance != null)
                {
                    CampusEnvironmentManager.Instance.SwitchToZone(locKey);
                }
            };

            if (SceneTransitionManager.Instance != null)
            {
                bool isRoom = locKey.StartsWith("nb-") || locKey.StartsWith("gw-");
                if (isRoom)
                {
                    string buildingKey = locKey.StartsWith("gw-") ? "gateway" : "north-block";
                    SceneTransitionManager.Instance.TransitionToInterior(buildingKey, locKey, onArrived);
                }
                else
                {
                    SceneTransitionManager.Instance.TransitionToExterior(locKey, onArrived);
                }
            }
            else if (ScreenFade.Instance != null)
            {
                ScreenFade.Instance.FadeOutAndIn(onArrived);
            }
            else
            {
                onArrived();
            }
        }

        private void ShowLocationPanel(string locKey)
        {
            if (!m_Locations.TryGetValue(locKey, out LocationData data))
            {
                // Fallback if entering a room directly
                RoomData room = m_Rooms.Find(r => r.key == locKey);
                if (room != null)
                {
                    ShowRoomInfo(room);
                    return;
                }
                return;
            }

            ActivateStage(m_LocationInfoPanel);
            UpdateHeaderLocation(data.name.ToUpperInvariant());

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
                m_LocPhotoCardImage.color = Color.white;
            }

            // Configure action buttons
            if (m_BtnExploreInterior != null)
            {
                m_BtnExploreInterior.gameObject.SetActive(data.hasInterior);
                TextMeshProUGUI btnText = m_BtnExploreInterior.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = "Go Inside";
            }

            if (TourAudioNarrator.Instance != null)
            {
                TourAudioNarrator.Instance.PlayNarration(data.narration);
            }
        }

        private void ShowRoomInfo(RoomData room)
        {
            ActivateStage(m_LocationInfoPanel);
            UpdateHeaderLocation($"{room.name.ToUpperInvariant()} • INTERIOR");

            if (m_LocZoneTag != null) m_LocZoneTag.text = $"{room.badge} • 360° INTERIOR";
            if (m_LocHeading != null) m_LocHeading.text = room.name;
            if (m_LocSummary != null) m_LocSummary.text = room.description;

            if (m_LocFeaturesList != null && room.highlights != null)
            {
                m_LocFeaturesList.text = string.Join("\n", room.highlights);
            }

            if (m_LocPhotoCaption != null) m_LocPhotoCaption.text = $"{room.name} 360 View";

            if (m_LocPhotoCardImage != null && room.photo != null)
            {
                m_LocPhotoCardImage.sprite = room.photo;
                m_LocPhotoCardImage.color = Color.white;
            }

            if (m_BtnExploreInterior != null)
            {
                m_BtnExploreInterior.gameObject.SetActive(true);
                TextMeshProUGUI btnText = m_BtnExploreInterior.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = "Switch Room →";
            }

            if (TourAudioNarrator.Instance != null)
            {
                TourAudioNarrator.Instance.PlayNarration(room.narration);
            }
        }

        private void NavigateToNextBuilding()
        {
            if (m_CurrentBuildingContext == "outside-north-block")
            {
                TeleportToLocation("outside-gateway");
            }
            else if (m_CurrentBuildingContext == "outside-gateway")
            {
                TeleportToLocation("outside-workshop");
            }
            else
            {
                TeleportToLocation("outside-north-block");
            }
        }

        // ====================================================================
        // STAGE 4: INTERNAL BUILDING HUB
        // ====================================================================
        public void OpenInteriorHubForCurrentBuilding()
        {
            string building = m_CurrentBuildingContext == "outside-gateway" ? "gateway" : "north-block";

            ActivateStage(m_InternalBuildingHubPanel);

            string hubTitle = building == "gateway" ? "GATEWAY BUILDING • INTERIOR SPACES" : "NORTH BLOCK • INTERIOR SPACES";
            UpdateHeaderLocation(hubTitle);

            if (m_InternalHubTag != null) m_InternalHubTag.text = hubTitle;
            if (m_InternalHubTitle != null) m_InternalHubTitle.text = "Select an Interior Facility to Enter";

            PopulateRoomCards(building);

            if (TourAudioNarrator.Instance != null)
            {
                TourAudioNarrator.Instance.PlayNarration("Select any of our academic facilities or research laboratories to step inside.");
            }
        }

        private void PopulateRoomCards(string buildingKey)
        {
            if (m_RoomCardsContainer == null || m_RoomCardTemplate == null) return;

            // Clear previous cards except template
            foreach (Transform child in m_RoomCardsContainer)
            {
                if (child.gameObject != m_RoomCardTemplate)
                {
                    Destroy(child.gameObject);
                }
            }

            m_RoomCardTemplate.SetActive(false);

            List<RoomData> matchingRooms = m_Rooms.FindAll(r => r.buildingKey == buildingKey);
            foreach (RoomData room in matchingRooms)
            {
                GameObject cardObj = Instantiate(m_RoomCardTemplate, m_RoomCardsContainer);
                cardObj.SetActive(true);

                TextMeshProUGUI[] texts = cardObj.GetComponentsInChildren<TextMeshProUGUI>();
                if (texts.Length > 0) texts[0].text = room.name;
                if (texts.Length > 1) texts[1].text = room.badge;
                if (texts.Length > 2) texts[2].text = room.description;

                Button btn = cardObj.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    string rKey = room.key;
                    btn.onClick.AddListener(() => TeleportToLocation(rKey));
                }
            }
        }

        // ====================================================================
        // THUMBSTICK CONTROLS (CAROUSEL & NAVIGATION)
        // ====================================================================
        private void HandleThumbstickInput()
        {
            if (Time.time - m_LastThumbstickTime < m_ThumbstickCooldown) return;

            float horizontal = 0f;
            float vertical = 0f;

            // 1. Read from XR Input Devices (Meta Quest 3S controllers)
            var leftHandDevices = new List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller,
                leftHandDevices);

            var rightHandDevices = new List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                rightHandDevices);

            foreach (var dev in leftHandDevices)
            {
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis) && axis.sqrMagnitude > 0.04f)
                {
                    horizontal = axis.x;
                    vertical = axis.y;
                    break;
                }
            }

            if (Mathf.Abs(horizontal) < 0.1f && Mathf.Abs(vertical) < 0.1f)
            {
                foreach (var dev in rightHandDevices)
                {
                    if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis) && axis.sqrMagnitude > 0.04f)
                    {
                        horizontal = axis.x;
                        vertical = axis.y;
                        break;
                    }
                }
            }

            // 2. Fallback to Gamepad (if present)
            if (Mathf.Abs(horizontal) < 0.1f && Mathf.Abs(vertical) < 0.1f)
            {
                if (Gamepad.current != null)
                {
                    Vector2 pad = Gamepad.current.leftStick.ReadValue();
                    if (pad.sqrMagnitude < 0.04f) pad = Gamepad.current.dpad.ReadValue();
                    if (pad.sqrMagnitude > 0.04f)
                    {
                        horizontal = pad.x;
                        vertical = pad.y;
                    }
                }
            }

            // 3. Fallback to Keyboard for Editor simulation
            if (Mathf.Abs(horizontal) < 0.1f && Mathf.Abs(vertical) < 0.1f)
            {
                if (Keyboard.current != null)
                {
                    var kb = Keyboard.current;
                    if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) horizontal = -1f;
                    else if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) horizontal = 1f;

                    if (kb.downArrowKey.isPressed || kb.sKey.isPressed) vertical = -1f;
                    else if (kb.upArrowKey.isPressed || kb.wKey.isPressed) vertical = 1f;
                }
            }

            if (Mathf.Abs(horizontal) > m_ThumbstickThreshold)
            {
                m_LastThumbstickTime = Time.time;
                if (m_IntroCarouselPanel != null && m_IntroCarouselPanel.activeSelf)
                {
                    if (horizontal > 0) NextSlide();
                    else PreviousSlide();
                }
                else if (m_LocationInfoPanel != null && m_LocationInfoPanel.activeSelf)
                {
                    if (horizontal > 0) NavigateToNextBuilding();
                }
            }

            if (vertical < -m_ThumbstickThreshold)
            {
                // Flick down on thumbstick to toggle minimize dock orb
                m_LastThumbstickTime = Time.time;
                if (m_DockOrb != null)
                {
                    m_DockOrb.ToggleDockState();
                }
            }
        }

        // ====================================================================
        // HELPER UTILITIES
        // ====================================================================
        private void ActivateStage(GameObject activePanel)
        {
            if (m_IntroCarouselPanel != null) m_IntroCarouselPanel.SetActive(m_IntroCarouselPanel == activePanel);
            if (m_CampusHubPanel != null) m_CampusHubPanel.SetActive(m_CampusHubPanel == activePanel);
            if (m_LocationInfoPanel != null) m_LocationInfoPanel.SetActive(m_LocationInfoPanel == activePanel);
            if (m_InternalBuildingHubPanel != null) m_InternalBuildingHubPanel.SetActive(m_InternalBuildingHubPanel == activePanel);
        }

        private void UpdateHeaderLocation(string locText)
        {
            if (m_HeaderLocationText != null)
            {
                m_HeaderLocationText.text = locText;
            }
        }
    }
}
