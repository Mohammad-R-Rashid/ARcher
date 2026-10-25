using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif
using UnityEngine;

namespace Siege.EditorTools
{
    // Command-line / menu iOS build plus the Xcode post-processing the haptics plugin needs.
    public static class iOSBuild
    {
        public const string OutputDir = "Build/iOS";

        [MenuItem("Siege/Build iOS (Xcode project)")]
        public static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Siege/Scenes/Main.unity" },
                locationPathName = OutputDir,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Siege] iOS build: {report.summary.result} errors={report.summary.totalErrors} -> {Path.GetFullPath(OutputDir)}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        [PostProcessBuild(100)]
        public static void OnPostprocess(BuildTarget target, string path)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS) return;
            var projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            var fw = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(fw, "CoreHaptics.framework", true);
            proj.SetBuildProperty(proj.GetUnityMainTargetGuid(), "ENABLE_BITCODE", "NO");
            proj.SetBuildProperty(fw, "ENABLE_BITCODE", "NO");
            proj.WriteToFile(projPath);

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var caps = plist.root["UIRequiredDeviceCapabilities"] as PlistElementArray ?? plist.root.CreateArray("UIRequiredDeviceCapabilities");
            bool hasArkit = false;
            foreach (var v in caps.values) if (v.AsString() == "arkit") hasArkit = true;
            if (!hasArkit) caps.AddString("arkit");
            plist.root.SetBoolean("UIStatusBarHidden", true);
            // The bow glove talks to the app over the phone's hotspot (UDP 9000); iOS asks for local-network access.
            plist.root.SetString("NSLocalNetworkUsageDescription", "Connects to your bow glove over WiFi.");
            plist.WriteToFile(plistPath);
#endif
        }
    }
}
