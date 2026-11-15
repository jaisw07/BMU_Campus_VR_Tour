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
    /// Classroom NB-105 ("Smart Case Study Room"): a 0.5x-scaled tiered room of 17.8 x 4.2 x 14.3 m built from
    /// Assets/Models/Interior/NB_NB105_Classroom.glb. The N105_* children are never renamed, moved or rescaled. Everything the
    /// setup adds (colliders, lights, probe, anchor) lives under the NB_105 root, so the room can be moved as one object.
    /// <see cref="Install"/> is used both for the standalone test scene and for the North Block tour scene.
    /// </summary>
    public static class NB105Setup
    {
        private const string GlbPath = "Assets/Models/Interior/NB_NB105_Classroom.glb";
        public const string ScenePath = "Assets/Scenes/Dev/NB_105_Classroom.unity";
        private const string LightingSettingsPath = "Assets/Settings/NB_Building_Interior_Lighting.lighting";

        private const float RootScale = 0.5f; // the GLB is a 2x export; uniform only

        // Lights: one soft Rectangle area light under each LED strip. URP renders Rectangle lights only through a lightmap bake.
        private const float LightY = 2.78f;          // just under the LED strips (strip bottoms are at ~2.78 m)
        private const float LightKelvin = 4000f;
        // The NB building's 1 x 1 m fixtures were calibrated to intensity 3.14 (0.1 x the authored Blender 31.4). Strips are
        // 1.2 x 0.3 m here, so intensity is scaled by the area ratio to give the same light output per fixture.
        private static readonly Vector2 StripAreaSize = new Vector2(1.2f, 0.3f);
        // x3 on top of that: calibrated by baking the room and looking at it (area-ratio value alone was too dim).
        private const float BrightnessCalibration = 3f;
        private static float StripIntensity => 3.1416f / (StripAreaSize.x * StripAreaSize.y) * BrightnessCalibration;

        [MenuItem("BMU VR Tour/Build NB-105 Classroom Test Scene", false, 19)]
        public static void BuildScene()
        {
            EnsureLightmapUVs();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath) == null) { Debug.LogError($"[NB105Setup] Could not load '{GlbPath}'."); return; }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StringBuilder log = new StringBuilder();

            // No placement was given for the standalone scene: the root sits at the origin, rotation 0.
            Install(null, Vector3.zero, 0f, true, "", log);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.07f); // near-black: the baked area lights do the work
            RenderSettings.skybox = null;
            ApplyLightingSettings();

            string dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[NB105Setup] Built '" + ScenePath + "'.\n" + log);
        }

        /// <summary>glTFast generates lightmap UVs on import when this is on; the GLB has none, and a bake needs them.</summary>
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

        public static void ApplyLightingSettings()
        {
            LightingSettings ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls != null) Lightmapping.lightingSettings = ls;
        }

        /// <summary>
        /// Instantiates NB_105 under <paramref name="parent"/> (null = scene root) at the given local position / yaw, with
        /// colliders, lights, reflection probe, room info and an entrance anchor. Returns the NB_105 root.
        /// </summary>
        public static GameObject Install(Transform parent, Vector3 localPosition, float yRotation, bool withPlayer, string navigatorKey, StringBuilder log)
        {
            GameObject glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (glb == null) { Debug.LogError($"[NB105Setup] Could not load '{GlbPath}'."); return null; }

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            root.name = "NB_105";
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            root.transform.localScale = Vector3.one * RootScale;

            // A child that cancels the root's 0.5 scale: everything under it is authored directly in metres, in the room's own
            // frame (so it follows the root if it is moved or rotated later).
            GameObject world = new GameObject("NB105_Setup");
            world.transform.SetParent(root.transform, false);
            world.transform.localScale = Vector3.one / RootScale;

            MarkStatic(root);
            AddColliders(root, world.transform, log);
            Physics.SyncTransforms();
            AddLights(root, world.transform, log);
            AddReflectionProbe(world.transform);
            AddRoomInfo(root, navigatorKey);
            Transform anchor = AddEntranceAnchor(root, world.transform, log);
            if (withPlayer) AddPlayer(anchor);
            return root;
        }

        // --- Static -------------------------------------------------------------------------------------------
        private static void MarkStatic(GameObject root)
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
                r.gameObject.layer = 0; // never the Teleport layer
            }
        }

        // mesh-local -> room frame (metres, origin at the room centre on the floor)
        private static Matrix4x4 ToRoom(Transform world, Transform mesh) => world.worldToLocalMatrix * mesh.localToWorldMatrix;

        // --- Colliders ----------------------------------------------------------------------------------------
        private static void AddColliders(GameObject root, Transform world, StringBuilder log)
        {
            int meshCols = 0;
            foreach (string n in new[] { "N105_Shell", "N105_Ceiling" })
            {
                Transform t = root.transform.Find(n);
                MeshCollider mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = false;
                meshCols++;
            }

            // Furniture is one mesh, so BoxColliders are built from its connected pieces (mesh islands), per material group:
            //   desks  -> merged into one box per desk row (touching pieces only; rows are 6.9 m long)
            //   chairs -> merged into one box per chair (about 0.5 m)
            //   other  -> podium / cabinet / teacher chair
            Transform fur = root.transform.Find("N105_Furniture");
            Mesh mesh = fur.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 m = ToRoom(world, fur);
            var groups = new (string name, int[] subs, float cap, float gap)[]
            {
                ("desk",  new[] { 0, 1 },          7.5f, 0.15f), // legs sit slightly below the desk top, so a wider gap joins a row
                ("chair", new[] { 2, 3, 6, 8 },    1.0f, 0.08f),
                ("other", new[] { 4, 5, 7 },       2.5f, 0.05f),
            };

            GameObject container = new GameObject("NB105_FurnitureColliders");
            container.transform.SetParent(world, false);
            int deskBoxes = 0, chairBoxes = 0, otherBoxes = 0;
            foreach (var grp in groups)
            {
                List<Bounds> boxes = Merge(ToRoomBounds(FindIslands(mesh, grp.subs), m), grp.gap, grp.cap);
                foreach (Bounds b in boxes)
                {
                    if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < 0.10f) continue;
                    BoxCollider bc = container.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size;
                    if (grp.name == "desk") deskBoxes++; else if (grp.name == "chair") chairBoxes++; else otherBoxes++;
                }
            }
            log?.AppendLine($"NB-105 colliders: mesh={meshCols} (N105_Shell, N105_Ceiling); box={deskBoxes + chairBoxes + otherBoxes} " +
                            $"(desk={deskBoxes}, chair={chairBoxes}, other={otherBoxes})");
        }

        private static List<Bounds> ToRoomBounds(List<Bounds> local, Matrix4x4 m)
        {
            var result = new List<Bounds>(local.Count);
            foreach (Bounds b in local)
            {
                Vector3 c = m.MultiplyPoint3x4(b.center);
                Vector3 e = b.extents;
                Vector3 ext = new Vector3(
                    Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                    Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                    Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
                result.Add(new Bounds(c, ext * 2f));
            }
            return result;
        }

        private static List<Bounds> Merge(List<Bounds> input, float gap, float maxSide)
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
                        if (u.size.x > maxSide || u.size.z > maxSide) continue;
                        boxes[i] = u;
                        boxes.RemoveAt(j);
                        merged = true;
                    }
            }
            return boxes;
        }

        private static List<Bounds> FindIslands(Mesh m, int[] subMeshes)
        {
            Vector3[] v = m.vertices;
            var map = new Dictionary<long, int>();
            int[] rep = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long k = ((long)Mathf.RoundToInt(v[i].x * 500f) * 73856093L) ^ ((long)Mathf.RoundToInt(v[i].y * 500f) * 19349663L) ^
                         ((long)Mathf.RoundToInt(v[i].z * 500f) * 83492791L);
                if (map.TryGetValue(k, out int j)) rep[i] = j; else { map[k] = i; rep[i] = i; }
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

        // --- Lights: one soft area light under every LED strip ------------------------------------------------
        private static void AddLights(GameObject root, Transform world, StringBuilder log)
        {
            Transform ceilT = root.transform.Find("N105_Ceiling");
            Mesh ceil = ceilT.GetComponent<MeshFilter>().sharedMesh;
            int ledSub = -1;
            Material[] mats = ceilT.GetComponent<MeshRenderer>().sharedMaterials;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "N105_LED") ledSub = i;
            if (ledSub < 0) { log?.AppendLine("no N105_LED sub-mesh found - no lights added"); return; }

            GameObject lightsRoot = new GameObject("NB105_Lights");
            lightsRoot.transform.SetParent(world, false);
            Color warm = Mathf.CorrelatedColorTemperatureToRGB(LightKelvin);
            List<Bounds> strips = ToRoomBounds(FindIslands(ceil, new[] { ledSub }), ToRoom(world, ceilT));
            strips.Sort((a, b) => a.center.z != b.center.z ? a.center.z.CompareTo(b.center.z) : a.center.x.CompareTo(b.center.x));

            int n = 0;
            foreach (Bounds s in strips)
            {
                GameObject go = new GameObject($"CeilingStripLight_{n + 1:00}");
                go.transform.SetParent(lightsRoot.transform, false);
                go.transform.localPosition = new Vector3(s.center.x, LightY, s.center.z);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // facing down, length along the room's X
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
            log?.AppendLine($"NB-105 lights: {n} Rectangle area lights, Baked, 4000 K, y={LightY} m, {StripAreaSize.x}x{StripAreaSize.y} m, intensity {StripIntensity:F2}");
        }

        private static void AddReflectionProbe(Transform world)
        {
            GameObject po = new GameObject("NB105_ReflectionProbe");
            po.transform.SetParent(world, false);
            po.transform.localPosition = new Vector3(0f, 1.75f, 0f); // room centre, mid-height (floor top 0 .. ceiling 3.5)
            ReflectionProbe probe = po.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.size = new Vector3(17f, 3.5f, 13.5f);
        }

        private static void AddRoomInfo(GameObject root, string navigatorKey)
        {
            NBRoomInfo info = root.AddComponent<NBRoomInfo>(); // component on the root only; the N105_* children are untouched
            SerializedObject so = new SerializedObject(info);
            so.FindProperty("m_Type").enumValueIndex = (int)NBRoomType.Classroom;
            so.FindProperty("m_DisplayName").stringValue = "Smart Case Study Room (NB-105)";
            so.FindProperty("m_Level").intValue = 0;
            so.FindProperty("m_Size").vector2Value = new Vector2(17.8f, 14.3f);
            so.FindProperty("m_NavigatorKey").stringValue = navigatorKey ?? "";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // --- Entrance anchor (and optional desktop player) -----------------------------------------------------------
        private static Transform AddEntranceAnchor(GameObject root, Transform world, StringBuilder log)
        {
            Transform shellT = root.transform.Find("N105_Shell");
            Mesh shell = shellT.GetComponent<MeshFilter>().sharedMesh;
            Material[] mats = shellT.GetComponent<MeshRenderer>().sharedMaterials;
            int doorSub = -1;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "N105_Door_Wood") doorSub = i;

            // The door is a flat panel in a wall. In the room frame its thin axis is the wall normal; the room is on the other side.
            Bounds doorLocal = shell.GetSubMesh(doorSub).bounds;
            Bounds door = ToRoomBounds(new List<Bounds> { doorLocal }, ToRoom(world, shellT))[0];
            Vector3 inwardRoom = door.size.x < door.size.z ? new Vector3(-Mathf.Sign(door.center.x), 0f, 0f) : new Vector3(0f, 0f, -Mathf.Sign(door.center.z));
            Vector3 inward = world.TransformDirection(inwardRoom);
            Vector3 want = world.TransformPoint(door.center + inwardRoom * 1.0f);

            GameObject anchorsRoot = new GameObject("NB105_Anchors");
            anchorsRoot.transform.SetParent(world, false);
            GameObject anchorObj = new GameObject("Anchor_NB105_Entrance");
            anchorObj.transform.SetParent(anchorsRoot.transform, false);

            Vector3 spawn = FindClearSpot(want);
            float free = FreeDistance(spawn, inward);
            Vector3 facing = inward;
            if (free < 2f)
            {
                foreach (Vector3 d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    float f = FreeDistance(spawn, d);
                    if (f > free + 0.25f) { free = f; facing = d; }
                }
            }
            anchorObj.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(facing, Vector3.up));
            log?.AppendLine($"NB-105 entrance: spawn {spawn:F2} facing {facing}");
            return anchorObj.transform;
        }

        private static void AddPlayer(Transform anchor)
        {
            GameObject player = new GameObject("Player");
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

            NBInteriorPlayer walker = player.AddComponent<NBInteriorPlayer>();
            SerializedObject so = new SerializedObject(walker);
            so.FindProperty("m_Head").objectReferenceValue = head.transform;
            SerializedProperty tp = so.FindProperty("m_Teleports");
            tp.arraySize = 1;
            SerializedProperty e = tp.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("Label").stringValue = "NB-105";
            e.FindPropertyRelative("Hotkey").intValue = (int)UnityEngine.InputSystem.Key.Digit3;
            e.FindPropertyRelative("Anchor").objectReferenceValue = anchor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector3 FindClearSpot(Vector3 want)
        {
            // Floor height under the wanted spot (the room is tiered), then the nearest spot a 0.3 m capsule fits.
            Vector3 best = want; float bestD = float.MaxValue;
            for (float dx = -2.5f; dx <= 2.5f; dx += 0.25f)
                for (float dz = -2.5f; dz <= 2.5f; dz += 0.25f)
                {
                    Vector3 p = want + new Vector3(dx, 0f, dz);
                    // Floor height = the shell's floor; furniture hits are ignored.
                    float floorY = float.NaN;
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 3f, p.z), Vector3.down, 6f))
                        if (h.collider.name == "N105_Shell" && (float.IsNaN(floorY) || h.point.y > floorY) && h.point.y < 1.5f) floorY = h.point.y;
                    if (float.IsNaN(floorY)) continue;
                    Vector3 c = new Vector3(p.x, floorY + 0.03f, p.z);
                    if (Physics.CheckCapsule(c + Vector3.up * 0.4f, c + Vector3.up * 1.4f, 0.32f)) continue;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = c; }
                }
            return best;
        }

        private static float FreeDistance(Vector3 from, Vector3 dir)
        {
            Vector3 a = from + Vector3.up * 0.4f, b = from + Vector3.up * 1.4f;
            return Physics.CapsuleCast(a, b, 0.3f, dir, out RaycastHit hit, 6f) ? hit.distance : 6f;
        }
    }
}
