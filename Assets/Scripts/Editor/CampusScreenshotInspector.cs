using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Captures standardized exterior screenshots of every campus building, road corridor,
    /// and UI_popup hologram placement into Screenshots/Verification/ so layout, grounding,
    /// and road continuity can be verified visually rather than guessed.
    /// </summary>
    public static class CampusScreenshotInspector
    {
        private const string ExteriorScenePath = "Assets/Scenes/Scene_Campus_Exterior.unity";
        private const string OutputFolder = "Screenshots/Verification";
        private const int ImageWidth = 1280;
        private const int ImageHeight = 720;

        private struct ShotSpec
        {
            public string fileName;
            public string targetObjectName;
            public Vector3 fallbackFocus;
            public Vector3 cameraOffset;
            public float fieldOfView;
            public bool orthographic;
            public float orthoSize;
        }

        [MenuItem("BMU VR Tour/Capture All Building & Placement Screenshots", false, 3)]
        public static void CaptureAllScreenshotsMenu()
        {
            CaptureAllScreenshotsInternal(openSceneIfNeeded: false);
        }

        // Batchmode entry point: Unity -batchmode -quit -executeMethod BMU.VRTour.Editor.CampusScreenshotInspector.CaptureAllScreenshotsBatch
        public static void CaptureAllScreenshotsBatch()
        {
            CaptureAllScreenshotsInternal(openSceneIfNeeded: true);
        }

        public static void CaptureAllScreenshotsInternal(bool openSceneIfNeeded)
        {
            Scene active = SceneManager.GetActiveScene();
            if (openSceneIfNeeded || active.path != ExteriorScenePath)
            {
                if (File.Exists(ExteriorScenePath))
                {
                    active = EditorSceneManager.OpenScene(ExteriorScenePath, OpenSceneMode.Single);
                }
            }

            string outDir = Path.GetFullPath(OutputFolder);
            Directory.CreateDirectory(outDir);

            // Disable XR Camera temporarily if needed so our inspection camera captures cleanly
            GameObject camGo = new GameObject("__ScreenshotInspectorCamera");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 2000f;

            RenderTexture rt = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);

            var sb = new StringBuilder();
            sb.AppendLine($"BMU Exterior Bounds & Placement Report ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine("================================================================");

            string[] inspectRoots =
            {
                "campus_entrance_v2", "Gateway", "BML_Library_Medical", "Library_Medical_Site_HD2",
                "Library_Medical_Building_HD2", "BML_MaggiPoint", "BML_XpressCafe", "BML_DominosPoint",
                "BML_BurgerPoint", "NorthBlock_Exterior", "BMU_Workshop", "Gargi_Hostel",
                "BML_BhagatSingh_Hostel", "VikramSarabhai_Hostel", "campus_ground_v2",
                "Campus_Exterior_Environment", "road_0001", "UI_popup_Main_Gate",
                "UI_popup_Gateway", "UI_popup_Library", "UI_popup_Food_Outlet",
                "UI_popup_North_Block", "UI_popup_Mechanical_Lab", "UI_popup_Gargi",
                "UI_popup_BSH", "UI_popup_VSH"
            };

            foreach (string name in inspectRoots)
            {
                GameObject go = GameObject.Find(name);
                if (go == null)
                {
                    sb.AppendLine($"{name,-30} : NOT FOUND");
                    continue;
                }

                if (TryComputeRendererBounds(go, out Bounds rb))
                {
                    sb.AppendLine($"{name,-30} : pos={go.transform.position} boundsCenter={rb.center} min={rb.min} max={rb.max} size={rb.size}");
                }
                else
                {
                    sb.AppendLine($"{name,-30} : pos={go.transform.position} (no active renderers)");
                }
            }

            var shots = new ShotSpec[]
            {
                new ShotSpec { fileName = "00_overview_campus.png", targetObjectName = null, fallbackFocus = new Vector3(-420f, 0f, -180f), cameraOffset = new Vector3(0f, 680f, -1f), orthographic = true, orthoSize = 560f, fieldOfView = 60f },
                new ShotSpec { fileName = "01_main_gate.png", targetObjectName = "UI_popup_Main_Gate", fallbackFocus = new Vector3(45f, 2f, -2f), cameraOffset = new Vector3(32f, 8f, -12f), fieldOfView = 55f },
                new ShotSpec { fileName = "02_gateway.png", targetObjectName = null, fallbackFocus = new Vector3(-135f, 4f, -88f), cameraOffset = new Vector3(48f, 14f, 26f), fieldOfView = 55f },
                new ShotSpec { fileName = "03_library.png", targetObjectName = null, fallbackFocus = new Vector3(-148f, 3f, -205f), cameraOffset = new Vector3(38f, 12f, 42f), fieldOfView = 55f },
                new ShotSpec { fileName = "04_food_court_overview.png", targetObjectName = null, fallbackFocus = new Vector3(-310f, 2f, -96f), cameraOffset = new Vector3(12f, 24f, -54f), fieldOfView = 58f },
                new ShotSpec { fileName = "04b_maggi_xpress_ground.png", targetObjectName = null, fallbackFocus = new Vector3(-286f, 2f, -96f), cameraOffset = new Vector3(-28f, 3.5f, 8f), fieldOfView = 55f },
                new ShotSpec { fileName = "04c_dominos_burger_ground.png", targetObjectName = null, fallbackFocus = new Vector3(-340f, 3f, -85f), cameraOffset = new Vector3(28f, 3.5f, -1f), fieldOfView = 55f },
                new ShotSpec { fileName = "05_road_nb_to_food.png", targetObjectName = null, fallbackFocus = new Vector3(-415f, 2f, -124f), cameraOffset = new Vector3(68f, 18f, -30f), fieldOfView = 58f },
                new ShotSpec { fileName = "06_north_block.png", targetObjectName = null, fallbackFocus = new Vector3(-478f, 10f, -95f), cameraOffset = new Vector3(50f, 16f, -70f), fieldOfView = 55f },
                new ShotSpec { fileName = "07_workshop.png", targetObjectName = null, fallbackFocus = new Vector3(-410f, 8f, -295f), cameraOffset = new Vector3(55f, 16f, 55f), fieldOfView = 55f },
                new ShotSpec { fileName = "08_gargi.png", targetObjectName = null, fallbackFocus = new Vector3(-702f, 8f, -120f), cameraOffset = new Vector3(40f, 16f, -72f), fieldOfView = 55f },
                new ShotSpec { fileName = "09_bsh.png", targetObjectName = null, fallbackFocus = new Vector3(-698f, 10f, -295f), cameraOffset = new Vector3(48f, 18f, 85f), fieldOfView = 55f },
                new ShotSpec { fileName = "10_vsh.png", targetObjectName = null, fallbackFocus = new Vector3(-918f, 8f, -305f), cameraOffset = new Vector3(42f, 14f, 68f), fieldOfView = 55f },
                new ShotSpec { fileName = "11_player_spawn.png", targetObjectName = "XR Origin (VR Headset & Hands)", fallbackFocus = new Vector3(60f, 1.8f, -2f), cameraOffset = new Vector3(0.01f, 1.7f, 0.01f), fieldOfView = 65f },
            };

            try
            {
                cam.targetTexture = rt;
                RenderTexture prev = RenderTexture.active;

                foreach (var shot in shots)
                {
                    Vector3 focus = shot.fallbackFocus;
                    if (!string.IsNullOrEmpty(shot.targetObjectName))
                    {
                        GameObject go = GameObject.Find(shot.targetObjectName);
                        if (go != null)
                        {
                            if (shot.fileName == "11_player_spawn.png")
                            {
                                cam.transform.position = go.transform.position + new Vector3(0f, 1.7f, 0f);
                                cam.transform.rotation = Quaternion.Euler(4f, -90f, 0f);
                                cam.orthographic = false;
                                cam.fieldOfView = shot.fieldOfView;
                                RenderToPng(cam, rt, tex, Path.Combine(outDir, shot.fileName));
                                continue;
                            }

                            if (TryComputeRendererBounds(go, out Bounds b))
                            {
                                focus = new Vector3(b.center.x, b.min.y + Mathf.Min(b.size.y * 0.28f, 10f), b.center.z);
                            }
                            else
                            {
                                focus = go.transform.position + Vector3.up * 2f;
                            }
                        }
                    }

                    cam.orthographic = shot.orthographic;
                    if (shot.orthographic)
                    {
                        cam.orthographicSize = shot.orthoSize;
                    }
                    else
                    {
                        cam.fieldOfView = shot.fieldOfView;
                    }

                    cam.transform.position = focus + shot.cameraOffset;
                    cam.transform.LookAt(focus, Vector3.up);
                    RenderToPng(cam, rt, tex, Path.Combine(outDir, shot.fileName));
                }

                RenderTexture.active = prev;
            }
            finally
            {
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }

            File.WriteAllText(Path.Combine(outDir, "bounds_report.txt"), sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"[CampusScreenshotInspector] Captured {shots.Length} screenshots and bounds_report.txt into '{outDir}'.");
        }

        private static void RenderToPng(Camera cam, RenderTexture rt, Texture2D tex, string filePath)
        {
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
            tex.Apply();
            File.WriteAllBytes(filePath, tex.EncodeToPNG());
        }

        private static bool TryComputeRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool has = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
        }
    }
}
