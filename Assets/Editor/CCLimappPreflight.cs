using System.Collections.Generic;
using System.Text;
using Liminal.SDK.VR.Avatars;
using UnityEditor;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    /// One check to run before Liminal > Build Window.
    ///
    /// The SDK already validates most of this in IssuesUtility, but CheckForAllIssues() evaluates
    /// its gates with || — it short circuits on the first failure and only records a bool in
    /// EditorPrefs, so it tells you THAT something is wrong and not WHICH. This runs each gate
    /// separately and reports all of them.
    ///
    /// It also covers the two things the SDK cannot know about, because they are ours: the perf
    /// readout is a measurement aid that must not ship, and its logging is pure overhead on device.
    public static class CCLimappPreflight
    {
        private const string RequiredUnityVersion = "2019.1.10f1";
        private static readonly Vector3 RequiredHeadLocalPosition = new Vector3(0f, 1.7f, 0f);

        [MenuItem("Crystal Catch/Quest/Preflight Check (.limapp)")]
        public static void Run()
        {
            var fail = new List<string>();
            var pass = new List<string>();

            Check(Application.unityVersion == RequiredUnityVersion,
                  "Unity is " + RequiredUnityVersion,
                  "Unity is " + Application.unityVersion + ", the SDK hard requires " +
                  RequiredUnityVersion + " (IssuesUtility.HasEditorIssues)",
                  pass, fail);

            // The SDK's rendering gate is exactly these two, and CCQuestBuild.Configure() happens to
            // set both — so an APK test run leaves the project satisfying it rather than breaking it
            Check(PlayerSettings.virtualRealitySupported &&
                  PlayerSettings.stereoRenderingPath == StereoRenderingPath.SinglePass,
                  "VR supported + Single Pass stereo",
                  "SDK requires virtualRealitySupported AND Single Pass (currently supported=" +
                  PlayerSettings.virtualRealitySupported + ", path=" +
                  PlayerSettings.stereoRenderingPath + ")",
                  pass, fail);

            var avatar = Object.FindObjectOfType<VRAvatar>();
            bool avatarOk = avatar != null
                            && avatar.Head != null
                            && avatar.Head.Transform.localPosition == RequiredHeadLocalPosition
                            && avatar.Head.Transform.localEulerAngles == Vector3.zero;
            Check(avatarOk,
                  "VRAvatar head at (0, 1.7, 0), unrotated",
                  avatar == null
                      ? "No VRAvatar in the open scene"
                      : "VRAvatar head is at " + avatar.Head.Transform.localPosition +
                        " rot " + avatar.Head.Transform.localEulerAngles + ", SDK requires (0.0, 1.7, 0.0) unrotated",
                  pass, fail);

            // Ours, not the SDK's: the readout is a measurement aid for the standalone APK
            var readout = Object.FindObjectOfType<PerfReadout>();
            Check(readout == null,
                  "No perf readout in the scene",
                  "PerfReadout is still in the scene - run Crystal Catch > Quest > " +
                  "Remove Perf Readout From HUD before building the .limapp",
                  pass, fail);

            Check(!EditorUserBuildSettings.development,
                  "Development Build is off",
                  "Development Build is ON - gameplay logs are compiled back in and IL2CPP is slower",
                  pass, fail);

            var report = new StringBuilder();
            report.Append("[CCLimappPreflight] ")
                  .Append(fail.Count == 0 ? "READY to build the .limapp." : "NOT READY, " + fail.Count + " issue(s).");
            foreach (var line in fail) report.Append("\n   FAIL  ").Append(line);
            foreach (var line in pass) report.Append("\n   ok    ").Append(line);

            report.Append("\n\n   Not covered here, the SDK checks these itself in its Build Window:")
                  .Append("\n   - incompatible packages (Post Processing, Curvy, DOTween)")
                  .Append("\n   - forbidden calls (Application.Quit, SceneManager.Load/UnloadScene, DontDestroyOnLoad)");

            if (fail.Count == 0) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        private static void Check(bool ok, string okText, string failText,
                                  List<string> pass, List<string> fail)
        {
            if (ok) pass.Add(okText);
            else fail.Add(failText);
        }
    }
}
