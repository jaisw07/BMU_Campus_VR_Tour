using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Builds Assets/Scenes/NB_Building_Interior.unity: the full North Block interior from NB-Building.glb (textured, real metre
    /// scale) lit by the 156 area lights authored in NB-Building.fbx (copied 1:1; URP only renders Rectangle lights through a
    /// lightmap bake). The "NB_Interior" root and its children are never renamed, moved or rescaled.
    /// </summary>
    public static class NBBuildingInteriorSetup
    {
        private const string Dir = "Assets/Models/Interior/NB-Building/";
        private const string GlbPath = Dir + "NB-Building.glb";
        private const string FbxPath = Dir + "NB-Building.fbx";
        public const string ScenePath = "Assets/Scenes/Dev/NB_Building_Interior.unity"; // desktop test scene (not part of the tour)

        // NB-105 (Smart Case Study Room) in the tour scene: local position under the NB_Interior root (which sits at the origin,
        // scale 1). The building spans x -4.9..14.5, z -32..20 and NB-105 is 17.8 x 14.3 m, so this puts its centre east of the
        // east wall with a 1.6 m gap and no overlap with any room.
        private static readonly Vector3 NB105LocalPosition = new Vector3(25f, 0f, 0f);
        private const float NB105YRotation = 0f;
        private const string LightingSettingsPath = "Assets/Settings/NB_Building_Interior_Lighting.lighting";

        // No placement was given: the model stays exactly where it was authored (GLB is already real-metre scale, so scale = 1).
        private static readonly Vector3 RootPosition = Vector3.zero;

        // Floor-top Y of the three storeys (Plane objects at ~0.03, 2.73, 5.39).
        private static readonly string[] WallNames = { "SideW1", "SideW2", "WallB", "WallL", "WallR", "Floor", "Ceiling" };
        private static readonly string[] GlassNames = { "Translucent Glass", "Transparentdoorwall" };
        private static readonly string[] SkipColliderNames = { "DoorHandle", "DoorHandles", "DoorHandleSupport1", "Multiline D-C 600_diff", "Plane" };
        private static readonly string[] FurnitureNames =
        {
            "Chair", "StudentChair", "LabChair", "OfficeChair", "TopPlank", "LongLeg", "TableLeg", "Boardroom_table",
            "SystemSet", "Whiteboard", "Plant_14", "pot_14", "soil_11", "legs_14"
        };

        // Authored Blender intensity (31.416) x this = Unity intensity (3.14), calibrated by looking at the baked rooms.
        private const float IntensityScale = 0.1f;

        private struct Room
        {
            public string Name; public Vector3 Centre; public Vector2 Size; public float Height;
            public int Level; public NBRoomType Type; public string Label; public string Display; public string NavKey;
        }

        public const string TourScenePath = "Assets/Scenes/Scene_NorthBlock_Interiors.unity";

        /// <summary>True when the NB-Building GLB and FBX are in the project (the tour builder then uses them).</summary>
        public static bool SourcesAvailable =>
            AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath) != null && AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) != null;

        /// <summary>
        /// True when Scene_NorthBlock_Interiors already holds the NB building (navigator pods present) with baked lighting.
        /// XRSetupInitializer rebuilds every scene on every script recompile, which would throw the lightmap bake away, so
        /// BMUTourSceneBuilder keeps this scene as it is. Use the "Build North Block Interiors Scene (NB-Building)" menu item
        /// to regenerate it deliberately (then bake again).
        /// </summary>
        public static bool TourSceneIsCurrent()
        {
            try
            {
                string lightingData = "Assets/Scenes/Scene_NorthBlock_Interiors/LightingData.asset";
                if (!File.Exists(TourScenePath) || !File.Exists(lightingData)) return false;
                string text = File.ReadAllText(TourScenePath);
                return text.Contains("NB_Interior_NavigatorPods") && text.Contains("NB105_Setup");
            }
            catch (System.Exception) { return false; }
        }

        [MenuItem("BMU VR Tour/Build NB Building Interior Scene", false, 16)]
        public static void BuildScene() => Build(ScenePath, false);

        /// <summary>Builds Scene_NorthBlock_Interiors from the NB building: room pods for the navigator, no desktop player.</summary>
        [MenuItem("BMU VR Tour/Build North Block Interiors Scene (NB-Building)", false, 17)]
        public static void BuildTourScene() => Build(TourScenePath, true);

        [MenuItem("BMU VR Tour/Add Room Markers To Open NB Interior Scene", false, 18)]
        public static void AddRoomMarkersToOpenScene()
        {
            GameObject root = GameObject.Find("NB_Interior");
            if (root == null) { Debug.LogError("[NBBuildingInteriorSetup] Open an NB interior scene first."); return; }
            List<Room> rooms = ClassifyRooms(root, FindRooms(root));
            GameObject old = GameObject.Find("NB_Interior_Rooms");
            if (old != null) Object.DestroyImmediate(old);
            AddRoomMarkers(rooms);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static void Build(string scenePath, bool tour)
        {
            GameObject glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (glb == null || fbx == null) { Debug.LogError("[NBBuildingInteriorSetup] Could not load the NB-Building GLB/FBX."); return; }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StringBuilder log = new StringBuilder();

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            root.name = "NB_Interior";
            root.transform.SetPositionAndRotation(RootPosition, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            ApplyFolderTextures(root, log);
            ConfigureRenderers(root, log);
            AddColliders(root, log);
            List<Room> rooms = ClassifyRooms(root, FindRooms(root));
            log.AppendLine($"rooms={rooms.Count}: " +
                           $"{rooms.FindAll(r => r.Type == NBRoomType.Classroom).Count} classrooms, " +
                           $"{rooms.FindAll(r => r.Type == NBRoomType.ARVRLab).Count} AR-VR lab, " +
                           $"{rooms.FindAll(r => r.Type == NBRoomType.Lab).Count} other labs, " +
                           $"{rooms.FindAll(r => r.Type == NBRoomType.FacultyCabin).Count} faculty cabins");
            AddRoomMarkers(rooms);
            AddAuthoredLights(root, fbx, log);
            AddProbesAndCulling(rooms, log);
            if (tour)
            {
                // NB-105 is the navigator's "nb-classroom" room, placed under the NB_Interior root.
                NB105Setup.EnsureLightmapUVs();
                GameObject nb105 = NB105Setup.Install(root.transform, NB105LocalPosition, NB105YRotation, false, "nb-classroom", log);
                Transform nb105Anchor = nb105 != null ? nb105.transform.Find("NB105_Setup/NB105_Anchors/Anchor_NB105_Entrance") : null;
                AddNavigatorPods(rooms, nb105Anchor, log);
            }
            else AddPlayer(root, rooms, log);
            ConfigureLightingSettings();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.06f); // near-black: the real lights do the work
            RenderSettings.skybox = null;

            string sceneDir = Path.GetDirectoryName(scenePath);
            if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[NBBuildingInteriorSetup] Built '" + scenePath + "'.\n" + log);
        }

        // ------------------------------------------------------------------------------------------------------
        private static string BaseName(string n)
        {
            int dot = n.LastIndexOf('.');
            if (dot > 0 && int.TryParse(n.Substring(dot + 1), out _)) return n.Substring(0, dot);
            return n;
        }

        private static bool In(string[] set, string n) => System.Array.IndexOf(set, n) >= 0;

        // --- Textures from the NB-Building folder ---------------------------------------------------------------
        // The GLB (exported before the .blend / PNGs were finished) has plain white ceilings and floors. The five PNGs in
        // the folder are those textures. Imported materials are copied (same shader, same factors) and only
        // baseColorTexture + its tiling/offset are added, so no shader is swapped.
        private struct TexMap { public string SourceMaterial, Png, Asset; public Vector4 St; }

        // NBClassCeiling's tile grid lives at u 0..0.405, v 0..0.62 of its canvas, but every ceiling's visible quad is at
        // u 0.13..0.38, v 0.50..0.75, so it is remapped (tiling/offset) to fit the whole grid onto the quad.
        private static readonly Vector4 ClassCeilingSt = new Vector4(0.405f / 0.25f, 0.62f / 0.25f, -0.13f * (0.405f / 0.25f), -0.5f * (0.62f / 0.25f));

        private static void ApplyFolderTextures(GameObject root, StringBuilder log)
        {
            const string matDir = "Assets/Materials/Environment/NBBuilding";
            if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);
            var maps = new List<TexMap>
            {
                new TexMap { SourceMaterial = "Material",     Png = "NBLabCeiling.png",      Asset = "NB_Ceiling_Lab",      St = new Vector4(1, 1, 0, 0) },
                new TexMap { SourceMaterial = "Material.028", Png = "NBClassCeiling.png",    Asset = "NB_Ceiling_Class",    St = ClassCeilingSt },
                new TexMap { SourceMaterial = "Material.051", Png = "4thfloor ceiling.png",  Asset = "NB_Ceiling_4thFloor", St = new Vector4(1, 1, 0, 0) },
                new TexMap { SourceMaterial = "Material.053", Png = "NBcabin.png",           Asset = "NB_Ceiling_Cabin",    St = new Vector4(1, 1, 0, 0) },
                new TexMap { SourceMaterial = "Floor",        Png = "NBFloors.png",          Asset = "NB_Floors",           St = new Vector4(1, 1, 0, 0) },
            };

            int assigned = 0;
            foreach (TexMap tm in maps)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + tm.Png);
                if (tex == null) { log.AppendLine($"texture missing: {tm.Png}"); continue; }

                string path = $"{matDir}/{tm.Asset}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    Material src = null;
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (Material m in r.sharedMaterials) if (m != null && m.name == tm.SourceMaterial) { src = m; break; }
                        if (src != null) break;
                    }
                    if (src == null) { log.AppendLine($"source material missing: {tm.SourceMaterial}"); continue; }
                    mat = new Material(src) { name = tm.Asset };
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.SetTexture("baseColorTexture", tex);
                mat.SetVector("baseColorTexture_ST", tm.St);
                if (tm.St != new Vector4(1f, 1f, 0f, 0f))
                {
                    // The glTFast shader only applies tiling/offset when _TEXTURE_TRANSFORM is on, and then it needs a valid
                    // 2x2 rotation matrix (identity = 1,0,0,1) - a zero vector collapses the texture to one texel.
                    mat.EnableKeyword("_TEXTURE_TRANSFORM");
                    mat.SetVector("baseColorTexture_Rotation", new Vector4(1f, 0f, 0f, 1f));
                }
                EditorUtility.SetDirty(mat);

                string baseName = tm.SourceMaterial;
                foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string rb = BaseName(r.name);
                    // Floor material is used by both "Floor" boxes and the full-footprint "Plane" quads.
                    bool eligible = baseName == "Floor" ? (rb == "Floor" || rb == "Plane") : rb == "Ceiling";
                    if (!eligible) continue;
                    Material[] mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].name == tm.SourceMaterial) { mats[i] = mat; changed = true; assigned++; }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"folder textures applied to {assigned} material slots (5 PNGs from NB-Building)");
        }

        // --- Static / GI ----------------------------------------------------------------------------------------
        private static void ConfigureRenderers(GameObject root, StringBuilder log)
        {
            int lightmapped = 0, probeLit = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string b = BaseName(r.name);
                bool transparent = false;
                foreach (Material m in r.sharedMaterials) if (m != null && m.renderQueue >= 3000) transparent = true;

                StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                                          StaticEditorFlags.ReflectionProbeStatic;
                if (!transparent) flags |= StaticEditorFlags.OccluderStatic;
                r.gameObject.layer = 0;

                // Architecture is lightmapped; furniture, glass and small parts are lit by light probes. Only architecture
                // (walls, floors, ceilings, planes and partition Cubes whose lightmap UVs are valid) contributes to GI:
                // letting the ~3,000 furniture pieces / ~160 tiny Cubes with invalid UV2 contribute made the bake fail with
                // "atlassing failed".
                bool architecture = IsArchitecture(r, b) && !In(GlassNames, b) && HasValidLightmapUVs(r);
                if (architecture) flags |= StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                r.receiveGI = architecture ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                if (architecture) lightmapped++; else probeLit++;
            }
            log.AppendLine($"renderers lightmapped={lightmapped} probe-lit={probeLit}");
        }

        private static bool HasValidLightmapUVs(Renderer r)
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            Vector2[] uv = mf != null && mf.sharedMesh != null ? mf.sharedMesh.uv2 : null;
            if (uv == null || uv.Length == 0) return false;
            foreach (Vector2 u in uv)
                if (float.IsNaN(u.x) || float.IsNaN(u.y) || u.x < 0f || u.y < 0f || u.x > 1f || u.y > 1f) return false;
            return true;
        }

        private static bool IsArchitecture(Renderer r, string baseName)
        {
            if (In(WallNames, baseName) || baseName == "Plane" || In(GlassNames, baseName)) return true;
            if (baseName == "Cube")
            {
                Vector3 s = r.bounds.size;
                return Mathf.Max(s.x, Mathf.Max(s.y, s.z)) >= 1.0f; // walls, partitions, big frames
            }
            return false;
        }

        // --- Colliders ----------------------------------------------------------------------------------------
        private static void AddColliders(GameObject root, StringBuilder log)
        {
            int meshCols = 0;
            var pool = new List<Bounds>();
            GameObject container = new GameObject("NB_Interior_FurnitureColliders");

            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string b = BaseName(r.name);
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                if (b == "Plane")
                {
                    // Full-footprint floor planes face down, so a MeshCollider would not be solid from above. Use a thin box
                    // under each plane instead (top face at the plane's Y) so the corridors are solid and gap-free.
                    Bounds pb = r.bounds;
                    BoxCollider floor = container.AddComponent<BoxCollider>();
                    floor.center = new Vector3(pb.center.x, pb.center.y - 0.15f, pb.center.z);
                    floor.size = new Vector3(pb.size.x, 0.3f, pb.size.z);
                    continue;
                }
                if (In(GlassNames, b) || In(SkipColliderNames, b)) continue;

                bool structural = In(WallNames, b) || (b == "Cube" && IsArchitecture(r, b));
                if (structural)
                {
                    MeshCollider mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                    meshCols++;
                }
                else if (In(FurnitureNames, b) || b == "Cube")
                {
                    Bounds wb = r.bounds;
                    if (Mathf.Max(wb.size.x, Mathf.Max(wb.size.y, wb.size.z)) >= 0.12f) pool.Add(wb);
                }
            }

            // Only items that actually touch are merged (5 cm), and a merged box is capped at 3.5 m per side, so chair rows and
            // benches in neighbouring rows are never chained together across the aisles.
            List<Bounds> boxes = MergeBoxes(pool, 0.05f, 3.5f);
            foreach (Bounds bx in boxes)
            {
                BoxCollider bc = container.AddComponent<BoxCollider>();
                bc.center = bx.center;
                bc.size = bx.size;
            }
            log.AppendLine($"meshColliders={meshCols} boxColliders={boxes.Count + 3} (furniture/row boxes={boxes.Count}, floor boxes=3) from {pool.Count} furniture renderers");
        }

        /// <summary>Greedy merge of touching world-space boxes into table-row / chair-row boxes; capped so aisles stay open.</summary>
        private static List<Bounds> MergeBoxes(List<Bounds> input, float gap, float maxSide)
        {
            var boxes = new List<Bounds>(input);
            boxes.Sort((a, b) => a.min.x.CompareTo(b.min.x));
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < boxes.Count; i++)
                {
                    for (int j = i + 1; j < boxes.Count; j++)
                    {
                        Bounds a = boxes[i], b = boxes[j];
                        if (b.min.x > a.max.x + gap) { /* sorted by min.x, but merged boxes grow, so keep scanning cheaply */ }
                        Bounds grown = a; grown.Expand(gap * 2f);
                        if (!grown.Intersects(b)) continue;
                        Bounds u = a; u.Encapsulate(b);
                        if (u.size.x > maxSide || u.size.z > maxSide) continue;
                        boxes[i] = u;
                        boxes.RemoveAt(j);
                        j--;
                        merged = true;
                    }
                }
            }
            return boxes;
        }

        // --- Rooms ----------------------------------------------------------------------------------------------
        private static List<Room> FindRooms(GameObject root)
        {
            var rooms = new List<Room>();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string b = BaseName(r.name);
                Bounds wb = r.bounds;
                if (b == "Floor")
                    rooms.Add(new Room { Name = r.name, Centre = new Vector3(wb.center.x, wb.max.y, wb.center.z), Size = new Vector2(wb.size.x, wb.size.z), Height = 2.4f });
                else if (b == "Ceiling" && wb.center.y > 7f) // top-floor cabin ceilings
                    rooms.Add(new Room { Name = r.name, Centre = new Vector3(wb.center.x, 5.4f, wb.center.z), Size = new Vector2(wb.size.x, wb.size.z), Height = wb.center.y - 5.4f });
            }
            rooms.Sort((a, b) => a.Centre.y != b.Centre.y ? a.Centre.y.CompareTo(b.Centre.y) : a.Centre.z.CompareTo(b.Centre.z));
            return rooms;
        }

        // --- The authored lighting --------------------------------------------------------------------------------
        private static void AddAuthoredLights(GameObject root, GameObject fbx, StringBuilder log)
        {
            GameObject lightRoot = new GameObject("NB_Interior_Lights_Authored");
            Light first = null;
            int count = 0;
            foreach (Light src in fbx.GetComponentsInChildren<Light>(true))
            {
                GameObject go = new GameObject(src.name);
                go.transform.SetParent(lightRoot.transform, false);
                go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                Light l = go.AddComponent<Light>();
                l.type = src.type;                 // Rectangle, exactly as authored
                l.areaSize = src.areaSize;
                l.color = src.color;
                // The FBX stores Blender's 31.4 (watts). Unity's baked area light is ~10x hotter at that value and blows the
                // rooms out to white, so intensity is scaled by one global factor. Positions, rotation, size and colour stay as authored.
                l.intensity = src.intensity * IntensityScale;
                l.range = src.range;
                l.shadows = LightShadows.Soft;
                l.lightmapBakeType = LightmapBakeType.Baked; // URP renders Rectangle lights only through baking
                if (first == null) first = l;
                count++;
            }
            log.AppendLine($"authored lights copied from FBX={count} (type={first?.type} area={first?.areaSize} intensity={first?.intensity} range={first?.range})");

            // Top-floor fixtures that exist in the GLB but have no authored light: same authored settings, separate group so
            // it can be deleted in one step.
            if (first == null) return;
            GameObject inferred = new GameObject("NB_Interior_Lights_TopFloor_Inferred");
            int extra = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Multiline D-C 600_diff")) continue;
                Vector3 p = r.bounds.center;
                bool covered = false;
                foreach (Light l in lightRoot.GetComponentsInChildren<Light>(true))
                    if (Vector3.Distance(l.transform.position, p) < 0.5f) { covered = true; break; }
                if (covered) continue;
                GameObject go = new GameObject($"TopFloorLight_{extra:00}");
                go.transform.SetParent(inferred.transform, false);
                go.transform.SetPositionAndRotation(p, first.transform.rotation);
                Light nl = go.AddComponent<Light>();
                nl.type = first.type; nl.areaSize = first.areaSize; nl.color = first.color;
                nl.intensity = first.intensity; nl.range = first.range; // 'first' is already scaled
                nl.shadows = LightShadows.Soft; nl.lightmapBakeType = LightmapBakeType.Baked;
                extra++;
            }
            log.AppendLine($"top-floor fixtures without an authored light (same settings applied)={extra}");
        }

        private static void AddProbesAndCulling(List<Room> rooms, StringBuilder log)
        {
            GameObject probesRoot = new GameObject("NB_Interior_Probes");
            GameObject cullingRoot = new GameObject("NB_Interior_Culling");
            var lp = new List<Vector3>();
            for (int i = 0; i < rooms.Count; i++)
            {
                Room rm = rooms[i];
                Vector3 mid = rm.Centre + Vector3.up * rm.Height * 0.5f;
                GameObject po = new GameObject($"ReflectionProbe_{i + 1:00}");
                po.transform.SetParent(probesRoot.transform, false);
                po.transform.position = mid;
                ReflectionProbe p = po.AddComponent<ReflectionProbe>();
                p.mode = ReflectionProbeMode.Baked;
                p.boxProjection = true;
                p.resolution = 128;
                p.size = new Vector3(rm.Size.x, rm.Height, rm.Size.y);

                GameObject oa = new GameObject($"OcclusionArea_{i + 1:00}");
                oa.transform.SetParent(cullingRoot.transform, false);
                oa.transform.position = mid;
                oa.AddComponent<OcclusionArea>().size = new Vector3(rm.Size.x, rm.Height, rm.Size.y);

                for (int ix = 0; ix < 3; ix++)
                for (int iz = 0; iz < 3; iz++)
                    lp.Add(new Vector3(rm.Centre.x + (ix - 1) * rm.Size.x * 0.3f, rm.Centre.y + 1.2f, rm.Centre.z + (iz - 1) * rm.Size.y * 0.3f));
            }
            GameObject lpg = new GameObject("NB_Interior_LightProbes");
            lpg.AddComponent<LightProbeGroup>().probePositions = lp.ToArray();
            log.AppendLine($"reflection probes={rooms.Count} light probes={lp.Count}");
        }

        // --- Player ---------------------------------------------------------------------------------------------
        // --- Room identity (classrooms vs labs vs cabins) -------------------------------------------------------
        // Data-driven: a room with SystemSet (computer workstation) pieces is a lab; the top-level cabins (low ceilings above
        // y 7 m) are faculty cabins; everything else is a classroom. The building has 12 classrooms (7.7 x 8.1 m), 4 labs
        // (8.3 x 12.1 m) and 6 cabins. "Floor" (ground floor, z ~ 1.1) is the AR-VR lab used by the navigator, as in the
        // earlier NB_Interior_Lite layout (same blend, offset +4.8 X / -6.0 Z).
        private const string ARVRLabFloorName = "Floor";
        private const string NavigatorClassroomFloorName = "Floor.006";
        public const string StandardClassroomKey = "nb-classroom-2";
        private const string NavigatorCabinName = "Ceiling.016";

        private static string LevelName(int level) => level == 0 ? "Ground Floor" : level == 1 ? "Level 1" : "4th Floor";

        private static List<Room> ClassifyRooms(GameObject root, List<Room> rooms)
        {
            int[] systemSets = new int[rooms.Count];
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (BaseName(r.name) != "SystemSet") continue;
                int idx = RoomIndex(rooms, r.bounds.center);
                if (idx >= 0) systemSets[idx]++;
            }

            var counters = new Dictionary<string, int>();
            for (int i = 0; i < rooms.Count; i++)
            {
                Room rm = rooms[i];
                rm.Level = rm.Centre.y < 1f ? 0 : rm.Centre.y < 4f ? 1 : 2;
                bool cabin = rm.Name.StartsWith("Ceiling");
                NBRoomType type = cabin ? NBRoomType.FacultyCabin
                                : rm.Name == ARVRLabFloorName ? NBRoomType.ARVRLab
                                : systemSets[i] > 0 ? NBRoomType.Lab : NBRoomType.Classroom;
                rm.Type = type;
                string key = type + "_" + rm.Level;
                counters[key] = counters.TryGetValue(key, out int n) ? n + 1 : 1;
                string seq = counters[key].ToString("00");
                switch (type)
                {
                    case NBRoomType.ARVRLab: rm.Label = "ARVRLab"; rm.Display = "AR-VR Lab (" + LevelName(rm.Level) + ")"; break;
                    case NBRoomType.Lab: rm.Label = $"Lab_L{rm.Level}_{seq}"; rm.Display = "Computer Lab (" + LevelName(rm.Level) + ")"; break;
                    case NBRoomType.FacultyCabin: rm.Label = $"FacultyCabin_{seq}"; rm.Display = "Faculty Cabin " + seq; break;
                    default: rm.Label = $"Classroom_L{rm.Level}_{seq}"; rm.Display = "Classroom (" + LevelName(rm.Level) + ") " + seq; break;
                }
                // The navigator's smart classroom is NB-105 (placed separately, key nb-classroom). The "standard classroom" card
                // (nb-classroom-2) is one of the building's own classrooms; the other classrooms carry no navigator key.
                rm.NavKey = rm.Name == ARVRLabFloorName ? "nb-lab"
                          : rm.Name == NavigatorCabinName ? "nb-faculty"
                          : rm.Name == NavigatorClassroomFloorName && type == NBRoomType.Classroom ? StandardClassroomKey : "";
                rooms[i] = rm;
            }
            return rooms;
        }

        private static void AddRoomMarkers(List<Room> rooms)
        {
            GameObject parent = new GameObject("NB_Interior_Rooms");
            foreach (Room rm in rooms)
            {
                GameObject go = new GameObject(rm.Label);
                go.transform.SetParent(parent.transform, false);
                go.transform.position = rm.Centre + Vector3.up * rm.Height * 0.5f;
                NBRoomInfo info = go.AddComponent<NBRoomInfo>();
                SerializedObject so = new SerializedObject(info);
                so.FindProperty("m_Type").enumValueIndex = (int)rm.Type;
                so.FindProperty("m_DisplayName").stringValue = rm.Display;
                so.FindProperty("m_Level").intValue = rm.Level;
                so.FindProperty("m_Size").vector2Value = rm.Size;
                so.FindProperty("m_NavigatorKey").stringValue = rm.NavKey;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // --- Navigator pods ---------------------------------------------------------------------------------------
        // CampusTourManager asks SceneTransitionManager for rooms by key; it loads Scene_NorthBlock_Interiors and moves the XR
        // rig to the PrimarySpawnAnchor of the RoomPodAnchorController whose key matches. These three pods keep the exact
        // names/keys the navigator already uses.
        private static void AddNavigatorPods(List<Room> rooms, Transform nb105Anchor, StringBuilder log)
        {
            Physics.SyncTransforms();
            GameObject podsParent = new GameObject("NB_Interior_NavigatorPods");
            var handles = new List<Vector3>(); // door handles are not needed: the corridor-facing wall rule is used

            // nb-classroom = NB-105 "Smart Case Study Room": the pod's spawn is NB-105's own entrance anchor.
            if (nb105Anchor != null)
            {
                GameObject classPod = new GameObject("RoomPod_SmartClassroom");
                classPod.transform.SetParent(podsParent.transform, false);
                classPod.transform.position = nb105Anchor.position;
                RoomPodAnchorController cc = classPod.AddComponent<RoomPodAnchorController>();
                SerializedObject cso = new SerializedObject(cc);
                cso.FindProperty("m_PodKey").stringValue = "nb-classroom";
                cso.FindProperty("m_PodDisplayName").stringValue = "Smart Case Study Room (NB-105)";
                cso.FindProperty("m_PrimarySpawnAnchor").objectReferenceValue = nb105Anchor;
                cso.FindProperty("m_FallbackRecoveryAnchor").objectReferenceValue = nb105Anchor;
                cso.FindProperty("m_AlternateAnchors").arraySize = 0;
                cso.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"navigator pod RoomPod_SmartClassroom [nb-classroom] -> Smart Case Study Room (NB-105) spawn={nb105Anchor.position} yaw={nb105Anchor.eulerAngles.y:F0}");
            }
            else log.AppendLine("navigator pod RoomPod_SmartClassroom: NB-105 anchor not found");

            foreach (var spec in new[]
            {
                new { Pod = "RoomPod_RoboticsLab",        Key = "nb-lab",            Anchor = "Anchor_ARVRLab_Entrance" },
                new { Pod = "RoomPod_FacultyCabins",      Key = "nb-faculty",        Anchor = "Anchor_FacultyCabin_Entrance" },
                new { Pod = "RoomPod_StandardClassroom",  Key = StandardClassroomKey, Anchor = "Anchor_StandardClassroom_Entrance" },
            })
                MakeNavigatorPod(podsParent.transform, rooms, spec.Pod, spec.Key, spec.Anchor, handles, log);

            // One recovery floor under the whole building. It sits outside every pod, so OutOfBoundsSafetyTrigger sends the
            // player back to SceneTransitionManager.CurrentAnchorTransform (the room they entered), not a fixed pod.
            GameObject safety = new GameObject("NB_Interior_OutOfBounds");
            safety.transform.position = new Vector3(4.8f, -4f, -6f);
            BoxCollider col = safety.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(120f, 1f, 160f);
            safety.AddComponent<OutOfBoundsSafetyTrigger>();
        }

        private static void AddPlayer(GameObject root, List<Room> rooms, StringBuilder log)
        {
            Physics.SyncTransforms();
            // Count furniture per room to identify the classroom (StudentChair) and the AR-VR lab (SystemSet / LabChair).
            int[] student = new int[rooms.Count], lab = new int[rooms.Count];
            var handles = new List<Vector3>();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string b = BaseName(r.name);
                Vector3 c = r.bounds.center;
                if (b.StartsWith("DoorHandle")) { handles.Add(c); continue; }
                if (b != "StudentChair" && b != "SystemSet" && b != "LabChair") continue;
                int idx = RoomIndex(rooms, c);
                if (idx < 0) continue;
                if (b == "StudentChair") student[idx]++; else lab[idx]++;
            }
            // Every lab (8.3 x 12.1 m: 80 student chairs + 40 system sets) and classroom (7.7 x 8.1 m: 60 chairs) repeats on
            // several floors, so furniture counts cannot pick one. These are the ground-floor rooms that match the AR-VR lab
            // and classroom of the earlier NB_Interior_Lite layout (same blend, offset +4.8 X / -6.0 Z).
            int arvr = rooms.FindIndex(r => r.Name == "Floor");
            int classroom = rooms.FindIndex(r => r.Name == "Floor.006");
            log.AppendLine($"AR-VR lab room={(arvr >= 0 ? rooms[arvr].Name : "n/a")} classroom room={(classroom >= 0 ? rooms[classroom].Name : "n/a")}");

            GameObject anchors = new GameObject("NB_Interior_Anchors");
            Transform cAnchor = MakeEntranceAnchor(anchors.transform, "Anchor_Classroom_Entrance", rooms, classroom, handles);
            Transform lAnchor = MakeEntranceAnchor(anchors.transform, "Anchor_ARVRLab_Entrance", rooms, arvr, handles);

            GameObject player = new GameObject("Player");
            player.transform.SetPositionAndRotation(cAnchor.position, cAnchor.rotation);
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.7f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.85f, 0f);
            cc.stepOffset = 0.3f; cc.slopeLimit = 45f; cc.skinWidth = 0.03f;

            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            GameObject camObj = new GameObject("Main Camera") { tag = "MainCamera" };
            camObj.transform.SetParent(head.transform, false);
            Camera cam = camObj.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 40f; cam.fieldOfView = 70f;
            camObj.AddComponent<AudioListener>();

            NBInteriorPlayer walker = player.AddComponent<NBInteriorPlayer>();
            SerializedObject so = new SerializedObject(walker);
            so.FindProperty("m_Head").objectReferenceValue = head.transform;
            SerializedProperty tp = so.FindProperty("m_Teleports");
            tp.arraySize = 2;
            SetTeleport(tp.GetArrayElementAtIndex(0), "AR-VR Lab", (int)UnityEngine.InputSystem.Key.Digit1, lAnchor);
            SetTeleport(tp.GetArrayElementAtIndex(1), "Classroom", (int)UnityEngine.InputSystem.Key.Digit2, cAnchor);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void MakeNavigatorPod(Transform podsParent, List<Room> rooms, string podName, string key, string anchorName, List<Vector3> handles, StringBuilder log)
        {
            int idx = rooms.FindIndex(r => r.NavKey == key);
            if (idx < 0) { log.AppendLine($"navigator pod '{podName}': no room found for key {key}"); return; }

            GameObject pod = new GameObject(podName);
            pod.transform.SetParent(podsParent, false);
            pod.transform.position = rooms[idx].Centre;
            Transform anchor = MakeEntranceAnchor(pod.transform, anchorName, rooms, idx, handles);

            RoomPodAnchorController controller = pod.AddComponent<RoomPodAnchorController>();
            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("m_PodKey").stringValue = key;
            so.FindProperty("m_PodDisplayName").stringValue = rooms[idx].Display;
            so.FindProperty("m_PrimarySpawnAnchor").objectReferenceValue = anchor;
            so.FindProperty("m_FallbackRecoveryAnchor").objectReferenceValue = anchor;
            so.FindProperty("m_AlternateAnchors").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"navigator pod {podName} [{key}] -> {rooms[idx].Display} spawn={anchor.position} yaw={anchor.eulerAngles.y:F0}");
        }

        /// <summary>
        /// Adds RoomPod_StandardClassroom [nb-classroom-2] to the OPEN Scene_NorthBlock_Interiors without rebuilding it (a rebuild
        /// would throw the lightmap bake away). Replaces an existing pod of that name.
        /// </summary>
        [MenuItem("BMU VR Tour/Add Standard Classroom Pod To Open NB Interior Scene", false, 19)]
        public static void AddStandardClassroomPodToOpenScene()
        {
            GameObject root = GameObject.Find("NB_Interior");
            GameObject podsParent = GameObject.Find("NB_Interior_NavigatorPods");
            if (root == null || podsParent == null) { Debug.LogError("[NBBuildingInteriorSetup] Open Scene_NorthBlock_Interiors first."); return; }

            Transform old = podsParent.transform.Find("RoomPod_StandardClassroom");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            Physics.SyncTransforms();
            StringBuilder log = new StringBuilder();
            List<Room> rooms = ClassifyRooms(root, FindRooms(root));
            MakeNavigatorPod(podsParent.transform, rooms, "RoomPod_StandardClassroom", StandardClassroomKey, "Anchor_StandardClassroom_Entrance", new List<Vector3>(), log);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[NBBuildingInteriorSetup] " + log);
        }

        private static int RoomIndex(List<Room> rooms, Vector3 p)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                Room r = rooms[i];
                if (Mathf.Abs(p.x - r.Centre.x) <= r.Size.x * 0.5f && Mathf.Abs(p.z - r.Centre.z) <= r.Size.y * 0.5f &&
                    p.y >= r.Centre.y - 0.2f && p.y <= r.Centre.y + r.Height) return i;
            }
            return -1;
        }

        private static int ArgMax(int[] a)
        {
            int best = -1, bv = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] > bv) { bv = a[i]; best = i; }
            return best;
        }

        // Spawn just inside the room at its door handles, facing the room centre; nudged to clear floor.
        private static Transform MakeEntranceAnchor(Transform parent, string name, List<Room> rooms, int roomIdx, List<Vector3> handles)
        {
            GameObject a = new GameObject(name);
            a.transform.SetParent(parent, false);
            if (roomIdx < 0) return a.transform;
            Room rm = rooms[roomIdx];

            // The rooms form two columns either side of a central corridor (door handles / glass door walls sit at x ~ 3.9),
            // so a room's door is on the wall that faces the corridor. Spawn 1 m inside that wall, looking into the room.
            const float CorridorX = 5.1f;
            float sign = rm.Centre.x < CorridorX ? 1f : -1f;
            Vector3 toCentre = new Vector3(-sign, 0f, 0f);
            Vector3 pos = new Vector3(rm.Centre.x + sign * (rm.Size.x * 0.5f - 1.0f), rm.Centre.y + 0.05f, rm.Centre.z);

            // Nudge to the nearest spot with room for the 0.3 m capsule.
            Vector3 bestPos = pos; float bestDist = float.MaxValue;
            for (float dx = -4.5f; dx <= 4.5f; dx += 0.25f)
            for (float dz = -5.5f; dz <= 5.5f; dz += 0.25f)
            {
                Vector3 c = pos + new Vector3(dx, 0f, dz);
                if (RoomIndex(rooms, c + Vector3.up * 0.5f) != roomIdx) continue;
                if (Physics.CheckCapsule(c + Vector3.up * 0.4f, c + Vector3.up * 1.4f, 0.32f)) continue; // player radius 0.3 + skin
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; bestPos = c; }
            }
            // Face into the room, but if that way is blocked (benches / desks right in front), face the direction with the most
            // walkable floor instead (capsule cast = same size as the player).
            Vector3 facing = toCentre;
            float bestFree = FreeDistance(bestPos, toCentre);
            if (bestFree < 2.0f)
            {
                foreach (Vector3 d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    float free = FreeDistance(bestPos, d);
                    if (free > bestFree + 0.25f) { bestFree = free; facing = d; }
                }
            }
            a.transform.SetPositionAndRotation(bestPos, Quaternion.LookRotation(facing, Vector3.up));
            SpawnPointOverrides.TryApply(a.transform); // hand-tuned spawns (e.g. Standard Classroom), see SpawnPointOverrides
            return a.transform;
        }

        private static float FreeDistance(Vector3 from, Vector3 dir)
        {
            Vector3 a = from + Vector3.up * 0.4f, b = from + Vector3.up * 1.4f;
            return Physics.CapsuleCast(a, b, 0.3f, dir, out RaycastHit hit, 6f) ? hit.distance : 6f;
        }

        private static void SetTeleport(SerializedProperty p, string label, int key, Transform anchor)
        {
            p.FindPropertyRelative("Label").stringValue = label;
            p.FindPropertyRelative("Hotkey").intValue = key;
            p.FindPropertyRelative("Anchor").objectReferenceValue = anchor;
        }

        // --- Bake settings --------------------------------------------------------------------------------------
        private static void ConfigureLightingSettings()
        {
            LightingSettings ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "NB_Building_Interior_Lighting" };
                string dir = Path.GetDirectoryName(LightingSettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(ls, LightingSettingsPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            // CPU lightmapper: the GPU one crashed the Editor on a 4 GB laptop GPU with ~3,800 static renderers / 1M triangles.
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            ls.lightmapResolution = 6f;        // texels per metre: modest, interior is mostly flat surfaces
            ls.lightmapPadding = 2;
            ls.lightmapMaxSize = 1024;
            ls.directSampleCount = 24;
            ls.indirectSampleCount = 64;
            ls.environmentSampleCount = 32;
            ls.maxBounces = 2;
            ls.directionalityMode = LightmapsMode.NonDirectional; // cheapest, Quest friendly
            ls.ao = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }
    }
}
