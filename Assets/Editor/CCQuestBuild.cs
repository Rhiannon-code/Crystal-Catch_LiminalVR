using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    public static class CCQuestBuild
    {
        private const string Scene = "Assets/Scenes/MineCart.unity";
        private const string OutputDir = "Builds/Quest";
        private const string ApkName = "CrystalCatch-Quest.apk";

        [MenuItem("Crystal Catch/Quest/Configure Player Settings for Quest 2")]
        public static void Configure()
        {
            // IL2CPP first: ARM64 is not a legal architecture under Mono, so setting them the other
            // way round silently leaves you on ARMv7
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            // The SDK's legacy path. Matches this project having no UNITY_XR define, which is also
            // what makes grip map to VRButton.Three in PickaxePickup
            PlayerSettings.virtualRealitySupported = true;
            PlayerSettings.SetVirtualRealitySDKs(BuildTargetGroup.Android, new[] { "Oculus" });

            // Single pass halves the per-eye draw cost and is the only sane default on a Quest 2
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;

            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.development = false;

            Debug.Log("[CCQuestBuild] Player settings configured for Quest 2: IL2CPP, ARM64, " +
                      "Oculus VR, single pass, ASTC, Gradle.");
        }

        [MenuItem("Crystal Catch/Quest/Build APK")]
        public static void BuildApk()
        {
            Build(false);
        }

        [MenuItem("Crystal Catch/Quest/Build APK and Install to Headset")]
        public static void BuildAndRun()
        {
            Build(true);
        }

        private static void Build(bool install)
        {
            if (!File.Exists(Scene))
            {
                Debug.LogError("[CCQuestBuild] " + Scene + " does not exist. Run " +
                               "Crystal Catch > Build Mine Cart Scene first.");
                return;
            }

            Configure();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[CCQuestBuild] Switching to Android. The first switch reimports every " +
                          "asset and takes a while, let it finish, then run this again.");

                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                return;
            }

            Directory.CreateDirectory(OutputDir);
            string path = Path.Combine(OutputDir, ApkName);

            // The scene list is passed explicitly rather than read from the Build Settings window, so
            // this cannot pick up whatever happened to be ticked there
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = install ? BuildOptions.AutoRunPlayer : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[CCQuestBuild] Built " + path + " (" + (summary.totalSize / 1048576) +
                          " MB) in " + summary.totalTime.TotalSeconds.ToString("0") + " s." +
                          (install ? " Installing to the connected headset." : ""));
            }
            else
            {
                Debug.LogError("[CCQuestBuild] Build " + summary.result + " with " +
                               summary.totalErrors + " error(s). See the lines above for the cause.");
            }
        }

        /// Reports what the player settings actually are, for when a build behaves unexpectedly and
        /// the window that would normally show you is not available
        [MenuItem("Crystal Catch/Quest/Log Current Build Settings")]
        public static void LogSettings()
        {
            Debug.Log(
                "[CCQuestBuild] Active target: " + EditorUserBuildSettings.activeBuildTarget +
                "\n  Scripting backend: " + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) +
                "\n  Architectures:     " + PlayerSettings.Android.targetArchitectures +
                "\n  Min SDK:           " + PlayerSettings.Android.minSdkVersion +
                "\n  VR supported:      " + PlayerSettings.virtualRealitySupported +
                "\n  Stereo path:       " + PlayerSettings.stereoRenderingPath +
                "\n  Texture format:    " + EditorUserBuildSettings.androidBuildSubtarget +
                "\n  Bundle id:         " + PlayerSettings.applicationIdentifier);
        }
    }
}
