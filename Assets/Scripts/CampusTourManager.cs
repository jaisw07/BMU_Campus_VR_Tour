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
            public Sprite photo;      // detail-panel photo (right side of the info panel)
            public Sprite cardPhoto;  // wide hub-card image
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
            public Sprite photo;      // detail-panel photo (right side of the info panel)
            public Sprite cardPhoto;  // wide hub-card image
            // Optional overrides (empty = legacy behaviour): info-panel tag line and short hub-card description.
            public string zoneTag;
            public string cardDescription;
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
        [SerializeField] private Sprite[] m_LocationCardSprites = new Sprite[14]; // wide hub-card images, index-aligned with m_LocationPhotoSprites

        [Header("Internal Building Hub Elements")]
        [SerializeField] private TextMeshProUGUI m_InternalHubTag;
        [SerializeField] private TextMeshProUGUI m_InternalHubTitle;
        [Tooltip("Optional footer under the room cards; shown on the Workshop hub as '<n> Internal Spaces'.")]
        [SerializeField] private TextMeshProUGUI m_InternalHubFooter;
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
#pragma warning disable CS0618
                mp.useGravity = false;
#pragma warning restore CS0618
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
            // 5-Slide Intro Carousel (matching slidewise material)
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
                badge = "INFRASTRUCTURE & LABS",
                title = "A 50-Acre Campus for Learning & Discovery",
                description = "Designed to support academic, residential and recreational activities, BMU's campus brings together spaces for learning, research, collaboration and student interaction. Its environment encourages students to explore their interests while balancing academics with personal development and campus life.",
                stats = new[] { "Dedicated Research & Innovation Facilities", "Residential Hostels & Student Amenities", "Sports, Recreation & Collaborative Spaces" }
            });

            m_Slides.Add(new CarouselSlideData
            {
                badge = "STUDENT LIFE & CULTURE",
                title = "Vibrant Campus Life, Events & Sports",
                description = "BMU's campus comes alive through the annual 67th Milestone festival, TEDxBMU, hackathons, and cultural events. With 24+ student clubs, outreach drives, and inter-university sports tournaments, students enjoy dynamic teamwork and vibrant campus life.",
                stats = new[] { "24 Student-Run Clubs & Societies", "67th Milestone – 50+ Colleges Participated in 2026", "Basketball, Tennis, Volleyball & Gym Facilities" }
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
                hasInterior = true,
                buildingKey = "workshop"
            };

            // Interior Rooms
            m_Rooms.Add(new RoomData
            {
                key = "nb-classroom",
                buildingKey = "north-block",
                name = "Smart Case Study Room (NB-105)",
                badge = "CASE-BASED LEARNING",
                description = "A tiered case-study classroom: rows of desks rise in four tiers facing the front whiteboard and wall display, so every seat has a clear view for discussion.",
                highlights = new[] { "• Four-Tier Desk Rows", "• Front Whiteboard & Wall Display", "• Podium for Case Presentations" },
                narration = "This is NB-105, the smart case study room, where tiered seating gives every student a clear view for case discussions."
            });

            m_Rooms.Add(new RoomData
            {
                key = "nb-classroom-2",
                buildingKey = "north-block",
                name = "Standard Classroom",
                badge = "LECTURE LEARNING",
                description = "A standard lecture classroom with rows of desks facing the front whiteboard, used for everyday lectures, tutorials and student presentations.",
                highlights = new[] { "• Row-Based Desk Seating", "• Front Whiteboard", "• Everyday Lectures & Tutorials" },
                narration = "This is one of the North Block's standard classrooms, where everyday lectures and tutorials take place."
            });

            m_Rooms.Add(new RoomData
            {
                key = "nb-lab",
                buildingKey = "north-block",
                name = "AR-VR Laboratory",
                badge = "IMMERSIVE TECHNOLOGY",
                description = "A workstation laboratory for augmented and virtual reality development, with rows of high-performance computers for student projects.",
                highlights = new[] { "• AR / VR Development Workstations", "• Row-Based Computer Stations", "• Student Project Space" },
                narration = "This is the AR-VR laboratory, where students build augmented and virtual reality projects on rows of high-performance workstations."
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

            // Gateway Building: three interior spaces (GA_Lab, GA_Office, GA_Auditorium in Scene_Gateway_Interiors).
            m_Rooms.Add(new RoomData
            {
                key = "gw-ai-lab",
                buildingKey = "gateway",
                name = "Artificial Intelligence (AI) Lab",
                badge = "AI LAB",
                zoneTag = "GATEWAY • INTERIOR",
                description = "A hands-on learning and development space where students explore Artificial Intelligence, build machine learning models and experiment with real-world datasets. Equipped with NVIDIA GPU computing infrastructure, including the DGX H200 system, and dedicated cooling facilities, the lab supports deep learning, Generative AI, model training and advanced AI research.",
                cardDescription = "GPU computing lab for machine learning, deep learning and Generative AI.",
                highlights = new[] { "• NVIDIA DGX H200 GPU Computing", "• Dedicated Cooling System", "• Machine Learning, Deep Learning & Generative AI" },
                narration = "This is the Artificial Intelligence lab, where students build machine learning models on NVIDIA GPU computing infrastructure."
            });

            m_Rooms.Add(new RoomData
            {
                key = "gw-office",
                buildingKey = "gateway",
                name = "Program Office",
                badge = "STUDENT SERVICES",
                description = "The program office: a reception lobby leads into an open-plan office with workstations and glass-walled cabins for programme coordinators.",
                highlights = new[] { "• Reception Lobby", "• Open-Plan Workstations", "• Glass-Walled Coordinator Cabins" },
                narration = "This is the program office, where students are welcomed in the reception lobby and programme coordinators work in the open office and glass cabins."
            });

            m_Rooms.Add(new RoomData
            {
                key = "gw-auditorium",
                buildingKey = "gateway",
                name = "Auditorium",
                badge = "EVENTS & LECTURES",
                description = "A tiered auditorium with 360 seats, a raised stage with truss lighting and a projection screen for guest lectures, seminars and campus events.",
                highlights = new[] { "• 360 Tiered Seats", "• Raised Stage with Truss Lighting", "• Projection Screen" },
                narration = "This is the Gateway auditorium, with tiered seating for 360 people, a raised stage and a projection screen for lectures and events."
            });

            m_Rooms.Add(new RoomData
            {
                key = "ws-foyer",
                buildingKey = "workshop",
                name = "Manufacturing Process Lab",
                badge = "MANUFACTURING",
                zoneTag = "MECHANICAL LAB • INTERIOR",
                description = "A large-scale manufacturing and machining facility where students gain hands-on experience with industrial machinery and production processes. The lab provides practical exposure to conventional machining, CNC operations, laser cutting and material processing, helping students understand how engineering designs are transformed into physical components.",
                cardDescription = "Workshop for conventional machining, CNC operations and laser cutting.",
                highlights = new[] { "• Lathe, Milling & CNC Machines", "• Laser Cutting & Material Processing", "• Hands-on Manufacturing Practice" },
                narration = "This is the Manufacturing Process Lab, where students gain hands-on experience with machining, CNC operations and laser cutting."
            });

            m_Rooms.Add(new RoomData
            {
                key = "ws-hall",
                buildingKey = "workshop",
                name = "KUKA Robotics Laboratory",
                badge = "ROBOTICS",
                zoneTag = "MECHANICAL LAB • INTERIOR",
                description = "A specialised facility for learning industrial robotics and automated manufacturing. Equipped with KUKA industrial robots and KRC-4 controllers, the laboratory provides hands-on training in robot operation and applications such as arc welding, spot welding and material handling, preparing students to understand robotic systems used in modern manufacturing industries.",
                cardDescription = "Industrial robotics lab for robot programming, welding and handling.",
                highlights = new[] { "• KUKA Industrial Robots & KRC-4 Controllers", "• Robotic Arc Welding & Spot Welding", "• Automated Material Handling" },
                narration = "This is the KUKA Robotics Laboratory, where students train on industrial robots for welding and material handling."
            });

            m_Rooms.Add(new RoomData
            {
                key = "ws-mech",
                buildingKey = "workshop",
                name = "Mechatronics Laboratory",
                badge = "MECHATRONICS",
                zoneTag = "MECHANICAL LAB • INTERIOR",
                description = "A specialised facility that brings together mechanical engineering, electronics and computer-based control to demonstrate automated production systems. Equipped with modular industrial training setups, the laboratory helps students understand how machines, sensors, actuators and programmable logic controllers work together to perform coordinated operations.",
                cardDescription = "Modular automation setups showing how sensors, actuators and PLCs work together.",
                highlights = new[] { "• Modular Five-Station Automation Systems", "• Sensors, Actuators & Drive Systems", "• PLC-Based Control & Industrial Automation" },
                narration = "This is the Mechatronics Laboratory, where sensors, actuators and PLCs work together in modular automation systems."
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
            // KEEP IN SYNC with BMUTourSceneBuilder.LoadLocationPhotos(): same keys, same order (index-aligned).
            string[] locKeys = new string[] {
                "front-gate", "outside-north-block", "nb-classroom", "nb-classroom-2", "nb-lab", "nb-faculty",
                "outside-gateway", "gw-ai-lab", "gw-office", "gw-auditorium", "outside-workshop",
                "ws-foyer", "ws-hall", "ws-mech"
            };
            for (int i = 0; i < locKeys.Length; i++)
            {
                if (m_LocationCardSprites != null && i < m_LocationCardSprites.Length && m_LocationCardSprites[i] != null)
                {
                    if (m_Locations.TryGetValue(locKeys[i], out LocationData cardLd)) cardLd.cardPhoto = m_LocationCardSprites[i];
                    RoomData cardRd = m_Rooms.Find(r => r.key == locKeys[i]);
                    if (cardRd != null) cardRd.cardPhoto = m_LocationCardSprites[i];
                }
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
            if (m_BtnSkipIntro != null) m_BtnSkipIntro.onClick.AddListener(() =>
            {
                if (TourAudioNarrator.Instance != null) TourAudioNarrator.Instance.StopNarration();
                OpenCampusHub();
            });

            // Campus Hub destination buttons
            if (m_BtnSelectNorthBlock != null) m_BtnSelectNorthBlock.onClick.AddListener(() => TeleportToLocation("outside-north-block"));
            if (m_BtnSelectGateway != null) m_BtnSelectGateway.onClick.AddListener(() => TeleportToLocation("outside-gateway"));
            if (m_BtnSelectWorkshop != null) m_BtnSelectWorkshop.onClick.AddListener(() => TeleportToLocation("outside-workshop"));

            // Location Info buttons
            if (m_BtnExploreInterior != null) m_BtnExploreInterior.onClick.AddListener(OnExploreInteriorClicked);
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
            if (TourAudioNarrator.Instance != null) TourAudioNarrator.Instance.StopNarration();
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

        // Looks the state's clip up on the panel's PanelVoiceover and hands it to the narrator, which is the
        // only place audio is triggered from. No entry / no clip stops whatever was playing.
        private void PlayVoice(GameObject panel, string key, string caption)
        {
            if (TourAudioNarrator.Instance == null) return;
            AudioClip clip = null;
            float delay = 0.3f;
            PanelVoiceover pv = panel != null ? panel.GetComponent<PanelVoiceover>() : null;
            if (pv != null) pv.TryGet(key, out clip, out delay);
            TourAudioNarrator.Instance.PlayNarration(caption, clip, delay);
        }

        private static string HubVoiceKey(string buildingKey) => "hub-" + buildingKey;

        // "Switch Room" in a workshop room cycles ws-foyer -> ws-hall -> ws-mech; other buildings still open their hub.
        private void OnExploreInteriorClicked()
        {
            if (m_CurrentLocationKey != null && m_CurrentLocationKey.StartsWith("ws-"))
            {
                List<RoomData> rooms = m_Rooms.FindAll(r => r.buildingKey == "workshop");
                int idx = rooms.FindIndex(r => r.key == m_CurrentLocationKey);
                if (rooms.Count > 1 && idx >= 0)
                {
                    TeleportToLocation(rooms[(idx + 1) % rooms.Count].key);
                    return;
                }
            }
            OpenInteriorHubForCurrentBuilding();
        }

        private void UpdateCarouselDisplay()
        {
            if (m_Slides == null || m_Slides.Count == 0) return;
            CarouselSlideData slide = m_Slides[m_CurrentSlideIndex];
            PlayVoice(m_IntroCarouselPanel, $"slide-{m_CurrentSlideIndex}", slide.title);

            if (m_SlideBadgeText != null)
            {
                m_SlideBadgeText.text = slide.badge;
                m_SlideBadgeText.color = new Color(0.01f, 0.52f, 0.78f); // Theme sky blue
            }
            if (m_SlideTitleText != null) m_SlideTitleText.text = slide.title;
            if (m_SlideDescText != null) m_SlideDescText.text = slide.description;

            if (m_SlidePointerTexts != null && m_SlidePointerTexts.Length > 0 && slide.stats != null)
            {
                for (int i = 0; i < m_SlidePointerTexts.Length; i++)
                {
                    if (m_SlidePointerTexts[i] != null)
                    {
                        if (i < slide.stats.Length && !string.IsNullOrEmpty(slide.stats[i]))
                        {
                            var parentObj = m_SlidePointerTexts[i].transform.parent.gameObject;
                            parentObj.SetActive(true);
                            m_SlidePointerTexts[i].text = slide.stats[i].TrimStart('•', ' ', '-');

                            var tagRt = parentObj.GetComponent<RectTransform>();
                            if (tagRt != null)
                            {
                                tagRt.pivot = new Vector2(0f, 0.5f);
                            }
                        }
                        else
                        {
                            m_SlidePointerTexts[i].transform.parent.gameObject.SetActive(false);
                        }
                    }
                }

                if (m_SlidePointerTexts[0] != null && m_SlidePointerTexts[0].transform.parent != null)
                {
                    var containerRt = m_SlidePointerTexts[0].transform.parent.parent as RectTransform;
                    if (containerRt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRt);
                    }
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

                PlayVoice(m_CampusHubPanel, "campus-hub", "Welcome to the Campus Exploration Hub. Choose one of our three primary university complexes to begin your tour.");
            };

            // If indoors, return to exterior front gate per architecture plan
            bool isIndoors = IsRoomKey(m_CurrentLocationKey);
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
        // Room keys carry their building in the prefix: nb- (North Block), gw- (Gateway), ws- (Workshop).
        private static bool IsRoomKey(string key)
        {
            return key.StartsWith("nb-") || key.StartsWith("gw-") || key.StartsWith("ws-");
        }

        private static string BuildingKeyForRoom(string roomKey)
        {
            if (roomKey.StartsWith("gw-")) return "gateway";
            if (roomKey.StartsWith("ws-")) return "workshop";
            return "north-block";
        }

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
                if (IsRoomKey(locKey))
                {
                    SceneTransitionManager.Instance.TransitionToInterior(BuildingKeyForRoom(locKey), locKey, onArrived);
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
                m_LocPhotoCardImage.preserveAspect = true;
                m_LocPhotoCardImage.color = Color.white;
            }

            // Configure action buttons
            if (m_BtnExploreInterior != null)
            {
                m_BtnExploreInterior.gameObject.SetActive(data.hasInterior);
                TextMeshProUGUI btnText = m_BtnExploreInterior.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = "ENTER";
            }

            PlayVoice(m_LocationInfoPanel, locKey, data.narration);
        }

        private void ShowRoomInfo(RoomData room)
        {
            ActivateStage(m_LocationInfoPanel);
            UpdateHeaderLocation($"{room.name.ToUpperInvariant()} • INTERIOR");

            if (m_LocZoneTag != null) m_LocZoneTag.text = string.IsNullOrEmpty(room.zoneTag) ? $"{room.badge} • 360° INTERIOR" : room.zoneTag;
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
                m_LocPhotoCardImage.preserveAspect = true;
                m_LocPhotoCardImage.color = Color.white;
            }

            if (m_BtnExploreInterior != null)
            {
                m_BtnExploreInterior.gameObject.SetActive(true);
                TextMeshProUGUI btnText = m_BtnExploreInterior.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = "Switch Room";
            }

            PlayVoice(m_LocationInfoPanel, room.key, room.narration);
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
            string building = m_CurrentBuildingContext == "outside-gateway" ? "gateway"
                : m_CurrentBuildingContext == "outside-workshop" ? "workshop" : "north-block";

            ActivateStage(m_InternalBuildingHubPanel);

            string hubTitle = building == "gateway" ? "GATEWAY BUILDING • INTERIOR SPACES"
                : building == "workshop" ? "MECHANICAL WORKSHOP • INTERIOR SPACES" : "NORTH BLOCK • INTERIOR SPACES";
            UpdateHeaderLocation(hubTitle);

            if (m_InternalHubTag != null) m_InternalHubTag.gameObject.SetActive(false);
            if (m_InternalHubTitle != null) m_InternalHubTitle.text = "Select a Facility";

            PopulateRoomCards(building);

            if (m_InternalHubFooter != null)
            {
                bool showFooter = building == "workshop";
                m_InternalHubFooter.gameObject.SetActive(showFooter);
                if (showFooter) m_InternalHubFooter.text = $"{m_Rooms.FindAll(r => r.buildingKey == building).Count} Internal Spaces";
            }

            PlayVoice(m_InternalBuildingHubPanel, HubVoiceKey(building), "Select any of our academic facilities or research laboratories to step inside.");
        }

        /// <summary>Assigns a sprite to an Image inside a masked container and fill-crops it (no stretching, no letterbox).</summary>
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

        private void PopulateRoomCards(string buildingKey)
        {
            if (m_RoomCardsContainer == null || m_RoomCardTemplate == null) return;

            // Clear previous cards except template. Deactivate first: Destroy is deferred to end of
            // frame, and the layout group would otherwise still lay out the old cards this frame.
            foreach (Transform child in m_RoomCardsContainer)
            {
                if (child.gameObject != m_RoomCardTemplate)
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }

            m_RoomCardTemplate.SetActive(false);

            List<RoomData> matchingRooms = m_Rooms.FindAll(r => r.buildingKey == buildingKey);
            foreach (RoomData room in matchingRooms)
            {
                GameObject cardObj = Instantiate(m_RoomCardTemplate, m_RoomCardsContainer);
                cardObj.SetActive(true);

                // Look texts up by name: hierarchy order puts the badge first, so index-based
                // assignment used to put the room name inside the small badge pill.
                SetDescendantText(cardObj.transform, "Title", room.name);
                SetDescendantText(cardObj.transform, "Badge", room.badge);
                SetDescendantText(cardObj.transform, "Description", string.IsNullOrEmpty(room.cardDescription) ? room.description : room.cardDescription);
                Transform actionPill = cardObj.transform.Find("ActionPill");
                if (actionPill != null) actionPill.gameObject.SetActive(false);

                // Ensure center alignment and Truncate overflow
                TextMeshProUGUI titleTmp = cardObj.transform.Find("TextStack/Title")?.GetComponent<TextMeshProUGUI>();
                if (titleTmp != null) titleTmp.alignment = TextAlignmentOptions.Center;
                TextMeshProUGUI descTmp = cardObj.transform.Find("TextStack/Description")?.GetComponent<TextMeshProUGUI>();
                if (descTmp != null)
                {
                    descTmp.alignment = TextAlignmentOptions.Center;
                    descTmp.overflowMode = TextOverflowModes.Truncate;
                }

                // Bind matching interior photo
                Transform photoTr = cardObj.transform.Find("PhotoContainer/CardPhoto") ?? cardObj.transform.Find("CardPhoto");
                if (photoTr != null)
                {
                    Image photoImg = photoTr.GetComponent<Image>();
                    Sprite cardSprite = room.cardPhoto != null ? room.cardPhoto : room.photo;
                    if (photoImg != null && cardSprite != null)
                    {
                        ApplyFillCropSprite(photoImg, cardSprite);
                    }
                }

                // Category color badge chip styling
                Transform badgePillTr = cardObj.transform.Find("BadgePill");
                if (badgePillTr != null)
                {
                    Image badgeImg = badgePillTr.GetComponent<Image>();
                    if (badgeImg != null)
                    {
                        if (buildingKey == "north-block")
                            badgeImg.color = new Color(0f, 0.17f, 0.29f); // #002B49 BMU Navy
                        else if (buildingKey == "gateway")
                            badgeImg.color = new Color(0.01f, 0.52f, 0.78f); // #0284C7 Natural Sky Blue
                        else
                            badgeImg.color = new Color(0.89f, 0.11f, 0.14f); // #E31B23 BMU Crimson
                    }

                    TextMeshProUGUI bTmp = badgePillTr.GetComponentInChildren<TextMeshProUGUI>();
                    if (bTmp != null)
                    {
                        bTmp.text = room.badge;
                        RectTransform bpRt = badgePillTr.GetComponent<RectTransform>();
                        if (bpRt != null)
                        {
                            bpRt.sizeDelta = new Vector2(bTmp.preferredWidth + 24f, bpRt.sizeDelta.y);
                        }
                    }
                }

                Button btn = cardObj.GetComponent<Button>() ?? cardObj.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    string rKey = room.key;
                    btn.onClick.AddListener(() => TeleportToLocation(rKey));
                }

                // Re-initialize VRButtonFeedback resting position after layout
                VRButtonFeedback fb = cardObj.GetComponent<VRButtonFeedback>();
                if (fb != null)
                {
                    fb.InitializeRestingPosition();
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

        private static void SetDescendantText(Transform root, string childName, string value)
        {
            foreach (TextMeshProUGUI tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.name == childName)
                {
                    tmp.text = value;
                    return;
                }
            }
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
