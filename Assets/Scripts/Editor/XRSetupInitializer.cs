using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.InputSystem;
using UnityEditor.PackageManager.UI;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// Automatically configures XR Settings, OpenXR interaction profiles,
    /// compiler defines, input behaviors, and sample assets to resolve
    /// all Project Validation issues.
    /// </summary>
    [InitializeOnLoad]
    public static class XRSetupInitializer
    {
        private const string SettingsAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string OpenXRLoaderPath = "Assets/XR/Loaders/OpenXRLoader.asset";
        private const string InteractionLayerSettingsPath = "Assets/XRI/Settings/Resources/InteractionLayerSettings.asset";
        private static bool s_IsBuildingScene = false;

        static XRSetupInitializer()
        {
            EditorApplication.delayCall += EnsureXRConfigured;
        }

        [MenuItem("BMU VR Tour/Configure XR Plug-in Management & OpenXR", false, 10)]
        public static void EnsureXRConfigured()
        {
            try
            {
                // 1. Get or create XRGeneralSettingsPerBuildTarget
                var generalSettings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsAssetPath);

                if (generalSettings == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/XR"))
                    {
                        AssetDatabase.CreateFolder("Assets", "XR");
                    }

                    generalSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(generalSettings, SettingsAssetPath);
                    Debug.Log($"[XRSetup] Created {SettingsAssetPath}");
                }

                // Register in EditorBuildSettings
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, generalSettings, true);

                // Configure platform settings & loaders
                ConfigureLoaders(generalSettings);

                // Fix all Project Validation issues (profiles, defines, layers, samples)
                FixAllProjectValidationIssues();

                EditorUtility.SetDirty(generalSettings);
                AssetDatabase.SaveAssets();
                Debug.Log("[XRSetup] XR Plug-in Management and OpenXR successfully configured & validated!");

                VerifyXRConfiguration();

                // Automatically rebuild the VR Tour Scene to apply interactor line visuals, event system, and ground collider fixes
                if (!s_IsBuildingScene && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    s_IsBuildingScene = true;
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            BMUTourSceneBuilder.BuildCompleteScene();
                        }
                        catch (Exception bEx)
                        {
                            Debug.LogError($"[XRSetup] Error rebuilding VR Tour Scene: {bEx}");
                        }
                        finally
                        {
                            s_IsBuildingScene = false;
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[XRSetup] Error during XR configuration: {ex}");
            }
        }

        [MenuItem("BMU VR Tour/Fix All Project Validation Issues", false, 11)]
        public static void FixAllProjectValidationIssues()
        {
            try
            {
                Debug.Log("[XRSetup] Applying automatic fixes for Project Validation issues...");

                // -------------------------------------------------------------
                // 1 & 4. Enable OpenXR Controller Profiles & Composition Layers
                // -------------------------------------------------------------
                BuildTargetGroup[] targetGroups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android };
                string[] featureIdsToEnable = new[]
                {
                    "com.unity.openxr.feature.input.metaquestplus", // Meta Quest Touch Plus Controller Profile (Quest 3 / 3S)
                    "com.unity.openxr.feature.input.oculustouch",   // Oculus Touch Controller Profile
                    "com.unity.openxr.feature.input.metaquestpro",  // Meta Quest Touch Pro Controller Profile
                    "com.unity.openxr.feature.compositionlayers"    // Composition Layers Support Feature
                };

                foreach (var group in targetGroups)
                {
                    foreach (var featureId in featureIdsToEnable)
                    {
                        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
                        if (feature != null)
                        {
                            if (!feature.enabled)
                            {
                                feature.enabled = true;
                                EditorUtility.SetDirty(feature);
                                Debug.Log($"[XRSetup] Enabled feature {feature.name} ({featureId}) for {group}");
                            }
                        }
                    }

                    var openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                    if (openXRSettings != null)
                    {
                        EditorUtility.SetDirty(openXRSettings);
                    }
                }

                // -------------------------------------------------------------
                // 2 & 3. Compiler Defines: PoseControl & StickControl
                // -------------------------------------------------------------
                NamedBuildTarget[] namedTargets = new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android };
                foreach (var target in namedTargets)
                {
                    string existingDefines = PlayerSettings.GetScriptingDefineSymbols(target);
                    var defineSet = new HashSet<string>(existingDefines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

                    bool changed = false;
                    if (!defineSet.Contains("USE_INPUT_SYSTEM_POSE_CONTROL"))
                    {
                        defineSet.Add("USE_INPUT_SYSTEM_POSE_CONTROL");
                        changed = true;
                    }
                    if (!defineSet.Contains("USE_STICK_CONTROL_THUMBSTICKS"))
                    {
                        defineSet.Add("USE_STICK_CONTROL_THUMBSTICKS");
                        changed = true;
                    }

                    if (changed)
                    {
                        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defineSet));
                        Debug.Log($"[XRSetup] Updated Scripting Define Symbols for {target.TargetName}");
                    }
                }

                // -------------------------------------------------------------
                // 5. Run In Background & Input System Background Behavior
                // -------------------------------------------------------------
                if (!PlayerSettings.runInBackground)
                {
                    PlayerSettings.runInBackground = true;
                    Debug.Log("[XRSetup] Enabled PlayerSettings.runInBackground = true");
                }

                if (InputSystem.settings != null)
                {
                    if (InputSystem.settings.backgroundBehavior != InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices)
                    {
                        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices;
                        EditorUtility.SetDirty(InputSystem.settings);
                        Debug.Log("[XRSetup] Set InputSystem.settings.backgroundBehavior to ResetAndDisableNonBackgroundDevices");
                    }
                }

                // -------------------------------------------------------------
                // 6. Import HandVisualizer sample from com.unity.xr.hands
                // -------------------------------------------------------------
                try
                {
                    var handSamples = Sample.FindByPackage("com.unity.xr.hands", string.Empty);
                    if (handSamples != null)
                    {
                        foreach (var sample in handSamples)
                        {
                            if (sample.displayName == "HandVisualizer")
                            {
                                sample.Import(Sample.ImportOptions.OverridePreviousImports);
                                Debug.Log("[XRSetup] Successfully imported HandVisualizer sample from com.unity.xr.hands");
                                break;
                            }
                        }
                    }
                }
                catch (Exception sampleEx)
                {
                    Debug.LogWarning($"[XRSetup] Could not automatically import HandVisualizer sample: {sampleEx.Message}");
                }

                // -------------------------------------------------------------
                // 7. Interaction Layer 31 -> 'Teleport' via SerializedObject
                // -------------------------------------------------------------
                try
                {
                    var layerAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(InteractionLayerSettingsPath);
                    if (layerAsset != null)
                    {
                        var so = new SerializedObject(layerAsset);
                        var layerNamesProp = so.FindProperty("m_LayerNames");
                        if (layerNamesProp != null && layerNamesProp.arraySize > 31)
                        {
                            var element = layerNamesProp.GetArrayElementAtIndex(31);
                            if (element.stringValue != "Teleport")
                            {
                                element.stringValue = "Teleport";
                                so.ApplyModifiedProperties();
                                EditorUtility.SetDirty(layerAsset);
                                Debug.Log("[XRSetup] Set Interaction Layer 31 to 'Teleport'");
                            }
                        }
                    }
                }
                catch (Exception layerEx)
                {
                    Debug.LogWarning($"[XRSetup] Could not set Interaction Layer 31: {layerEx.Message}");
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[XRSetup] All Project Validation issues processed.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[XRSetup] Error applying validation fixes: {ex}");
            }
        }

        private static void ConfigureLoaders(XRGeneralSettingsPerBuildTarget generalSettings)
        {
            if (generalSettings == null) return;

            // Load OpenXRLoader
            XRLoader openXRLoader = AssetDatabase.LoadAssetAtPath<XRLoader>(OpenXRLoaderPath);
            if (openXRLoader == null)
            {
                var guids = AssetDatabase.FindAssets("t:OpenXRLoader");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    openXRLoader = AssetDatabase.LoadAssetAtPath<XRLoader>(path);
                }
            }

            BuildTargetGroup[] targetGroups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android };

            bool dirty = false;
            foreach (var group in targetGroups)
            {
                if (!generalSettings.HasSettingsForBuildTarget(group))
                {
                    generalSettings.CreateDefaultSettingsForBuildTarget(group);
                    dirty = true;
                }

                XRGeneralSettings targetSettings = generalSettings.SettingsForBuildTarget(group);
                if (targetSettings != null)
                {
                    targetSettings.InitManagerOnStart = true;

                    if (!generalSettings.HasManagerSettingsForBuildTarget(group))
                    {
                        generalSettings.CreateDefaultManagerSettingsForBuildTarget(group);
                        dirty = true;
                    }

                    XRManagerSettings manager = generalSettings.ManagerSettingsForBuildTarget(group);
                    if (manager != null && openXRLoader != null)
                    {
                        bool hasLoader = false;
                        foreach (var l in manager.activeLoaders)
                        {
                            if (l != null && l is OpenXRLoader)
                            {
                                hasLoader = true;
                                break;
                            }
                        }

                        if (!hasLoader)
                        {
                            manager.TryAddLoader(openXRLoader);
                            Debug.Log($"[XRSetup] Added OpenXRLoader to {group} build target");
                            dirty = true;
                        }
                    }
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(generalSettings);
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("BMU VR Tour/Verify XR Configuration", false, 12)]
        public static void VerifyXRConfiguration()
        {
            var standaloneSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var androidSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);

            int standaloneCount = standaloneSettings?.Manager?.activeLoaders?.Count ?? 0;
            int androidCount = androidSettings?.Manager?.activeLoaders?.Count ?? 0;

            Debug.Log($"[XRSetup Verification] Standalone active loaders: {standaloneCount}, Android active loaders: {androidCount}");
        }
    }
}
