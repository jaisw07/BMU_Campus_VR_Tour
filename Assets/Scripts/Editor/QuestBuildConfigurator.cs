using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;

namespace BMU.VRTour.Editor
{
    /// <summary>
    /// One-click configuration and build for the Meta Quest 3S (Android, OpenXR, Vulkan, IL2CPP, ARM64).
    /// Menu: BMU VR Tour / Quest.
    /// </summary>
    public static class QuestBuildConfigurator
    {
        private const string PackageId = "com.bmu.campustour";
        private const string BuildDir = "Builds";
        private const string ApkName = "BMU_Campus_VR_Tour.apk";

        [MenuItem("BMU VR Tour/Quest/1. Configure Project for Quest 3S", false, 30)]
        public static void ConfigureForQuest()
        {
            // --- Platform ---
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }
            EditorUserBuildSettings.buildAppBundle = false;                 // sideloadable .apk
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Release;

            // --- Identity ---
            PlayerSettings.companyName = "BMU";
            PlayerSettings.productName = "BMU Campus VR Tour";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);

            // --- Android requirements for Quest ---
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.optimizedFramePacing = false;           // XR runtime paces frames
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.startInFullscreen = true;

            // --- Rendering: Vulkan only, linear, ASTC ---
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);

            // --- XR: OpenXR loader + Meta Quest features for Android ---
            // (Not EnsureXRConfigured: that also triggers a full scene rebuild.)
            XRSetupInitializer.FixAllProjectValidationIssues();
            EnableOpenXRFeature(BuildTargetGroup.Android, "com.unity.openxr.feature.metaquest");
            EnableOpenXRFeature(BuildTargetGroup.Android, "com.unity.openxr.feature.input.metaquestplus");
            EnableOpenXRFeature(BuildTargetGroup.Android, "com.unity.openxr.feature.input.oculustouch");

            // --- Build Settings: make sure the 4 tour scenes are present in order ---
            EnsureScenesInBuild();

            AssetDatabase.SaveAssets();
            Debug.Log("[QuestBuild] Project configured for Meta Quest 3S. Use 'BMU VR Tour > Quest > 2. Build APK'.");
        }

        [MenuItem("BMU VR Tour/Quest/2. Build APK", false, 31)]
        public static void BuildApk()
        {
            BuildInternal(false);
        }

        [MenuItem("BMU VR Tour/Quest/3. Build And Run (headset via USB)", false, 32)]
        public static void BuildAndRun()
        {
            BuildInternal(true);
        }

        private static void BuildInternal(bool run)
        {
            ConfigureForQuest();

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[QuestBuild] No enabled scenes in Build Settings. Run 'Build Complete VR Tour Scene' first.");
                return;
            }

            Directory.CreateDirectory(BuildDir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(BuildDir, ApkName),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[QuestBuild] SUCCESS: {options.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB). " +
                          $"Install with: adb install -r \"{options.locationPathName}\"");
            }
            else
            {
                Debug.LogError($"[QuestBuild] Build {report.summary.result}: {report.summary.totalErrors} error(s). See Console.");
            }
        }

        private static void EnableOpenXRFeature(BuildTargetGroup group, string featureId)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
            if (feature == null)
            {
                Debug.LogWarning($"[QuestBuild] OpenXR feature not found: {featureId}");
                return;
            }
            if (!feature.enabled)
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                Debug.Log($"[QuestBuild] Enabled OpenXR feature {feature.name}");
            }
        }

        private static void EnsureScenesInBuild()
        {
            string[] required =
            {
                "Assets/Scenes/Scene_Campus_Exterior.unity",
            };
            var list = required
                .Where(File.Exists)
                .Select(p => new EditorBuildSettingsScene(p, true))
                .ToArray();
            if (list.Length == required.Length)
            {
                EditorBuildSettings.scenes = list;
            }
            else
            {
                Debug.LogWarning("[QuestBuild] Some tour scenes are missing; leaving Build Settings untouched.");
            }
        }
    }
}
