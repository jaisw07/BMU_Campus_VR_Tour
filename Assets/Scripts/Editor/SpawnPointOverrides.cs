using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Hand-tuned spawn positions that must survive a scene rebuild. Several anchors are computed by the setup scripts
    /// from room geometry (NB105Setup, NBBuildingInteriorSetup) or live in a hand-built scene (BMU_Workshop_Interior),
    /// so their final position is recorded here and re-applied:
    ///  - automatically by NB105Setup / NBBuildingInteriorSetup after they place an anchor,
    ///  - on demand through BMU VR Tour -> Apply Spawn Overrides To Open Scenes (needed for the workshop interior).
    /// The exterior anchors are also written straight into BMUTourSceneBuilder.BuildExteriorScene.
    ///
    /// Positions are the FLOOR point the player's head should stand over (the rig is aligned by head, see
    /// SceneTransitionManager.MoveRigToAnchor). Yaw is only set where recorded; otherwise it is left alone.
    /// </summary>
    public static class SpawnPointOverrides
    {
        public struct Spawn
        {
            public Vector3 Position;
            public float? Yaw;
            public Spawn(Vector3 position, float? yaw = null) { Position = position; Yaw = yaw; }
        }

        public static readonly Dictionary<string, Spawn> Table = new Dictionary<string, Spawn>
        {
            // Exterior anchors (front gate, North Block, workshop, gateway) are positioned by CampusLayoutBuilder from the campus map.
            // Smart Case Study Room (NB-105): horizontal centre of the room (was x = 30.48, against the wall).
            { "Anchor_NB105_Entrance",             new Spawn(new Vector3(25.0f, 0.66f, 6.22f), 180f) },
            // Standard Classroom: front (whiteboard wall) centre, still facing the back wall (was mid-room, off-centre).
            { "Anchor_StandardClassroom_Entrance", new Spawn(new Vector3(-0.11f, 0.03f, 12.0f), 0f) },
            // Mechatronics Lab: nearer the middle of the hall (was x = 14).
            { "Anchor_Workshop_Mechatronics",      new Spawn(new Vector3(5.0f, 0.45f, -3.8f), 90f) },
        };

        /// <summary>Moves the anchor to its recorded spot if it has one. Returns true when applied.</summary>
        public static bool TryApply(Transform anchor)
        {
            if (anchor == null || !Table.TryGetValue(anchor.name, out Spawn s)) return false;
            anchor.position = s.Position;
            if (s.Yaw.HasValue) anchor.rotation = Quaternion.Euler(0f, s.Yaw.Value, 0f);
            return true;
        }

        [MenuItem("BMU VR Tour/Apply Spawn Overrides To Open Scenes", false, 24)]
        public static void ApplyToOpenScenes()
        {
            int applied = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (TryApply(t)) { applied++; changed = true; }
                    }
                }
                if (changed) EditorSceneManager.MarkSceneDirty(scene);
            }
            Debug.Log($"[BMU VR Tour] Applied {applied} spawn override(s) to the open scenes. Save the scenes to keep them.");
        }
    }
}
