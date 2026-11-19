using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Data-driven "no floating models" pass for Scene_Campus_Exterior.
    /// Instead of hardcoded building heights, every building / kiosk / tree is measured against the real ground
    /// colliders underneath it and moved vertically so its lowest point sits on that ground.
    /// Only the Y position changes. Moves larger than <see cref="MaxAutoShift"/> are reported, not applied.
    /// </summary>
    public static class GroundAllModels
    {
        private const string ExteriorSceneName = "Scene_Campus_Exterior";
        private const string ExteriorScenePath = "Assets/Scenes/Scene_Campus_Exterior.unity";

        /// <summary>Largest vertical correction applied automatically (metres). Bigger offsets are reported for manual review.</summary>
        // Kept small on purpose: a larger "offset" usually means a stray low mesh skews the bounds (e.g. MaggiPoint), and
        // snapping to it would LIFT the model off the ground instead of seating it.
        public const float MaxAutoShift = 0.75f;
        private const float IgnoreBelow = 0.02f;

        // Footprint-sampled objects (buildings and kiosks). Matched by exact GameObject name.
        private static readonly string[] BuildingNames =
        {
            "NB", "GA_GB", "Library", "MechanicalWorkshop", "MainBuildingBSH", "SideBSH1", "SideBSH2", "Mess",
            "BMU_Workshop", "Gateway", "Gargi_Hostel", "VikramSarabhai_Hostel", "Bhagat_Singh_Hostel_Building",
            "NorthBlock_Exterior", "BML_Library_Medical",
            "BML_MaggiPoint", "BML_XpressCafe", "BML_DominosPoint", "BML_BurgerPoint",
        };

        [MenuItem("BMU VR Tour/Ground Models/Report Floating Models (no changes)", false, 30)]
        public static void ReportMenu() => RunOnExterior(false);

        [MenuItem("BMU VR Tour/Ground Models/Snap All Models To Ground", false, 31)]
        public static void ApplyMenu() => RunOnExterior(true);

        private static void RunOnExterior(bool apply)
        {
            Scene scene = SceneManager.GetSceneByName(ExteriorSceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ExteriorScenePath, OpenSceneMode.Single);
            }

            var report = new StringBuilder();
            int moved = Apply(scene, apply, report);
            if (apply && moved > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log($"[GroundAllModels] {(apply ? "Applied" : "Report only")}: {moved} model(s) {(apply ? "moved" : "would move")}.\n{report}");
        }

        /// <summary>Measures every target against the ground below it; moves it when <paramref name="apply"/> is true. Returns the number moved (or that would move).</summary>
        public static int Apply(Scene scene, bool apply, StringBuilder report)
        {
            Physics.SyncTransforms();
            int count = 0;

            var all = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
                all.AddRange(root.GetComponentsInChildren<Transform>(true));

            // Buildings & kiosks: footprint-sampled.
            foreach (string name in BuildingNames)
            {
                Transform t = all.Find(x => x.name == name && x.gameObject.activeInHierarchy);
                if (t == null) continue;
                if (Process(t, true, apply, report)) count++;
            }

            // Trees: root-level Tree_* objects and the children of Campus_Trees; sampled at the trunk position.
            foreach (Transform t in all)
            {
                if (!t.gameObject.activeInHierarchy) continue;
                bool rootTree = t.parent == null && t.name.StartsWith("Tree_", StringComparison.Ordinal);
                bool groupedTree = t.parent != null && t.parent.name == "Campus_Trees";
                if (!rootTree && !groupedTree) continue;
                if (Process(t, false, apply, report)) count++;
            }

            // Street furniture that is baked into campus_ground_v2 as one merged mesh per type (benches/chairs, lamp posts).
            count += SnapMergedFurniture(all, apply, report);

            if (apply) Physics.SyncTransforms();
            return count;
        }

        // Real walkable surfaces inside campus_ground_v2 (furniture and plants are deliberately not in this list).
        private static readonly HashSet<string> GroundSurfaceNames = new HashSet<string>
        {
            "Ground_Lawn", "Roads_Marked", "Kerbs", "Paths_Paving", "Paths_Terracotta", "Paths_Slab",
            "Seating_Aprons", "Ground_Dirt", "Courts_Sand", "Terrace_Steps", "Beds_Soil", "Mound",
        };

        // (mesh object, objects that must move with it e.g. lamp heads on their posts)
        private static readonly (string name, string[] followers)[] MergedFurniture =
        {
            ("Benches_Wood", new string[0]),
            ("Lamp_Posts", new[] { "Lamp_Lights" }),
        };

        private const float MaxFurnitureShift = 1.0f;

        /// <summary>
        /// Merged furniture meshes cannot be snapped per item, so measure how far their lowest vertices (the feet) sit above the
        /// real ground and move the whole mesh down by the typical (25th percentile) offset.
        /// </summary>
        private static int SnapMergedFurniture(List<Transform> all, bool apply, StringBuilder report)
        {
            Transform ground = all.Find(x => x.name == "campus_ground_v2" && x.gameObject.activeInHierarchy);
            if (ground == null) return 0;

            int moved = 0;
            foreach (var item in MergedFurniture)
            {
                Transform mesh = ground.Find(item.name);
                MeshFilter mf = mesh != null ? mesh.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;

                // Lowest vertex in each 2 m cell = the feet of whatever stands there.
                var feet = new Dictionary<long, Vector3>();
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 w = mesh.TransformPoint(v);
                    long key = ((long)Mathf.FloorToInt(w.x / 2f) << 32) ^ (uint)Mathf.FloorToInt(w.z / 2f);
                    if (!feet.TryGetValue(key, out Vector3 cur) || w.y < cur.y) feet[key] = w;
                }

                var offsets = new List<float>();
                foreach (Vector3 w in feet.Values)
                {
                    bool found = false; float best = 0f;
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(w.x, w.y + 0.02f, w.z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (!GroundSurfaceNames.Contains(h.collider.name)) continue;
                        if (!found || h.point.y > best) { best = h.point.y; found = true; }
                    }
                    if (found) offsets.Add(w.y - best);
                }
                if (offsets.Count < 3) continue;

                offsets.Sort();
                float shift = offsets[offsets.Count / 4];
                if (Mathf.Abs(shift) <= IgnoreBelow) continue;
                if (Mathf.Abs(shift) > MaxFurnitureShift)
                {
                    report?.AppendLine($"  REVIEW {item.name}: feet {shift:F2} m off the ground - larger than {MaxFurnitureShift} m, NOT changed.");
                    continue;
                }

                report?.AppendLine($"  {(apply ? "MOVED" : "would move")} {item.name}: feet {(shift > 0 ? "floating" : "sunk")} by {Mathf.Abs(shift):F2} m ({offsets.Count} samples).");
                moved++;
                if (!apply) continue;

                var targets = new List<Transform> { mesh };
                foreach (string f in item.followers)
                {
                    Transform ft = ground.Find(f);
                    if (ft != null) targets.Add(ft);
                }
                foreach (Transform t in targets)
                {
                    Undo.RecordObject(t, "Ground furniture");
                    Vector3 p = t.position; p.y -= shift; t.position = p;
                    EditorUtility.SetDirty(t);
                }
                Physics.SyncTransforms();
            }
            return moved;
        }

        private static bool Process(Transform t, bool footprint, bool apply, StringBuilder report)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return false;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

            float ground;
            if (!TrySampleGround(t, b, footprint, out ground))
            {
                report?.AppendLine($"  SKIP  {t.name}: no ground collider underneath (nothing to snap to).");
                return false;
            }

            float delta = ground - b.min.y;
            if (Mathf.Abs(delta) <= IgnoreBelow) return false;

            string verdict = delta > 0f ? "sunk" : "floating";
            if (Mathf.Abs(delta) > MaxAutoShift)
            {
                report?.AppendLine($"  REVIEW {t.name}: {verdict} by {Mathf.Abs(delta):F2} m (base {b.min.y:F2}, ground {ground:F2}) - larger than {MaxAutoShift} m, NOT changed.");
                return false;
            }

            report?.AppendLine($"  {(apply ? "MOVED" : "would move")} {t.name}: {verdict} by {Mathf.Abs(delta):F2} m (base {b.min.y:F2} -> ground {ground:F2}).");
            if (apply)
            {
                Undo.RecordObject(t, "Ground model");
                Vector3 p = t.position;
                p.y += delta;
                t.position = p;
                EditorUtility.SetDirty(t);
            }
            return true;
        }

        /// <summary>Median height of the real ground surface under the target (grid under its footprint, or just the pivot for trees).</summary>
        private static bool TrySampleGround(Transform target, Bounds b, bool footprint, out float groundY)
        {
            var ys = new List<float>();
            if (footprint)
            {
                const int n = 5;
                for (int ix = 0; ix < n; ix++)
                    for (int iz = 0; iz < n; iz++)
                    {
                        float x = Mathf.Lerp(b.min.x + b.size.x * 0.1f, b.max.x - b.size.x * 0.1f, ix / (float)(n - 1));
                        float z = Mathf.Lerp(b.min.z + b.size.z * 0.1f, b.max.z - b.size.z * 0.1f, iz / (float)(n - 1));
                        if (RayGround(target, x, z, b.max.y, out float y)) ys.Add(y);
                    }
            }
            else
            {
                Vector3 p = target.position;
                if (RayGround(target, p.x, p.z, b.max.y, out float y)) ys.Add(y);
            }

            if (ys.Count == 0) { groundY = 0f; return false; }
            ys.Sort();
            groundY = ys[ys.Count / 2];
            return true;
        }

        private static bool RayGround(Transform target, float x, float z, float topY, out float y)
        {
            y = 0f;
            bool found = false;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, topY + 50f, z), Vector3.down, topY + 400f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(target)) continue;
                GameObject go = h.collider.gameObject;
                if (go.name == "Backup_Walkable_Ground") continue; // safety net plane, not a visible surface
                if (!ExteriorTourFinalizer.IsGroundOrRoadMesh(go)) continue;
                if (!found || h.point.y > y) { y = h.point.y; found = true; }
            }
            return found;
        }
    }
}
