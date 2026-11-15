using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Gateway Building auditorium ("GA_Auditorium"): Assets/Models/Interior/GA_Auditorium.glb, a 2x export used at exactly 0.5x
    /// (21.4 x 7.3 x 26.5 m). Installed into Scene_Gateway_Interiors under the same "Gateway_Interior" root as GA_Lab.
    /// The AUD_* children are never renamed, moved or rescaled. Everything the setup adds lives under the GA_Auditorium root,
    /// inside an "GAAud_Setup" child that cancels the root's 0.5 scale so those objects are authored directly in metres.
    /// Room frame: origin = room centre at floor level; the stage is at -Z (deck 0.8 m), the raked seating rises toward +Z
    /// (13 steps of 0.14 m up to 1.68 m) and the entrance doors are in the back wall at z = +12.7.
    /// </summary>
    public static class GAAuditoriumSetup
    {
        private const string GlbPath = "Assets/Models/Interior/GA_Auditorium.glb";
        public const string RootName = "GA_Auditorium";

        private const float RootScale = 0.5f; // the GLB is a 2x export; uniform only

        // No placement was supplied. Pods sit at x = 0 and 60 and GA_Lab at x = 120 (60 m slots), so the auditorium takes the next slot.
        public static readonly Vector3 AuditoriumPosition = new Vector3(180f, 0f, 0f);
        public const float AuditoriumYRotation = 0f;

        private static readonly Vector3 ExpectedSize = new Vector3(21.4f, 7.3f, 26.5f); // X, height, Z after the 0.5x scale

        private const float LightKelvin = 4000f;
        private static readonly Vector2 StripAreaSize = new Vector2(0.3f, 1.2f); // house-light area lights, length along Z
        private static float StripIntensity => 3.1416f / (StripAreaSize.x * StripAreaSize.y) * 3f; // same per-fixture output as GA_Lab / NB-105
        private const float HouseLightSpacing = 3f;

        [MenuItem("BMU VR Tour/Add GA Auditorium To Gateway Interiors Scene", false, 21)]
        public static void AddToGatewayScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(GALabSetup.GatewayScenePath, OpenSceneMode.Single);
            StringBuilder log = new StringBuilder();
            InstallInGatewayScene(log);
            EditorSceneManager.SaveScene(scene, GALabSetup.GatewayScenePath);
            GALabSetup.ComputeOcclusion(scene, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[GAAuditoriumSetup] Done.\n" + log);
        }

        /// <summary>Re-installs GA_Auditorium into the open Gateway interiors scene (replaces an existing one). Does not save.</summary>
        public static GameObject InstallInGatewayScene(StringBuilder log)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject interior = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == GALabSetup.InteriorRootName) interior = go;
            if (interior == null) interior = new GameObject(GALabSetup.InteriorRootName);

            for (int i = interior.transform.childCount - 1; i >= 0; i--)
                if (interior.transform.GetChild(i).name == RootName) Object.DestroyImmediate(interior.transform.GetChild(i).gameObject);

            GameObject root = Install(interior.transform, AuditoriumPosition, AuditoriumYRotation, log);
            EditorSceneManager.MarkSceneDirty(scene);
            return root;
        }

        private static void EnsureLightmapUVs()
        {
            AssetImporter imp = AssetImporter.GetAtPath(GlbPath);
            if (imp == null) return;
            SerializedObject so = new SerializedObject(imp);
            SerializedProperty p = so.FindProperty("editorImportSettings.generateSecondaryUVSet");
            if (p != null && !p.boolValue)
            {
                p.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                imp.SaveAndReimport();
            }
        }

        public static GameObject Install(Transform parent, Vector3 localPosition, float yRotation, StringBuilder log)
        {
            EnsureLightmapUVs();
            GameObject glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (glb == null) { Debug.LogError($"[GAAuditoriumSetup] Could not load '{GlbPath}'."); return null; }

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            root.name = RootName;
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            root.transform.localScale = Vector3.one * RootScale;

            GameObject world = new GameObject("GAAud_Setup");
            world.transform.SetParent(root.transform, false);
            world.transform.localScale = Vector3.one / RootScale; // net scale 1: colliders, lights and the player are in real metres

            ConfigureRenderers(root, log);
            CheckMaterials(root, log);
            AddColliders(root, log);
            Physics.SyncTransforms();
            AddLights(root, world.transform, log);
            AddProbesAudioCulling(world.transform, log);
            Transform anchor = AddEntranceAnchor(root, world.transform, log);
            GALabSetup.AddPlayer(world.transform, anchor, "GAAud_Player", "Gateway Auditorium", UnityEngine.InputSystem.Key.Digit5);
            GALabSetup.AddRoomPod(world.transform, anchor, "RoomPod_Auditorium", "gw-auditorium", "Auditorium");
            GALabSetup.SetTeleportRayIgnoreLayer(root, log, "AUD_Seats", "AUD_Stage_Dressing", "AUD_Ceiling");
            GALabSetup.AddTeleportAnchors(world.transform, "GAAud_TeleportAnchors", "AUD_Shell", new[]
            {
                new GALabSetup.TeleportSpec("TP_Aud_TopCentre",   0.0f, 11.7f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_WestUpper",  -5.65f, 8.2f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_WestMid",    -5.65f, 4.2f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_WestFront",  -5.65f, -0.3f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_EastUpper",   5.65f, 8.2f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_EastMid",     5.65f, 4.2f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_EastFront",   5.65f, -0.3f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_FrontFloor",  0.0f, -2.5f, 180f),
                new GALabSetup.TeleportSpec("TP_Aud_Stage",       0.0f, -8.0f, 0f), // the 0.8 m stage deck has no stairs: teleport only
            }, log);
            GALabSetup.EnsureGatewayOutOfBounds();
            Report(root, log);
            return root;
        }

        // --- Static flags / GI -----------------------------------------------------------------------------------
        private static void ConfigureRenderers(GameObject root, StringBuilder log)
        {
            int lightmapped = 0, probeLit = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool transparent = false;
                foreach (Material m in r.sharedMaterials) if (m != null && m.renderQueue >= 3000) transparent = true;
                bool architecture = r.name == "AUD_Shell" || r.name == "AUD_Ceiling"; // seats, stage dressing and truss are probe-lit

                StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                // The ceiling is NOT an occluder: its zero-thickness stepped planes made the baked occlusion data hide the ceiling,
                // truss and stage dressing from inside the room. The shell (walls) is the occluder that matters.
                if (!transparent && r.name != "AUD_Ceiling") flags |= StaticEditorFlags.OccluderStatic;
                if (architecture) flags |= StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                r.receiveGI = architecture ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                r.gameObject.layer = 0; // never the Teleport layer
                if (architecture) lightmapped++; else probeLit++;
            }
            log?.AppendLine($"static: all renderers static; lightmapped={lightmapped} (shell, ceiling), probe-lit={probeLit} (seats, stage dressing, truss) (lighting NOT baked)");
        }

        // --- Materials: imported ones are kept untouched; this only verifies them ---------------------------------
        private static void CheckMaterials(GameObject root, StringBuilder log)
        {
            var seen = new HashSet<Material>();
            int missing = 0, pink = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) { missing++; log?.AppendLine("MISSING material on " + r.name); continue; }
                    if (!seen.Add(m)) continue;
                    if (m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalError")) { pink++; log?.AppendLine("PINK material " + m.name); }
                }
            log?.AppendLine($"materials: distinct={seen.Count}, missing={missing}, pink/broken shader={pink} (AUD_Seat_Purple is the intentional pink/purple)");
        }

        // --- Colliders -------------------------------------------------------------------------------------------
        private static void AddColliders(GameObject root, StringBuilder log)
        {
            float k = 1f / RootScale; // mesh-local units are 2x room metres
            // The shell holds the raked carpet (13 steps of 0.14 m: under the 0.3 step offset, so no ramps are needed), the stage
            // deck (y 0.8), walls and doors. The ceiling is its own mesh.
            foreach (string n in new[] { "AUD_Shell", "AUD_Ceiling" })
            {
                Transform t = root.transform.Find(n);
                MeshCollider mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = false;
            }

            // Seats: 360 seats in 12 raked rows, split by two aisles into 3 blocks. One box per row per block (36 boxes).
            Mesh seatsMesh = root.transform.Find("AUD_Seats").GetComponent<MeshFilter>().sharedMesh;
            int seatBoxes = GALabSetup.AddBoxes(root, "AUD_Seats",
                GALabSetup.Merge(GALabSetup.FindIslands(seatsMesh, GALabSetup.All(seatsMesh)), 0.12f * k, new Vector2(10.5f, 1.3f) * k), k);

            // Stage props: one box per prop (podium, stand, maroon flats, low curtains). Anything a 1.7 m capsule standing on the
            // 0.8 m deck cannot reach (everything starting above 2.5 m: truss bar, wing borders) gets no collider.
            Mesh dressMesh = root.transform.Find("AUD_Stage_Dressing").GetComponent<MeshFilter>().sharedMesh;
            var props = new List<Bounds>();
            foreach (Bounds b in GALabSetup.FindIslands(dressMesh, new[] { 0, 1, 2, 3 })) // sub 4 is the flat logo quad on the podium
                if (b.min.y / k < 2.5f) props.Add(b);
            int propBoxes = GALabSetup.AddBoxes(root, "AUD_Stage_Dressing", props, k);

            // AUD_Truss_Lights: no colliders (overhead fixtures).
            log?.AppendLine($"colliders: MeshCollider(non-convex)=2 (AUD_Shell incl. stage deck + raked floor, AUD_Ceiling); BoxCollider seats={seatBoxes}, stage props={propBoxes}; truss lights: none");
        }

        // --- Lights ----------------------------------------------------------------------------------------------
        // Spot lights sit on the 16 fixture bodies of the truss (10 on the stage rig, 6 on the front-of-house bar) and aim at the stage.
        // They are Mixed with no shadows (16 realtime lights, cheap). House lights are baked Rectangle area lights every 3 m along
        // each of the 6 ceiling LED strips (URP renders Rectangle lights only through a bake, so they stay dark until the room is baked).
        private static void AddLights(GameObject root, Transform world, StringBuilder log)
        {
            Color warm = Mathf.CorrelatedColorTemperatureToRGB(LightKelvin);
            float k = 1f / RootScale;
            GameObject lightsRoot = new GameObject("GAAud_Lights");
            lightsRoot.transform.SetParent(world, false);

            // --- truss spots
            Mesh truss = root.transform.Find("AUD_Truss_Lights").GetComponent<MeshFilter>().sharedMesh;
            int spots = 0;
            foreach (Bounds b in GALabSetup.FindIslands(truss, new[] { 0 }))
            {
                Vector3 s = b.size / k;
                bool can = s.x > 0.15f && s.x < 0.35f && s.z > 0.15f && s.z < 0.35f && s.y > 0.3f && s.y < 0.4f; // fixture body (0.22-0.24 x 0.35 x 0.22-0.24)
                if (!can) continue;
                Vector3 c = b.center / k;
                Vector3 pos = new Vector3(c.x, c.y - s.y * 0.5f - 0.03f, c.z);        // just under the fixture
                Vector3 target = new Vector3(Mathf.Clamp(c.x * 0.5f, -6f, 6f), 1.0f, -8.0f); // stage centre area, spread a little by x
                float d = Vector3.Distance(pos, target);

                GameObject go = new GameObject($"TrussSpot_{spots + 1:00}");
                go.transform.SetParent(lightsRoot.transform, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.LookRotation(target - pos, Vector3.up);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 50f;
                l.innerSpotAngle = 30f;
                l.useColorTemperature = true;
                l.colorTemperature = LightKelvin;
                l.color = warm;
                l.range = d * 1.5f + 4f;
                l.intensity = Mathf.Clamp(0.2f * d * d, 3f, 80f); // inverse-square: constant light on the stage whatever the throw distance
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Mixed;
                spots++;
            }

            // --- house lights along the ceiling LED strips
            Mesh ceil = root.transform.Find("AUD_Ceiling").GetComponent<MeshFilter>().sharedMesh;
            Material[] cmats = root.transform.Find("AUD_Ceiling").GetComponent<MeshRenderer>().sharedMaterials;
            int ledSub = -1;
            for (int i = 0; i < cmats.Length; i++) if (cmats[i] != null && cmats[i].name == "AUD_LED") ledSub = i;
            int area = 0;
            if (ledSub >= 0)
            {
                foreach (Bounds b in GALabSetup.FindIslands(ceil, new[] { ledSub }))
                {
                    Vector3 c = b.center / k, s = b.size / k;
                    int n = Mathf.FloorToInt(s.z / HouseLightSpacing) + 1;
                    float first = c.z - (n - 1) * HouseLightSpacing * 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        GameObject go = new GameObject($"HouseLight_{area + 1:00}");
                        go.transform.SetParent(lightsRoot.transform, false);
                        go.transform.localPosition = new Vector3(c.x, c.y - 0.03f, first + i * HouseLightSpacing);
                        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        Light l = go.AddComponent<Light>();
                        l.type = LightType.Rectangle;
                        l.areaSize = StripAreaSize;
                        l.useColorTemperature = true;
                        l.colorTemperature = LightKelvin;
                        l.color = warm;
                        l.intensity = StripIntensity;
                        l.range = 8f;
                        l.shadows = LightShadows.Soft;
                        l.lightmapBakeType = LightmapBakeType.Baked;
                        area++;
                    }
                }
            }
            log?.AppendLine($"lights: {spots} truss spot lights (Mixed, no shadows, 4000 K) + {area} baked Rectangle house lights (4000 K, every {HouseLightSpacing} m along 6 LED strips) = {spots + area}; realtime/mixed = {spots}");
        }

        private static void AddProbesAudioCulling(Transform world, StringBuilder log)
        {
            Vector3 centre = new Vector3(0f, 3.25f, 0f); // room centre (floor 0 .. ceiling top 6.9, shell min -0.4)

            GameObject po = new GameObject("GAAud_ReflectionProbe");
            po.transform.SetParent(world, false);
            po.transform.localPosition = new Vector3(0f, 3.3f, 0f);
            ReflectionProbe probe = po.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked; // not baked yet
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.size = new Vector3(21f, 7f, 26f);

            // Seats / props are probe-lit, so the room needs light probes once the lighting is baked (floor height + 1.2 m).
            GameObject lpg = new GameObject("GAAud_LightProbes");
            lpg.transform.SetParent(world, false);
            var pts = new List<Vector3>();
            foreach (float x in new[] { -8f, -4f, 0f, 4f, 8f })
                foreach (float z in new[] { -10f, -6f, -2f, 2f, 6f, 10f })
                {
                    float floor = z < -3.35f ? 0.8f : Mathf.Clamp((z + 1.8f) * 0.14f, 0f, 1.68f);
                    pts.Add(new Vector3(x, floor + 1.2f, z));
                }
            lpg.AddComponent<LightProbeGroup>().probePositions = pts.ToArray();

            GameObject oa = new GameObject("GAAud_OcclusionArea");
            oa.transform.SetParent(world, false);
            oa.transform.localPosition = centre;
            oa.AddComponent<OcclusionArea>().size = new Vector3(21.6f, 7.6f, 27f);

            // Audio: Auditorium reverb over the whole room (full effect inside 14 m, fading out by 18 m) and a speaker on the stage.
            GameObject rz = new GameObject("GAAud_ReverbZone");
            rz.transform.SetParent(world, false);
            rz.transform.localPosition = new Vector3(0f, 3f, 0f);
            AudioReverbZone zone = rz.AddComponent<AudioReverbZone>();
            zone.reverbPreset = AudioReverbPreset.Auditorium;
            zone.minDistance = 14f;
            zone.maxDistance = 18f;

            GameObject sp = new GameObject("GAAud_Stage_Speaker");
            sp.transform.SetParent(world, false);
            sp.transform.localPosition = new Vector3(0f, 2.0f, -10f); // upstage centre, 1.2 m above the deck
            AudioSource src = sp.AddComponent<AudioSource>();
            src.playOnAwake = false; // off by default; assign a clip and call Play() to use it
            src.loop = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 3f;
            src.maxDistance = 30f;
            log?.AppendLine($"probes/audio: 1 reflection probe (baked mode, not baked), {pts.Count} light probes, 1 occlusion area, 1 Auditorium reverb zone, 1 stage AudioSource (playOnAwake off, no clip)");
        }

        // --- Entrance anchor ---------------------------------------------------------------------------------------
        private static Transform AddEntranceAnchor(GameObject root, Transform world, StringBuilder log)
        {
            // Main entrance = a door in the back wall (the highest-Z grey door; with a tie, the +X one).
            Transform shellT = root.transform.Find("AUD_Shell");
            Mesh shell = shellT.GetComponent<MeshFilter>().sharedMesh;
            Material[] mats = shellT.GetComponent<MeshRenderer>().sharedMaterials;
            int doorSub = -1;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "AUD_Door_Grey") doorSub = i;

            Vector3 doorRoom = new Vector3(4f, 0f, 12.72f); // fallback = measured position
            if (doorSub >= 0)
            {
                bool found = false; Bounds best = default;
                foreach (Bounds b in GALabSetup.FindIslands(shell, new[] { doorSub }))
                    if (!found || b.center.z > best.center.z + 0.01f || (Mathf.Abs(b.center.z - best.center.z) < 0.01f && b.center.x > best.center.x)) { best = b; found = true; }
                if (found) doorRoom = best.center * RootScale;
            }
            Vector3 want = world.TransformPoint(new Vector3(doorRoom.x, 0f, doorRoom.z - 1.0f)); // 1 m inside the back wall

            GameObject anchorsRoot = new GameObject("GAAud_Anchors");
            anchorsRoot.transform.SetParent(world, false);
            GameObject anchorObj = new GameObject("Anchor_GAAud_Entrance");
            anchorObj.transform.SetParent(anchorsRoot.transform, false);

            Vector3 spawn = FindClearSpot(want);
            Vector3 facing = world.TransformPoint(new Vector3(0f, 0f, -8f)) - spawn; // look down the room at the stage
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = world.TransformDirection(Vector3.back);
            anchorObj.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing.normalized, Vector3.up));
            log?.AppendLine($"entrance anchor: spawn {spawn:F2} facing {facing.normalized:F2} (back door at room ({doorRoom.x:F2}, {doorRoom.z:F2}))");
            return anchorObj.transform;
        }

        private static Vector3 FindClearSpot(Vector3 want)
        {
            Vector3 best = want; float bestD = float.MaxValue;
            for (float dx = -2.5f; dx <= 2.5f; dx += 0.25f)
                for (float dz = -2.5f; dz <= 2.5f; dz += 0.25f)
                {
                    Vector3 p = want + new Vector3(dx, 0f, dz);
                    float floorY = float.NaN; // top-most upward-facing shell surface below 3 m (the raked floor reaches 1.68 m)
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 3f, p.z), Vector3.down, 6f))
                        if (h.collider.name == "AUD_Shell" && h.normal.y > 0.5f && (float.IsNaN(floorY) || h.point.y > floorY)) floorY = h.point.y;
                    if (float.IsNaN(floorY)) continue;
                    Vector3 c = new Vector3(p.x, floorY + 0.03f, p.z);
                    if (Physics.CheckCapsule(c + Vector3.up * 0.4f, c + Vector3.up * 1.4f, 0.32f)) continue;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = c; }
                }
            return best;
        }

        // --- Report ----------------------------------------------------------------------------------------------
        private static void Report(GameObject root, StringBuilder log)
        {
            if (log == null) return;
            Bounds total = default; bool has = false; long tris = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!has) { total = r.bounds; has = true; } else total.Encapsulate(r.bounds);
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += mf.sharedMesh.GetIndexCount(i) / 3;
            }
            Vector3 s = total.size;
            Vector3 dev = new Vector3((s.x / ExpectedSize.x - 1f) * 100f, (s.y / ExpectedSize.y - 1f) * 100f, (s.z / ExpectedSize.z - 1f) * 100f);
            bool off = Mathf.Abs(dev.x) > 5f || Mathf.Abs(dev.y) > 5f || Mathf.Abs(dev.z) > 5f;
            log.AppendLine($"scale: root {root.transform.localScale} | bounds {s.x:F2} x {s.y:F2} x {s.z:F2} m (expected ~{ExpectedSize.x} x {ExpectedSize.y} x {ExpectedSize.z}; deviation {dev.x:+0.0;-0.0}% / {dev.y:+0.0;-0.0}% / {dev.z:+0.0;-0.0}%)" +
                           (off ? "  ** OFF BY MORE THAN 5% - NOT RESCALED **" : "  OK"));
            log.AppendLine($"bounds centre {total.center:F2}, min y {total.min.y:F2}, max y {total.max.y:F2}; triangles={tris}");
            log.AppendLine($"colliders total: Mesh={root.GetComponentsInChildren<MeshCollider>(true).Length}, Box={root.GetComponentsInChildren<BoxCollider>(true).Length}; " +
                           $"lights={root.GetComponentsInChildren<Light>(true).Length}");
        }
    }
}
