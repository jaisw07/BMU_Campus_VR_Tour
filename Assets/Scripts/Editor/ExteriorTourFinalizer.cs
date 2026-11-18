using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Prepares Scene_Campus_Exterior.unity for the exterior-only free-roam VR tour without wiping
    /// manually placed buildings or UI_popup* / UI popup* zones:
    /// 1. Converts UI_popup* / UI popup* planes into glowing holographic indicators + trigger colliders.
    /// 2. Adds solid non-trigger colliders to all building meshes (preventing interior entry/wall clipping).
    /// 3. Disables teleportation pads/areas in the exterior scene.
    /// 4. Applies the panoramic skybox and creates invisible [World_Border_Walls] perimeter colliders.
    /// </summary>
    public static class ExteriorTourFinalizer
    {
        private const string ExteriorScenePath = "Assets/Scenes/Scene_Campus_Exterior.unity";
        private const string HologramShaderName = "BMU/HologramZone";
        private const string HologramMaterialPath = "Assets/Materials/M_UI_Popup_Hologram.mat";
        private const string HologramRingMaterialPath = "Assets/Materials/M_UI_Popup_Hologram_Ring.mat";

        private static readonly string[] BuildingRootNames =
        {
            "NorthBlock_Exterior",
            "Gateway",
            "BMU_Workshop",
            "Gargi_Hostel",
            "VikramSarabhai_Hostel",
            "BML_BhagatSingh_Hostel",
            "Bhagat_Singh_Hostel_Building",
            "BML_Library_Medical",
            "Library_Medical_Building_HD2",
            "BML_MaggiPoint",
            "BML_DominosPoint",
            "BML_BurgerPoint",
            "BML_XpressCafe",
            "Hostels",
            "MainGate",
            "Main_Gate",
            "Campus_Entrance"
        };

        [MenuItem("BMU VR Tour/Finalize Exterior Scene (Single-Scene Setup)", false, 2)]
        public static void FinalizeExteriorScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(ExteriorScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[ExteriorTourFinalizer] Could not open '{ExteriorScenePath}'.");
                return;
            }

            // Ensure solid building & ground colliders first so raycasts hit real ground
            int colliderCount = EnsureSolidBuildingColliders(scene);
            Physics.SyncTransforms();

            // Restore calibrated building heights & build connected 2-lane roads matching road_0001
            int groundedCount = GroundBuildingsAndSealGaps(scene);
            Physics.SyncTransforms();
            int roadSegments = BuildCampusRoadNetwork(scene);
            Physics.SyncTransforms();

            Material beaconMat = GetOrCreateHologramMaterial(HologramMaterialPath, new Color(0.01f, 0.58f, 0.95f, 0.48f), new Color(0.35f, 0.90f, 1.0f, 0.95f), 2.2f, 14f, 1.8f);
            Material ringMat = GetOrCreateHologramMaterial(HologramRingMaterialPath, new Color(0.05f, 0.78f, 1.0f, 0.85f), new Color(0.55f, 0.95f, 1.0f, 1.0f), 3.0f, 4f, 0.5f);

            // Place/snap UI_popup zones BEFORE spawning XR Rig so XR Rig reads calibrated Main Gate location
            int triggerCount = SetupHologramTriggerZones(scene, beaconMat, ringMat);
            Physics.SyncTransforms();

            // Install XR Rig (both-stick walking), EventSystem, ScreenFade, and simplified Spatial UI directly in Scene_Campus_Exterior
            BMUTourSceneBuilder.InstallSingleSceneTourIntoActiveScene();
            int disabledTpCount = DisableExteriorTeleportation(scene);

            // Apply panoramic skybox
            Material skyboxMat = CampusSkyboxSetup.CreateOrUpdateSkyboxMaterial();
            if (skyboxMat != null)
            {
                CampusSkyboxSetup.ApplySkyboxToExteriorScene(skyboxMat);
            }

            // Reload active reference in case ApplySkybox saved
            scene = SceneManager.GetActiveScene();
            Bounds worldBounds = CreateWorldBorderWalls(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ExteriorTourFinalizer] Single-scene tour ready in '{ExteriorScenePath}': {triggerCount} holographic UI_popup zones, {groundedCount} buildings grounded/sealed, {roadSegments} road/plaza segments built, {colliderCount} solid building/ground colliders added, {disabledTpCount} teleport interactables disabled, world border bounds = {worldBounds.min} to {worldBounds.max}.");
        }

        private static readonly (string name, Vector3 pos, Vector3 scale)[] DefaultPopupZones =
        {
            ("UI_popup_North_Block",    new Vector3(-476f,   2.3f, -136.6f), new Vector3(17.408304f, 1f, 1f)),
            ("UI_popup_VSH",            new Vector3(-915f,   2.3f, -267.8f), new Vector3(8.71303f,   1f, 1f)),
            ("UI_popup_Gargi",          new Vector3(-705f,   2.3f, -162.0f), new Vector3(8.71303f,   1f, 1f)),
            ("UI_popup_Gateway",        new Vector3(-118f,   0.0f,  -88.4f), new Vector3(1.5756041f, 1f, 16.794544f)),
            ("UI_popup_Library",        new Vector3(-140.5f, 2.3f, -188.5f), new Vector3(9.315496f,  1f, 1f)),
            ("UI_popup_Food_Outlet",    new Vector3(-314f,   0.0f,  -98.0f), new Vector3(1.2f,       1f, 1.2f)),
            ("UI_popup_Mechanical_Lab", new Vector3(-402.4f, 2.3f, -279.5f), new Vector3(26.183065f, 1f, 1f)),
            ("UI_popup_BSH",            new Vector3(-705f,   2.3f, -242.0f), new Vector3(17.408304f, 1f, 1f)),
            ("UI_popup_Main_Gate",      new Vector3(48.0f,   0.0f,   -2.0f), new Vector3(0.8f,       1f, 1.2f)),
        };

        // ====================================================================
        // 1. HOLOGRAPHIC UI_POPUP TRIGGER ZONES
        // ====================================================================
        private static int SetupHologramTriggerZones(Scene scene, Material beaconMat, Material ringMat)
        {
            var targets = new List<GameObject>();
            var existingKeys = new HashSet<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (IsUIPopupName(tr.name))
                    {
                        targets.Add(tr.gameObject);
                        UIPopupTriggerZone.ResolveKeyFromName(tr.name, out string k, out _);
                        if (!string.IsNullOrEmpty(k)) existingKeys.Add(k);
                    }
                }
            }

            // Recreate any missing UI_popup zones from the calibrated campus positions
            foreach (var def in DefaultPopupZones)
            {
                UIPopupTriggerZone.ResolveKeyFromName(def.name, out string defKey, out _);
                if (!string.IsNullOrEmpty(defKey) && existingKeys.Contains(defKey)) continue;

                GameObject zoneObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
                zoneObj.name = def.name;
                zoneObj.transform.position = def.pos;
                zoneObj.transform.localScale = def.scale;
                targets.Add(zoneObj);
                existingKeys.Add(defKey);
            }

            int count = 0;
            foreach (GameObject go in targets)
            {
                UIPopupTriggerZone.ResolveKeyFromName(go.name, out string locKey, out bool isMainGate);
                go.name = NormalizePopupName(go.name);

                // Remove flat plane renderer & non-trigger mesh collider from the root
                MeshRenderer rootMr = go.GetComponent<MeshRenderer>();
                if (rootMr != null) UnityEngine.Object.DestroyImmediate(rootMr);
                MeshFilter rootMf = go.GetComponent<MeshFilter>();
                if (rootMf != null) UnityEngine.Object.DestroyImmediate(rootMf);
                MeshCollider oldMc = go.GetComponent<MeshCollider>();
                if (oldMc != null) UnityEngine.Object.DestroyImmediate(oldMc);

                // Compute desired trigger footprint: preserve existing trigger BoxCollider size on re-runs,
                // otherwise derive from original plane scale (Unity Plane = 10x10).
                BoxCollider existingTrigger = go.GetComponent<UIPopupTriggerZone>() != null ? go.GetComponent<BoxCollider>() : null;
                Vector3 lossy = go.transform.lossyScale;
                float worldWidth = existingTrigger != null ? existingTrigger.size.x : Mathf.Clamp(Mathf.Abs(lossy.x) * 10f, 5f, 40f);
                float worldDepth = existingTrigger != null ? existingTrigger.size.z : Mathf.Clamp(Mathf.Abs(lossy.z) * 10f, 5f, 40f);

                // Reset scale to (1,1,1) so child hologram proportions stay uniform and predictable
                go.transform.localScale = Vector3.one;
                go.transform.rotation = Quaternion.identity;

                // Snap Y to actual walkable ground surface underneath (ignoring building roofs/trees!)
                Vector3 pos = go.transform.position;
                if (go.name == "UI_popup_Food_Outlet")
                {
                    pos.x = -314f;
                    pos.z = -98f;
                }
                else if (go.name == "UI_popup_Main_Gate")
                {
                    pos.x = 48f;
                    pos.z = -2f;
                }
                else if (go.name == "UI_popup_Gateway")
                {
                    pos.x = -118f;
                    pos.z = -88.4f;
                }
                else if (go.name == "UI_popup_Gargi")
                {
                    pos.x = -705f;
                    pos.z = -162f;
                }
                else if (go.name == "UI_popup_BSH")
                {
                    pos.x = -705f;
                    pos.z = -242f;
                }
                float groundY = SampleGroundHeight(pos, go);
                pos.y = groundY;
                go.transform.position = pos;

                // Trigger BoxCollider covering player walking height cleanly
                BoxCollider triggerCol = go.GetComponent<BoxCollider>();
                if (triggerCol == null) triggerCol = go.AddComponent<BoxCollider>();
                triggerCol.isTrigger = true;
                triggerCol.center = new Vector3(0f, 2.5f, 0f);
                triggerCol.size = new Vector3(Mathf.Max(6f, worldWidth), 8.0f, Mathf.Max(6f, worldDepth));

                // Clean previous HologramVisual if re-running
                Transform existingVis = go.transform.Find("HologramVisual");
                if (existingVis != null) UnityEngine.Object.DestroyImmediate(existingVis.gameObject);

                GameObject visRoot = new GameObject("HologramVisual");
                visRoot.transform.SetParent(go.transform, false);

                float ringDiameter = Mathf.Clamp(Mathf.Min(worldWidth, worldDepth) * 0.45f, 3.0f, 6.0f);

                // Outer Translucent Hologram Beacon Cylinder
                GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beacon.name = "HologramBeacon";
                beacon.transform.SetParent(visRoot.transform, false);
                beacon.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                beacon.transform.localScale = new Vector3(ringDiameter, 1.6f, ringDiameter);
                RemoveAllColliders(beacon);
                ConfigureHologramRenderer(beacon.GetComponent<MeshRenderer>(), beaconMat);

                // Ground Hologram Ring (thin flat cylinder at ground level)
                GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "HologramGroundRing";
                ring.transform.SetParent(visRoot.transform, false);
                ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                ring.transform.localScale = new Vector3(ringDiameter * 1.15f, 0.04f, ringDiameter * 1.15f);
                RemoveAllColliders(ring);
                ConfigureHologramRenderer(ring.GetComponent<MeshRenderer>(), ringMat);

                // Floating Hologram Diamond Marker above center
                GameObject diamond = GameObject.CreatePrimitive(PrimitiveType.Cube);
                diamond.name = "HologramMarker";
                diamond.transform.SetParent(visRoot.transform, false);
                diamond.transform.localPosition = new Vector3(0f, 2.6f, 0f);
                diamond.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
                diamond.transform.localScale = Vector3.one * 0.65f;
                RemoveAllColliders(diamond);
                ConfigureHologramRenderer(diamond.GetComponent<MeshRenderer>(), ringMat);

                // Attach / update UIPopupTriggerZone
                UIPopupTriggerZone zone = go.GetComponent<UIPopupTriggerZone>();
                if (zone == null) zone = go.AddComponent<UIPopupTriggerZone>();
                zone.LocationKey = locKey;
                zone.IsMainGate = isMainGate;
                zone.AnimatedRing = diamond.transform;

                EditorUtility.SetDirty(go);
                count++;
            }

            return count;
        }

        private static bool IsUIPopupName(string objName)
        {
            if (string.IsNullOrEmpty(objName)) return false;
            return objName.StartsWith("UI_popup", StringComparison.OrdinalIgnoreCase) ||
                   objName.StartsWith("UI popup", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePopupName(string objName)
        {
            string trimmed = objName.Trim();
            if (trimmed.StartsWith("UI popup", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "UI_popup" + trimmed.Substring("UI popup".Length);
            }
            return trimmed.Replace(' ', '_');
        }

        private static float SampleGroundHeight(Vector3 pos, GameObject ignoreObj = null)
        {
            Ray ray = new Ray(new Vector3(pos.x, 35f, pos.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 80f);
            float bestRealY = float.MinValue;
            bool foundReal = false;
            float bestBackupY = 0f;
            bool foundBackup = false;

            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (ignoreObj != null && hit.collider.transform.IsChildOf(ignoreObj.transform)) continue;

                GameObject hitGo = hit.collider.gameObject;
                if (hit.point.y < -2.5f || hit.point.y > 2.5f) continue;

                if (hitGo.name == "Backup_Walkable_Ground")
                {
                    if (!foundBackup || hit.point.y > bestBackupY)
                    {
                        foundBackup = true;
                        bestBackupY = hit.point.y;
                    }
                    continue;
                }

                bool isGround = hit.collider is TerrainCollider || IsGroundOrRoadMesh(hitGo);
                if (!isGround) continue;

                // Pick the highest visible walkable surface at (x, z) so rings & roads sit on top of grass/paving
                if (!foundReal || hit.point.y > bestRealY)
                {
                    foundReal = true;
                    bestRealY = hit.point.y;
                }
            }
            if (foundReal) return bestRealY;
            return foundBackup ? bestBackupY : 0.0f;
        }

        // ====================================================================
        // 1B. RESTORE CALIBRATED BUILDING HEIGHTS & BUILD MATCHING ROADS
        // ====================================================================
        private static int GroundBuildingsAndSealGaps(Scene scene)
        {
            GameObject roadsRoot = GameObject.Find("[Campus_Roads_And_Plazas]");
            if (roadsRoot != null) UnityEngine.Object.DestroyImmediate(roadsRoot);
            roadsRoot = new GameObject("[Campus_Roads_And_Plazas]");

            // Restore original calibrated scene Y elevations; lift campus_entrance_v2 so its forecourt road aligns at y=0
            var calibratedY = new (string name, float y)[]
            {
                ("NorthBlock_Exterior",      0.8749962f),
                ("Gateway",                  0.0f),
                ("BMU_Workshop",             0.0f),
                ("Gargi_Hostel",             0.0f),
                ("VikramSarabhai_Hostel",    0.0f),
                ("BML_BhagatSingh_Hostel",   0.0f),
                ("BML_Library_Medical",      0.0f),
                ("BML_MaggiPoint",           0.0f),
                ("BML_XpressCafe",           0.0f),
                ("BML_DominosPoint",         0.0f),
                ("BML_BurgerPoint",          0.0f),
                ("campus_entrance_v2",      -0.05f)
            };

            int count = 0;
            foreach (var entry in calibratedY)
            {
                GameObject obj = GameObject.Find(entry.name);
                if (obj == null) continue;
                Vector3 p = obj.transform.position;
                if (!Mathf.Approximately(p.y, entry.y))
                {
                    p.y = entry.y;
                    obj.transform.position = p;
                    EditorUtility.SetDirty(obj);
                }
                count++;
            }

            return count;
        }

        private static int BuildCampusRoadNetwork(Scene scene)
        {
            // Keep EasyRoads "Road Network" / "road_0001" (the textured road between NB & Food Place) ENABLED and intact!
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Road Network" || root.name == "road_0001")
                {
                    foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = true;
                    foreach (var col in root.GetComponentsInChildren<Collider>(true)) col.enabled = true;
                }
            }

            GameObject roadsRoot = GameObject.Find("[Campus_Roads_And_Plazas]");
            if (roadsRoot == null) roadsRoot = new GameObject("[Campus_Roads_And_Plazas]");

            // Match the exact 2-lane EasyRoads texture used on road_0001 between NB and Food Place
            const string erRoadTexPath = "Assets/EasyRoads3D/textures/roads/road2Lane_A.tga";
            const string erRoadNormalPath = "Assets/EasyRoads3D/textures/roads/road_N.png";
            Material roadMat = GetOrCreateTexturedSurfaceMaterial(
                "Assets/Materials/M_Campus_Asphalt_Road.mat",
                FileExistsAsset(erRoadTexPath) ? erRoadTexPath : "Assets/Textures/Campus/T_Campus_Asphalt.png",
                Color.white,
                Vector2.one,
                FileExistsAsset(erRoadNormalPath) ? erRoadNormalPath : null);

            // Connected 2-lane road network extending seamlessly from road_0001 and campus_entrance_v2 without cutting through ANY building/courtyard/court
            var segments = new (string name, Vector2 a, Vector2 b, float width, float? overrideYA, float? overrideYB, bool isWalkway)[]
            {
                ("Road_Entrance_Plaza_Join",   new Vector2(-108.0f, -12.0f),   new Vector2(-118.0f,  -12.0f), 11.5f, 0.08f, 0.08f, false),
                ("Road_Entrance_To_Gateway",   new Vector2(-118.0f, -12.0f),   new Vector2(-118.0f,  -88.4f), 11.5f, 0.08f, 0.14f, false),
                ("Road_Gateway_To_Library",    new Vector2(-118.0f, -88.4f),   new Vector2(-118.0f, -185.5f), 10.5f, 0.14f, 0.36f, false),
                ("Road_Library_Corridor",      new Vector2(-118.0f,-185.5f),   new Vector2(-262.0f, -185.5f), 10.5f, 0.36f, 0.44f, false),
                ("Road_WestGateway_To_Food",   new Vector2(-262.0f,-185.5f),   new Vector2(-262.0f, -138.0f), 10.5f, 0.44f, 0.46f, false),
                ("Road_Food_SouthCorridor",    new Vector2(-262.0f,-138.0f),   new Vector2(-320.0f, -138.0f), 10.5f, 0.46f, 0.46f, false),
                ("Road_SouthBend_To_FoodRoad", new Vector2(-320.0f,-138.0f),   new Vector2(-372.0f, -126.5f), 11.5f, 0.46f, 1.00f, false),
                ("Walkway_FoodCourt_Entry",    new Vector2(-314.0f,-138.0f),   new Vector2(-314.0f,  -95.0f),  8.0f, 0.44f, 0.44f, true),
                ("Road_Workshop_Avenue",       new Vector2(-402.4f,-129.0f),   new Vector2(-402.4f, -280.5f), 11.0f, 1.02f, 0.44f, false),
                ("Road_Workshop_PastCourts",   new Vector2(-402.4f,-280.5f),   new Vector2(-545.0f, -280.5f), 10.5f, 0.44f, 0.46f, false),
                ("Road_Workshop_To_BSH",       new Vector2(-545.0f,-280.5f),   new Vector2(-705.0f, -242.0f), 10.5f, 0.46f, 0.46f, false),
                ("Road_Hostel_Gargi_East",     new Vector2(-669.5f,-115.8f),   new Vector2(-669.5f, -162.0f), 11.0f, 1.07f, 0.46f, false),
                ("Road_Hostel_Gargi_Front",    new Vector2(-669.5f,-162.0f),   new Vector2(-705.0f, -162.0f), 11.0f, 0.46f, 0.46f, false),
                ("Road_Gargi_To_BSH",          new Vector2(-705.0f,-162.0f),   new Vector2(-705.0f, -242.0f), 11.0f, 0.46f, 0.46f, false),
                ("Road_BSH_To_VSH",            new Vector2(-705.0f,-242.0f),   new Vector2(-915.0f, -262.0f), 10.5f, 0.46f, 0.46f, false),
            };

            Material walkwayMat = GetOrCreateTexturedSurfaceMaterial(
                "Assets/Materials/M_Campus_Sidewalk.mat",
                "Assets/Textures/Campus/T_Campus_Concrete.png",
                new Color(0.80f, 0.78f, 0.75f),
                Vector2.one);

            // Extend continuous green lawn under Gargi & VSH so west/north-west hostel edges never overhang skybox void
            Material lawnMat = GetOrCreateTexturedSurfaceMaterial(
                "Assets/Materials/M_Layout_Grass.mat",
                null,
                new Color(0.24f, 0.41f, 0.14f),
                Vector2.one);
            var lawnExtensions = new (string name, Vector3 pos, Vector3 scale)[]
            {
                ("Lawn_Extension_VSH_West",      new Vector3(-980f,  0.22f, -335f), new Vector3(200f, 0.12f, 260f)),
                ("Lawn_Extension_Gargi_West",    new Vector3(-765f,  0.22f,  -60f), new Vector3(310f, 0.12f, 220f)),
            };
            foreach (var ext in lawnExtensions)
            {
                GameObject lawn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lawn.name = ext.name;
                lawn.transform.SetParent(roadsRoot.transform, false);
                lawn.transform.position = ext.pos;
                lawn.transform.localScale = ext.scale;
                lawn.layer = 0;
                GameObjectUtility.SetStaticEditorFlags(lawn, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                lawn.GetComponent<MeshRenderer>().sharedMaterial = lawnMat;
                lawn.GetComponent<BoxCollider>().isTrigger = false;
            }

            int created = 0;
            foreach (var seg in segments)
            {
                Vector2 diff = seg.b - seg.a;
                float length = diff.magnitude + seg.width * 0.35f; // slight corner overlap avoids seams
                Vector2 mid = (seg.a + seg.b) * 0.5f;
                float yaw = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

                float gA = SampleGroundHeight(new Vector3(seg.a.x, 0f, seg.a.y));
                float gM = SampleGroundHeight(new Vector3(mid.x, 0f, mid.y));
                float gB = SampleGroundHeight(new Vector3(seg.b.x, 0f, seg.b.y));
                float yA = seg.overrideYA ?? (Mathf.Max(gA, gM) + 0.08f);
                float yB = seg.overrideYB ?? (Mathf.Max(gB, gM) + 0.08f);
                float midY = (yA + yB) * 0.5f;

                GameObject road = new GameObject(seg.name);
                road.transform.SetParent(roadsRoot.transform, false);
                road.transform.position = new Vector3(mid.x, midY, mid.y);
                road.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                road.layer = 0;
                GameObjectUtility.SetStaticEditorFlags(road, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);

                MeshFilter mf = road.AddComponent<MeshFilter>();
                mf.sharedMesh = CreateRoadStripMesh(seg.name + "_Mesh", seg.width, length, yA - midY, yB - midY, seg.isWalkway ? 6f : 14f);
                MeshRenderer mr = road.AddComponent<MeshRenderer>();
                mr.sharedMaterial = seg.isWalkway ? walkwayMat : roadMat;

                MeshCollider mc = road.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                mc.isTrigger = false;
                created++;
            }

            return created;
        }

        private static Mesh CreateRoadStripMesh(string meshName, float width, float length, float localYA, float localYB, float tileLengthMeters)
        {
            Mesh mesh = new Mesh { name = meshName };
            float hw = width * 0.5f;
            float hl = length * 0.5f;
            float vMax = Mathf.Max(1f, length / tileLengthMeters);

            mesh.vertices = new Vector3[]
            {
                new Vector3(-hw, localYA, -hl),
                new Vector3( hw, localYA, -hl),
                new Vector3( hw, localYB,  hl),
                new Vector3(-hw, localYB,  hl)
            };
            Vector3 slopeNormal = Vector3.Cross(new Vector3(0f, localYB - localYA, length), Vector3.right).normalized;
            if (slopeNormal.y < 0f) slopeNormal = -slopeNormal;
            mesh.normals = new Vector3[] { slopeNormal, slopeNormal, slopeNormal, slopeNormal };
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, vMax),
                new Vector2(0f, vMax)
            };
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool FileExistsAsset(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null;
        }

        private static bool TryComputeRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool has = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
        }

        private static Material GetOrCreateTexturedSurfaceMaterial(string matPath, string texPath, Color tint, Vector2 tiling, string normalTexPath = null)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (sh != null && mat.shader != sh)
            {
                mat.shader = sh;
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                mat.mainTexture = tex;
                mat.mainTextureScale = tiling;
            }
            if (!string.IsNullOrEmpty(normalTexPath))
            {
                Texture2D ntex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexPath);
                if (ntex != null && mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", ntex);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.color = tint;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ====================================================================
        // 2. SOLID BUILDING COLLIDERS (STRICT EXTERIOR-ONLY)
        // ====================================================================
        private static int EnsureSolidBuildingColliders(Scene scene)
        {
            int added = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (IsUIPopupName(root.name) || root.name == "[World_Border_Walls]" || root.name == "OutOfBounds_ResetFloor")
                    continue;

                bool isKnownBuildingRoot = IsBuildingRoot(root.name);
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    GameObject go = mf.gameObject;

                    if (IsUIPopupName(go.name) || go.GetComponentInParent<UIPopupTriggerZone>() != null)
                        continue;

                    // Skip foliage / trees / decals / anchor visual pads
                    if (IsNonSolidVisual(go))
                        continue;

                    if (!isKnownBuildingRoot && !IsBuildingMesh(go.name) && !IsGroundOrRoadMesh(go))
                        continue;

                    Collider existing = go.GetComponent<Collider>();
                    if (existing != null)
                    {
                        existing.isTrigger = false;
                        continue;
                    }

                    // Use MeshCollider for architectural shells & ground so curved/L-shaped walls block accurately
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                    mc.isTrigger = false;
                    EditorUtility.SetDirty(go);
                    added++;
                }
            }

            return added;
        }

        private static bool IsGroundOrRoadMesh(GameObject go)
        {
            string lower = go.name.ToLowerInvariant();
            if (lower.Contains("campus_ground") || lower.Contains("er road") || lower == "surface" ||
                lower.StartsWith("plaza_") || lower.StartsWith("plinth_") ||
                lower.StartsWith("lawn_extension") || lower.StartsWith("walkway_") ||
                lower.Contains("ground_lawn") || lower.Contains("paths_") || lower.Contains("grasslawn") ||
                lower.Contains("libfront") || lower.Contains("nbfront") ||
                lower.Contains("maggipointpath") || lower.Contains("workshopfront") ||
                lower.Contains("towardsgargi") || lower.Contains("pondarea") ||
                lower.Contains("furtherinner") || lower.Contains("roadaround") ||
                lower.Contains("forecourt") || lower.Contains("highway_asphalt") ||
                lower.EndsWith("_ground") || lower.StartsWith("lawn_patches") ||
                lower.Contains("road_"))
            {
                return true;
            }
            Transform p = go.transform.parent;
            while (p != null)
            {
                if (p.name.IndexOf("campus_ground", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                p = p.parent;
            }
            return false;
        }

        private static bool IsBuildingRoot(string rootName)
        {
            foreach (string b in BuildingRootNames)
            {
                if (rootName.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsBuildingMesh(string meshObjName)
        {
            string lower = meshObjName.ToLowerInvariant();
            return lower.Contains("nb_") ||
                   lower.Contains("ga_") ||
                   lower.Contains("gateway") ||
                   lower.Contains("workshop") ||
                   lower.Contains("hostel") ||
                   lower.Contains("library") ||
                   lower.Contains("medical") ||
                   lower.Contains("maggi") ||
                   lower.Contains("burger") ||
                   lower.Contains("domino") ||
                   lower.Contains("cafe") ||
                   lower.Contains("gate") ||
                   lower.Contains("wall") ||
                   lower.Contains("building") ||
                   lower.Contains("buildingspace") ||
                   lower.Contains("door") ||
                   lower.Contains("glass") ||
                   lower.Contains("atrium");
        }

        private static bool IsNonSolidVisual(GameObject go)
        {
            string lower = go.name.ToLowerInvariant();
            if (lower.Contains("tree") || lower.Contains("leaf") || lower.Contains("leaves") ||
                lower.Contains("foliage") || lower.Contains("grass_blade") || lower.Contains("grassblade") ||
                lower.Contains("bush") ||
                lower.Contains("comingsoon") || lower.Contains("anchorvisualpad") ||
                lower.Contains("hologram") || lower.Contains("walllamp") ||
                lower.Contains("counterlight") || lower.Contains("floodlight"))
            {
                return true;
            }

            Transform p = go.transform.parent;
            while (p != null)
            {
                string pl = p.name.ToLowerInvariant();
                if (pl.Contains("campus_trees") || pl.Contains("realisticlawns") || pl.Contains("lawn_patches"))
                    return true;
                p = p.parent;
            }
            return false;
        }

        // ====================================================================
        // 3. DISABLE TELEPORTATION IN EXTERIOR SCENE
        // ====================================================================
        private static int DisableExteriorTeleportation(Scene scene)
        {
            int disabled = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var ta in root.GetComponentsInChildren<TeleportationArea>(true))
                {
                    ta.enabled = false;
                    disabled++;
                }
                foreach (var anc in root.GetComponentsInChildren<TeleportationAnchor>(true))
                {
                    anc.enabled = false;
                    disabled++;
                }
                if (root.name == "Exterior_Navigation_Anchors")
                {
                    // Hide visual blue pads while keeping anchor transforms available for Spawn/Safety recovery
                    foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        mr.enabled = false;
                    }
                    foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    {
                        col.enabled = false;
                    }
                }
            }
            return disabled;
        }

        // ====================================================================
        // 4. WORLD BORDER COLLIDER WALLS
        // ====================================================================
        private static Bounds CreateWorldBorderWalls(Scene scene)
        {
            GameObject existing = GameObject.Find("[World_Border_Walls]");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            // Calculate playable campus bounds from buildings, UI_popup zones, anchors, and plazas
            Bounds b = new Bounds(new Vector3(-400f, 0f, -150f), Vector3.zero);
            bool hasPoints = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "OutOfBounds_ResetFloor" || root.name == "Campus_Exterior_ReflectionProbe" ||
                    root.name == "Exterior_Directional_Light")
                    continue;

                bool includeRoot = IsBuildingRoot(root.name) || IsUIPopupName(root.name) ||
                                   root.name == "Exterior_Navigation_Anchors" || root.name == "ER Road Mesh" ||
                                   root.name == "[Campus_Roads_And_Plazas]" ||
                                   root.name.StartsWith("Plaza_", StringComparison.OrdinalIgnoreCase);

                if (includeRoot)
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!r.enabled) continue;
                        if (!hasPoints) { b = r.bounds; hasPoints = true; }
                        else b.Encapsulate(r.bounds);
                    }
                    foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (!hasPoints) { b = c.bounds; hasPoints = true; }
                        else b.Encapsulate(c.bounds);
                    }
                }
            }

            // Fallback bounds if scene had no matching bounds
            if (!hasPoints)
            {
                b.SetMinMax(new Vector3(-960f, -5f, -380f), new Vector3(80f, 40f, 120f));
            }

            const float margin = 40f;
            float minX = b.min.x - margin;
            float maxX = b.max.x + margin;
            float minZ = b.min.z - margin;
            float maxZ = b.max.z + margin;

            float width = maxX - minX;
            float depth = maxZ - minZ;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            const float wallHeight = 40f;
            const float wallThickness = 4f;
            float centerY = 12f;

            GameObject borderRoot = new GameObject("[World_Border_Walls]");
            CreateBorderWall(borderRoot.transform, "Border_North", new Vector3(minX - wallThickness * 0.5f, centerY, centerZ), new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f));
            CreateBorderWall(borderRoot.transform, "Border_South", new Vector3(maxX + wallThickness * 0.5f, centerY, centerZ), new Vector3(wallThickness, wallHeight, depth + wallThickness * 2f));
            CreateBorderWall(borderRoot.transform, "Border_West", new Vector3(centerX, centerY, minZ - wallThickness * 0.5f), new Vector3(width + wallThickness * 2f, wallHeight, wallThickness));
            CreateBorderWall(borderRoot.transform, "Border_East", new Vector3(centerX, centerY, maxZ + wallThickness * 0.5f), new Vector3(width + wallThickness * 2f, wallHeight, wallThickness));

            // Backup continuous safety ground plane at y = -0.25 so gravity never drops the player between road/plaza seams
            GameObject backupFloor = new GameObject("Backup_Walkable_Ground");
            backupFloor.transform.SetParent(borderRoot.transform, false);
            backupFloor.transform.position = new Vector3(centerX, -0.75f, centerZ);
            BoxCollider floorCol = backupFloor.AddComponent<BoxCollider>();
            floorCol.isTrigger = false;
            floorCol.size = new Vector3(width + wallThickness * 2f, 1f, depth + wallThickness * 2f);

            Bounds finalBounds = new Bounds();
            finalBounds.SetMinMax(new Vector3(minX, 0f, minZ), new Vector3(maxX, wallHeight, maxZ));
            return finalBounds;
        }

        private static void CreateBorderWall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            wall.transform.position = pos;
            BoxCollider bc = wall.AddComponent<BoxCollider>();
            bc.isTrigger = false;
            bc.size = size;
        }

        // ====================================================================
        // HELPERS
        // ====================================================================
        private static Material GetOrCreateHologramMaterial(string path, Color baseColor, Color rimColor, float pulseSpeed, float scanDensity, float fadePower)
        {
            Shader sh = Shader.Find(HologramShaderName);
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (sh != null)
            {
                mat.shader = sh;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", rimColor);
            if (mat.HasProperty("_PulseSpeed")) mat.SetFloat("_PulseSpeed", pulseSpeed);
            if (mat.HasProperty("_ScanlineDensity")) mat.SetFloat("_ScanlineDensity", scanDensity);
            if (mat.HasProperty("_FadeTop")) mat.SetFloat("_FadeTop", fadePower);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ConfigureHologramRenderer(MeshRenderer mr, Material mat)
        {
            if (mr == null) return;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void RemoveAllColliders(GameObject go)
        {
            foreach (var c in go.GetComponents<Collider>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
        }
    }
}
