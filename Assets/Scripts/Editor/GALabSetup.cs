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
    /// Gateway Building computer lab ("GA_Lab"): Assets/Models/Interior/GA_Lab.glb, a 2x export used at exactly 0.5x
    /// (10.6 x 3.9 x 14.9 m including the corridor). It is installed into Scene_Gateway_Interiors under a "Gateway_Interior" root.
    /// The GAL_* children are never renamed, moved or rescaled. Everything the setup adds (colliders, lights, probes, anchor,
    /// desktop player) lives under the GA_Lab root, inside a "GALab_Setup" child that cancels the root's 0.5 scale so
    /// those objects are authored directly in metres.
    /// Room frame: origin = room centre at floor level, +Z = corridor / back door, -Z = teacher wall, window wall = +X.
    /// </summary>
    public static class GALabSetup
    {
        private const string GlbPath = "Assets/Models/Interior/GA_Lab.glb";
        public const string GatewayScenePath = "Assets/Scenes/Scene_Gateway_Interiors.unity";
        private const string MaterialDir = "Assets/Materials/GA_Lab";
        public const string RootName = "GA_Lab";
        public const string InteriorRootName = "Gateway_Interior";

        private const float RootScale = 0.5f; // the GLB is a 2x export; uniform only

        // No placement was supplied. The scene's two existing pods sit at x = 0 and x = 60 (14 x 12 m each), so the lab takes the
        // next 60 m slot instead of overlapping them. Change these two values to move the room.
        public static readonly Vector3 LabPosition = new Vector3(120f, 0f, 0f);
        public const float LabYRotation = 0f;

        // Expected size after the 0.5x scale, per the brief (X, height, Z).
        private static readonly Vector3 ExpectedSize = new Vector3(10.6f, 3.9f, 14.9f);

        // Lights
        private const float LightY = 2.78f;        // just under the dropped ceiling (underside at 2.8 m)
        private const float LightSpacing = 3f;     // one light every 3 m along each strip
        private const float LightKelvin = 4000f;
        private static readonly Vector2 StripAreaSize = new Vector2(0.3f, 1.2f); // 0.3 m wide, 1.2 m along the strip (Z)
        // Same per-fixture output as NB-105 (area-ratio scaled, x3 calibrated by baking). Needs a re-check when this room is baked.
        private static float StripIntensity => 3.1416f / (StripAreaSize.x * StripAreaSize.y) * 3f;

        private static readonly string[] GlassNames = { "GAL_door_glass", "GAL_window_glass", "GAL_frosted_glass", "GAL_glass_clear" };
        private static readonly string[] FurnitureMeshes = { "GAL_Student_Desks", "GAL_Student_Computers", "GAL_Chairs", "GAL_Teacher_Station", "GAL_Furniture_Misc", "GAL_Wall_Decor" };

        [MenuItem("BMU VR Tour/Add GA Lab To Gateway Interiors Scene", false, 20)]
        public static void AddToGatewayScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(GatewayScenePath, OpenSceneMode.Single);
            StringBuilder log = new StringBuilder();
            InstallInGatewayScene(log);
            EditorSceneManager.SaveScene(scene, GatewayScenePath);
            ComputeOcclusion(scene, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[GALabSetup] Done.\n" + log);
        }

        /// <summary>Re-installs GA_Lab into the open Gateway interiors scene (replaces an existing GA_Lab). Does not save.</summary>
        public static GameObject InstallInGatewayScene(StringBuilder log)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject interior = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == InteriorRootName) interior = go;
            if (interior == null) interior = new GameObject(InteriorRootName);

            for (int i = interior.transform.childCount - 1; i >= 0; i--)
                if (interior.transform.GetChild(i).name == RootName) Object.DestroyImmediate(interior.transform.GetChild(i).gameObject);

            GameObject root = Install(interior.transform, LabPosition, LabYRotation, log);
            EditorSceneManager.MarkSceneDirty(scene);
            return root;
        }

        /// <summary>Bakes occlusion data (not lighting) for the saved scene; the OcclusionArea limits it to the lab volume.</summary>
        public static void ComputeOcclusion(Scene scene, StringBuilder log)
        {
            bool ok = StaticOcclusionCulling.Compute();
            log?.AppendLine("occlusion culling bake: " + (ok ? "done" : "FAILED"));
            if (ok) EditorSceneManager.SaveScene(scene, scene.path);
        }

        /// <summary>glTFast generates lightmap UVs on import when this is on; the GLB has none and a later bake needs them.</summary>
        public static void EnsureLightmapUVs()
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
            if (glb == null) { Debug.LogError($"[GALabSetup] Could not load '{GlbPath}'."); return null; }

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            root.name = RootName;
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            root.transform.localScale = Vector3.one * RootScale;

            GameObject world = new GameObject("GALab_Setup");
            world.transform.SetParent(root.transform, false);
            world.transform.localScale = Vector3.one / RootScale; // net scale 1: colliders, lights and the player are in real metres

            ConfigureRenderers(root, log);
            ConfigureMaterials(root, log);
            AddColliders(root, log);
            Physics.SyncTransforms();
            AddLights(world.transform, log);
            AddProbesAndCulling(world.transform, log);
            Transform anchor = AddDoorAnchor(root, world.transform, log);
            AddPlayer(world.transform, anchor, "GALab_Player", "Gateway Lab", UnityEngine.InputSystem.Key.Digit4);
            AddRoomPod(world.transform, anchor, "RoomPod_ARVRLab", "gw-lab", "AR-VR Lab");
            SetTeleportRayIgnoreLayer(root, log, "GAL_Student_Desks", "GAL_Student_Computers", "GAL_Chairs", "GAL_Teacher_Station", "GAL_Furniture_Misc");
            AddTeleportAnchors(world.transform, "GALab_TeleportAnchors", "GAL_Room_Shell", new[]
            {
                new TeleportSpec("TP_Lab_Entrance",   4.1f,  4.9f, 180f),
                new TeleportSpec("TP_Lab_EastAisle",  4.2f,  1.0f, 270f),
                new TeleportSpec("TP_Lab_WestAisle", -4.2f,  1.0f, 90f),
                new TeleportSpec("TP_Lab_Centre",     0.0f,  2.0f, 180f),
                new TeleportSpec("TP_Lab_Front",      0.0f, -3.2f, 180f),
            }, log);
            EnsureGatewayOutOfBounds();
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
                bool furniture = System.Array.IndexOf(FurnitureMeshes, r.name) >= 0;

                StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                if (!transparent) flags |= StaticEditorFlags.OccluderStatic;   // glass meshes must not hide things behind them
                if (!furniture) flags |= StaticEditorFlags.ContributeGI;      // architecture is lightmapped; furniture is probe-lit
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                r.receiveGI = furniture ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
                r.gameObject.layer = 0; // never the Teleport layer
                if (furniture) probeLit++; else lightmapped++;
            }
            log?.AppendLine($"static: all renderers static; lightmapped={lightmapped}, probe-lit furniture={probeLit} (lighting NOT baked)");
        }

        // --- Materials -------------------------------------------------------------------------------------------
        // Imported materials are sub-assets of the GLB and cannot be edited in place, so the few that need a change get a
        // project copy (same shader, same textures) assigned on this room's renderers only.
        private static void ConfigureMaterials(GameObject root, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/Materials", "GA_Lab");

            var copies = new Dictionary<Material, Material>();
            Material Copy(Material src, string suffix, System.Action<Material> tweak)
            {
                if (copies.TryGetValue(src, out Material c)) return c;
                string path = $"{MaterialDir}/{src.name}{suffix}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
                c = new Material(src) { name = src.name + suffix };
                tweak?.Invoke(c);
                AssetDatabase.CreateAsset(c, path);
                copies[src] = c;
                return c;
            }

            int instanced = 0, glowing = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    if (r.name == "GAL_Chairs" || r.name == "GAL_Student_Computers")
                    {
                        mats[i] = Copy(m, "_GPUI", c => c.enableInstancing = true);
                        changed = true; instanced++;
                    }
                    else if (m.name == "GAL_projector_screen")
                    {
                        // Subtle glow: the projected image is also used as the (dim) emission map. Screens a/b are already emissive.
                        mats[i] = Copy(m, "_Glow", c =>
                        {
                            c.EnableKeyword("_EMISSIVE");
                            if (c.HasProperty("emissiveFactor")) c.SetColor("emissiveFactor", new Color(0.3f, 0.3f, 0.3f, 1f));
                            if (c.HasProperty("emissiveTexture") && c.HasProperty("baseColorTexture")) c.SetTexture("emissiveTexture", c.GetTexture("baseColorTexture"));
                        });
                        changed = true; glowing++;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }

            // Verification: glass must stay alpha-blended with no depth write; nothing may be missing / pink.
            var seen = new HashSet<Material>();
            int missing = 0, pink = 0;
            var glassReport = new StringBuilder();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) { missing++; log?.AppendLine("MISSING material on " + r.name); continue; }
                    if (!seen.Add(m)) continue;
                    if (m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalError")) { pink++; log?.AppendLine("PINK material " + m.name); }
                    string baseName = m.name.Replace("_GPUI", "").Replace("_Glow", "");
                    if (System.Array.IndexOf(GlassNames, baseName) >= 0)
                    {
                        bool ok = m.renderQueue >= 3000 && m.GetFloat("_Surface") > 0.5f && m.GetFloat("_ZWrite") < 0.5f;
                        glassReport.Append($"{baseName}:{(ok ? "transparent/no-zwrite" : "NOT TRANSPARENT")} ");
                    }
                }
            log?.AppendLine($"materials: distinct={seen.Count}, missing={missing}, pink={pink}; GPU-instanced copies assigned={instanced} slots, projector glow={glowing}");
            log?.AppendLine("glass: " + glassReport);
        }

        // --- Colliders -------------------------------------------------------------------------------------------
        private static void AddColliders(GameObject root, StringBuilder log)
        {
            float k = 1f / RootScale; // mesh-local units are 2x room metres
            foreach (string n in new[] { "GAL_Room_Shell", "GAL_Doors_Windows" })
            {
                Transform t = root.transform.Find(n);
                MeshCollider mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = false; // glass + doors are solid for the player; the renderer stays alpha-blended
            }

            // Furniture: BoxColliders built from the connected pieces of each mesh (mesh islands), merged per desk row / chair.
            // Boxes sit on the GAL_ object that owns the mesh. Computers sit on the desks, so the desk-row boxes cover them.
            Mesh desks = root.transform.Find("GAL_Student_Desks").GetComponent<MeshFilter>().sharedMesh;
            Mesh computers = root.transform.Find("GAL_Student_Computers").GetComponent<MeshFilter>().sharedMesh;
            var rowPieces = FindIslands(desks, All(desks));
            rowPieces.AddRange(FindIslands(computers, All(computers)));
            int rows = AddBoxes(root, "GAL_Student_Desks", Merge(rowPieces, 0.15f * k, new Vector2(3.0f, 9.0f) * k), k); // two desk rows, each ~1.3 m wide x 8.4 m long

            Mesh chairsMesh = root.transform.Find("GAL_Chairs").GetComponent<MeshFilter>().sharedMesh;
            int chairs = AddBoxes(root, "GAL_Chairs", Merge(FindIslands(chairsMesh, All(chairsMesh)), 0.08f * k, new Vector2(1.0f, 1.0f) * k), k);

            Mesh teacherMesh = root.transform.Find("GAL_Teacher_Station").GetComponent<MeshFilter>().sharedMesh;
            int teacher = AddBoxes(root, "GAL_Teacher_Station", Merge(FindIslands(teacherMesh, All(teacherMesh)), 0.05f * k, new Vector2(3.5f, 2.5f) * k), k);

            Mesh miscMesh = root.transform.Find("GAL_Furniture_Misc").GetComponent<MeshFilter>().sharedMesh;
            int misc = AddBoxes(root, "GAL_Furniture_Misc", Merge(FindIslands(miscMesh, All(miscMesh)), 0.05f * k, new Vector2(2.0f, 1.0f) * k), k);

            log?.AppendLine($"colliders: MeshCollider(non-convex)=2 (GAL_Room_Shell, GAL_Doors_Windows); BoxCollider desk-rows={rows}, chairs={chairs}, teacher-station={teacher}, misc={misc}");
        }

        internal static int[] All(Mesh m)
        {
            int[] a = new int[m.subMeshCount];
            for (int i = 0; i < a.Length; i++) a[i] = i;
            return a;
        }

        internal static int AddBoxes(GameObject root, string childName, List<Bounds> boxes, float k)
        {
            GameObject target = root.transform.Find(childName).gameObject;
            int n = 0;
            foreach (Bounds b in boxes)
            {
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < 0.10f * k) continue; // cables, screws, handles
                BoxCollider bc = target.AddComponent<BoxCollider>();
                bc.center = b.center;
                bc.size = b.size;
                n++;
            }
            return n;
        }

        internal static List<Bounds> Merge(List<Bounds> input, float gap, Vector2 maxXZ)
        {
            var boxes = new List<Bounds>(input);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < boxes.Count; i++)
                    for (int j = boxes.Count - 1; j > i; j--)
                    {
                        Bounds grown = boxes[i]; grown.Expand(gap * 2f);
                        if (!grown.Intersects(boxes[j])) continue;
                        Bounds u = boxes[i]; u.Encapsulate(boxes[j]);
                        if (u.size.x > maxXZ.x || u.size.z > maxXZ.y) continue;
                        boxes[i] = u;
                        boxes.RemoveAt(j);
                        merged = true;
                    }
            }
            return boxes;
        }

        // Connected pieces (welded by position) of the given sub-meshes, as mesh-local bounds.
        internal static List<Bounds> FindIslands(Mesh m, int[] subMeshes)
        {
            Vector3[] v = m.vertices;
            var map = new Dictionary<long, int>();
            int[] rep = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long key = ((long)Mathf.RoundToInt(v[i].x * 500f) * 73856093L) ^ ((long)Mathf.RoundToInt(v[i].y * 500f) * 19349663L) ^
                           ((long)Mathf.RoundToInt(v[i].z * 500f) * 83492791L);
                if (map.TryGetValue(key, out int j)) rep[i] = j; else { map[key] = i; rep[i] = i; }
            }
            int[] parent = new int[v.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

            bool[] used = new bool[v.Length];
            foreach (int s in subMeshes)
            {
                int[] idx = m.GetTriangles(s);
                for (int i = 0; i < idx.Length; i += 3)
                {
                    int a = Find(rep[idx[i]]);
                    parent[Find(rep[idx[i + 1]])] = a;
                    parent[Find(rep[idx[i + 2]])] = Find(a);
                    used[idx[i]] = used[idx[i + 1]] = used[idx[i + 2]] = true;
                }
            }
            var d = new Dictionary<int, Bounds>();
            for (int i = 0; i < v.Length; i++)
            {
                if (!used[i]) continue;
                int r = Find(rep[i]);
                if (d.TryGetValue(r, out Bounds b)) { b.Encapsulate(v[i]); d[r] = b; }
                else d[r] = new Bounds(v[i], Vector3.zero);
            }
            return new List<Bounds>(d.Values);
        }

        // --- Lights ----------------------------------------------------------------------------------------------
        // The four light strips run along Z (x = -3.77, -1.27, 1.13, 3.63; tubes from z -5.45 to 5.15). One soft Rectangle
        // area light every 3 m along each strip, centred on it. URP renders Rectangle lights only through a lightmap bake,
        // so they are Baked; until the room is baked the scene's existing fill light is all that lights it.
        private static readonly float[] StripX = { -3.77f, -1.27f, 1.13f, 3.63f };
        private const float StripCentreZ = -0.15f;
        private const float StripLength = 9.6f; // first to last tube centre

        private static void AddLights(Transform world, StringBuilder log)
        {
            GameObject lightsRoot = new GameObject("GALab_Lights");
            lightsRoot.transform.SetParent(world, false);
            Color warm = Mathf.CorrelatedColorTemperatureToRGB(LightKelvin);

            int perStrip = Mathf.FloorToInt(StripLength / LightSpacing) + 1;
            float first = StripCentreZ - (perStrip - 1) * LightSpacing * 0.5f;
            int n = 0;
            for (int s = 0; s < StripX.Length; s++)
                for (int i = 0; i < perStrip; i++)
                {
                    GameObject go = new GameObject($"StripLight_S{s + 1}_{i + 1}");
                    go.transform.SetParent(lightsRoot.transform, false);
                    go.transform.localPosition = new Vector3(StripX[s], LightY, first + i * LightSpacing);
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // facing down; area width along X, length along Z
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Rectangle;
                    l.areaSize = StripAreaSize;
                    l.useColorTemperature = true;
                    l.colorTemperature = LightKelvin;
                    l.color = warm;
                    l.intensity = StripIntensity;
                    l.range = 6f;
                    l.shadows = LightShadows.Soft;
                    l.lightmapBakeType = LightmapBakeType.Baked;
                    n++;
                }

            // Soft window light: the windows are on the +X wall, so the "sun" travels toward -X and slightly down.
            GameObject sun = new GameObject("WindowLight_Directional");
            sun.transform.SetParent(lightsRoot.transform, false);
            sun.transform.localRotation = Quaternion.Euler(35f, -90f, 0f);
            Light d = sun.AddComponent<Light>();
            d.type = LightType.Directional;
            d.useColorTemperature = true;
            d.colorTemperature = 6000f;
            d.color = Mathf.CorrelatedColorTemperatureToRGB(6000f);
            d.intensity = 0.8f;
            d.shadows = LightShadows.Soft;
            d.lightmapBakeType = LightmapBakeType.Baked;

            log?.AppendLine($"lights: {n} Rectangle area lights (Baked, 4000 K, y={LightY} m, every {LightSpacing} m, {StripX.Length} strips x {perStrip}, intensity {StripIntensity:F1}) + 1 baked directional window light (+X side)");
        }

        private static void AddProbesAndCulling(Transform world, StringBuilder log)
        {
            Vector3 centre = new Vector3(-0.07f, 1.65f, 0.05f); // room centre, mid-height (floor 0, ceiling 3.3)

            GameObject po = new GameObject("GALab_ReflectionProbe");
            po.transform.SetParent(world, false);
            po.transform.localPosition = centre;
            ReflectionProbe probe = po.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked; // not baked yet
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.size = new Vector3(10.6f, 3.4f, 13.6f);

            // Furniture is probe-lit, so the room needs light probes for it once the lighting is baked.
            GameObject lpg = new GameObject("GALab_LightProbes");
            lpg.transform.SetParent(world, false);
            var pts = new List<Vector3>();
            foreach (float x in new[] { -3.5f, 0f, 3.5f })
                foreach (float z in new[] { -5f, -2.5f, 0f, 2.5f, 5f })
                    pts.Add(new Vector3(x, 1.2f, z));
            lpg.AddComponent<LightProbeGroup>().probePositions = pts.ToArray();

            GameObject oa = new GameObject("GALab_OcclusionArea");
            oa.transform.SetParent(world, false);
            oa.transform.localPosition = centre;
            oa.AddComponent<OcclusionArea>().size = new Vector3(10.8f, 3.8f, 15.2f); // whole room incl. corridor
            log?.AppendLine($"probes: 1 reflection probe (baked mode, not baked), {pts.Count} light probes, 1 occlusion area");
        }

        // --- Door anchor and desktop player -----------------------------------------------------------------------
        private static Transform AddDoorAnchor(GameObject root, Transform world, StringBuilder log)
        {
            // The lab door is the door frame on the +Z (corridor) side: the one with the largest Z centre.
            Transform doorsT = root.transform.Find("GAL_Doors_Windows");
            Mesh doors = doorsT.GetComponent<MeshFilter>().sharedMesh;
            Material[] mats = doorsT.GetComponent<MeshRenderer>().sharedMaterials;
            int frameSub = -1;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "GAL_door_frame") frameSub = i;

            Vector3 doorRoom = new Vector3(3.23f, 1f, 6.68f); // fallback = measured position of the +Z door frame
            Vector3 inwardRoom = Vector3.back;
            if (frameSub >= 0)
            {
                Bounds best = default; bool found = false;
                foreach (Bounds b in FindIslands(doors, new[] { frameSub }))
                    if (!found || b.center.z > best.center.z) { best = b; found = true; }
                if (found)
                {
                    doorRoom = best.center * RootScale; // mesh-local -> room metres
                    inwardRoom = new Vector3(0f, 0f, -Mathf.Sign(doorRoom.z));
                }
            }
            Vector3 inward = world.TransformDirection(inwardRoom);
            Vector3 want = world.TransformPoint(new Vector3(doorRoom.x, 0f, doorRoom.z) + inwardRoom * 1.0f);

            GameObject anchorsRoot = new GameObject("GALab_Anchors");
            anchorsRoot.transform.SetParent(world, false);
            GameObject anchorObj = new GameObject("Anchor_GALab_Door");
            anchorObj.transform.SetParent(anchorsRoot.transform, false);

            // Spawn just inside the door and look at the middle of the room (a chair or desk often sits straight ahead).
            Vector3 spawn = FindClearSpot(want);
            Vector3 facing = world.TransformPoint(new Vector3(0f, 0f, -1f)) - spawn;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = inward;
            anchorObj.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing.normalized, Vector3.up));
            log?.AppendLine($"door anchor: spawn {spawn:F2} facing {facing} (door frame at room ({doorRoom.x:F2}, {doorRoom.z:F2}))");
            return anchorObj.transform;
        }

        internal static void AddPlayer(Transform world, Transform anchor, string playerName, string label, UnityEngine.InputSystem.Key hotkey)
        {
            GameObject player = new GameObject(playerName);
            player.transform.SetParent(world, false);
            player.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.7f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.85f, 0f);
            cc.stepOffset = 0.3f; cc.slopeLimit = 45f; cc.skinWidth = 0.03f;

            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            GameObject camObj = new GameObject("Main Camera") { tag = "MainCamera" };
            camObj.transform.SetParent(head.transform, false);
            Camera cam = camObj.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 70f;
            camObj.AddComponent<AudioListener>();

            // WASD, mouse look (smoothed), Shift sprint, ~0.15 s accel/decel, gravity; switches itself off when the XR rig is present.
            NBInteriorPlayer walker = player.AddComponent<NBInteriorPlayer>();
            SerializedObject so = new SerializedObject(walker);
            so.FindProperty("m_Head").objectReferenceValue = head.transform;
            SerializedProperty tp = so.FindProperty("m_Teleports");
            tp.arraySize = 1;
            SerializedProperty e = tp.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("Label").stringValue = label;
            e.FindPropertyRelative("Hotkey").intValue = (int)hotkey;
            e.FindPropertyRelative("Anchor").objectReferenceValue = anchor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Navigator pod for the tour: a RoomPodAnchorController whose key matches the CampusTourManager room key. When a room card
        /// is pressed SceneTransitionManager loads Scene_Gateway_Interiors, finds this pod by key and moves the XR rig to
        /// its spawn anchor. The pod lives under the room's setup child, so it moves with the room.
        /// </summary>
        internal static RoomPodAnchorController AddRoomPod(Transform world, Transform anchor, string podName, string key, string displayName)
        {
            GameObject pod = new GameObject(podName);
            pod.transform.SetParent(world, false);
            pod.transform.position = anchor.position;
            RoomPodAnchorController c = pod.AddComponent<RoomPodAnchorController>();
            SerializedObject so = new SerializedObject(c);
            so.FindProperty("m_PodKey").stringValue = key;
            so.FindProperty("m_PodDisplayName").stringValue = displayName;
            so.FindProperty("m_PrimarySpawnAnchor").objectReferenceValue = anchor;
            so.FindProperty("m_FallbackRecoveryAnchor").objectReferenceValue = anchor;
            so.FindProperty("m_AlternateAnchors").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        /// <summary>
        /// One recovery floor under all Gateway rooms (like NB_Interior_OutOfBounds): if the rig ever falls out of a room it is sent
        /// back to SceneTransitionManager.CurrentAnchorTransform, i.e. the room it entered. Idempotent.
        /// </summary>
        internal static void EnsureGatewayOutOfBounds()
        {
            const string name = "Gateway_Interior_OutOfBounds";
            if (GameObject.Find(name) != null) return;
            GameObject safety = new GameObject(name);
            safety.transform.position = new Vector3(120f, -4f, 0f);
            BoxCollider col = safety.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(400f, 1f, 100f); // covers x -80..320, z -50..50: every Gateway room
            safety.AddComponent<OutOfBoundsSafetyTrigger>();
        }

        /// <summary>
        /// Physics layer for furniture, glass and ceilings. The VR teleport interactor's ray mask is Default + UI + Teleport and it only
        /// takes the CLOSEST hit, so anything on Default (walls, desks, glass...) stops the ray: a pad behind a glass partition or a
        /// desk could not be aimed at. Objects on this layer still collide with the player (CharacterController) as normal; the
        /// teleport ray just passes through them. Walls and floors stay on Default so rooms still block the ray.
        /// </summary>
        public const int TeleportRayIgnoreLayer = 30;

        internal static void SetTeleportRayIgnoreLayer(GameObject root, StringBuilder log, params string[] childNames)
        {
            int n = 0;
            foreach (string name in childNames)
            {
                Transform t = root.transform.Find(name);
                if (t == null) continue;
                t.gameObject.layer = TeleportRayIgnoreLayer;
                n++;
            }
            log?.AppendLine($"teleport-ray ignore layer ({TeleportRayIgnoreLayer}): {n} objects (furniture / glass / ceiling) collide with the player but not with the teleport ray");
        }

        internal struct TeleportSpec
        {
            public string Name; public float X, Z, Yaw;
            public TeleportSpec(string name, float x, float z, float yaw) { Name = name; X = x; Z = z; Yaw = yaw; }
        }

        /// <summary>
        /// Curated VR teleport anchors (layer 31, TeleportationAnchor + a visible 1 m pad), the project's only in-room locomotion:
        /// the rig's thumbstick move / grab move are switched off on purpose, so without these a room can be entered but not walked
        /// around. Positions are room metres (x, z) in the room's own frame; the height comes from the room's floor collider.
        /// </summary>
        internal static int AddTeleportAnchors(Transform world, string groupName, string floorColliderName, TeleportSpec[] specs, StringBuilder log)
        {
            GameObject group = new GameObject(groupName);
            group.transform.SetParent(world, false);
            Color padColor = new Color(0f, 0.45f, 0.85f); // same blue as the exterior anchors
            int made = 0;
            var report = new StringBuilder();
            foreach (TeleportSpec sp in specs)
            {
                Vector3 origin = world.TransformPoint(new Vector3(sp.X, 2.5f, sp.Z));
                float floorY = float.NaN;
                foreach (RaycastHit h in Physics.RaycastAll(origin, Vector3.down, 6f))
                    if (h.collider.name == floorColliderName && h.normal.y > 0.5f && (float.IsNaN(floorY) || h.point.y > floorY)) floorY = h.point.y;
                if (float.IsNaN(floorY)) { report.Append($" [{sp.Name}: NO FLOOR]"); continue; }

                Vector3 pos = new Vector3(origin.x, floorY + 0.02f, origin.z);
                bool clear = !Physics.CheckCapsule(pos + Vector3.up * 0.4f, pos + Vector3.up * 1.4f, 0.3f);
                if (!clear) report.Append($" [{sp.Name}: BLOCKED at ({sp.X:F1},{sp.Z:F1})]");

                GameObject a = BMUTourSceneBuilder.CreateTeleportAnchor(sp.Name, pos, world.rotation * Quaternion.Euler(0f, sp.Yaw, 0f), group.transform, padColor);
                Transform pad = a.transform.Find("AnchorVisualPad");
                if (pad != null)
                {
                    pad.localScale = new Vector3(1.0f, 0.01f, 1.0f); // 1 m pad: the exterior's 1.4 m is too wide for doorways and cabins
                    GameObjectUtility.SetStaticEditorFlags(pad.gameObject, StaticEditorFlags.BatchingStatic); // all pads share one material: batch them

                    // The builder's pad is a cylinder primitive: its CapsuleCollider, squashed flat, becomes a 0.5 m-radius invisible sphere that
                    // blocks walking and sits around the rig after a teleport. Replace it with a thin flat box (still hit by the teleport ray).
                    Collider old = pad.GetComponent<Collider>();
                    var tele = a.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>();
                    if (tele != null && old != null) tele.colliders.Remove(old);
                    if (old != null) Object.DestroyImmediate(old);
                    BoxCollider flat = pad.gameObject.AddComponent<BoxCollider>();
                    flat.size = new Vector3(1f, 4f, 1f); // local: pad scale y = 0.01, so 4 -> a 4 cm slab on the floor
                    if (tele != null) tele.colliders.Add(flat);
                }
                made++;
            }
            log?.AppendLine($"teleport anchors ({groupName}): {made}/{specs.Length} created, layer 31, 1 m pads{(report.Length > 0 ? " ->" + report : "")}");
            return made;
        }

        private static Vector3 FindClearSpot(Vector3 want)
        {
            Vector3 best = want; float bestD = float.MaxValue;
            for (float dx = -2.5f; dx <= 2.5f; dx += 0.25f)
                for (float dz = -2.5f; dz <= 2.5f; dz += 0.25f)
                {
                    Vector3 p = want + new Vector3(dx, 0f, dz);
                    float floorY = float.NaN; // floor = the shell's top-most surface below 1.5 m; furniture hits are ignored
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, 6f))
                        if (h.collider.name == "GAL_Room_Shell" && (float.IsNaN(floorY) || h.point.y > floorY) && h.point.y < p.y + 1.5f) floorY = h.point.y;
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
            log.AppendLine($"colliders total: Mesh={root.GetComponentsInChildren<MeshCollider>(true).Length}, Box={root.GetComponentsInChildren<BoxCollider>(true).Length}; lights={root.GetComponentsInChildren<Light>(true).Length}");
        }
    }
}
