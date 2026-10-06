using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BorrowedSeconds.EditorTools
{
    /// <summary>Menu items and batch-mode entry points for building players.</summary>
    public static class BuildScript
    {
        static readonly string[] Scenes = { "Assets/Scenes/Main.unity" };

        [MenuItem("Borrowed Seconds/Build Linux Player")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/BorrowedSeconds.x86_64", false);

        /// <summary>Development build (faster iteration, logs stack traces).</summary>
        public static void BuildLinuxDev() => Build(BuildTarget.StandaloneLinux64, "Builds/LinuxDev/BorrowedSeconds.x86_64", true);

        /// <summary>
        /// macOS app bundle, universal (Intel + Apple silicon), Mono. Unsigned and not notarized:
        /// signing needs an Apple Developer ID on a Mac.
        /// </summary>
        [MenuItem("Borrowed Seconds/Build macOS Player")]
        public static void BuildMac()
        {
            // UnityEditor.OSXStandalone.UserBuildSettings lives in the Mac support module; reach it
            // by reflection so this script still compiles where that module isn't installed
            var settings = System.Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
            var prop = settings?.GetProperty("architecture");
            if (prop != null) prop.SetValue(null, System.Enum.Parse(prop.PropertyType, "x64ARM64"));
            else Debug.LogWarning("[Build] macOS architecture setting not found; building the default architecture");
            Build(BuildTarget.StandaloneOSX, "Builds/macOS/BorrowedSeconds.app", false);
        }

        /// <summary>Windows x64 (Mono). Needs Unity's Windows Build Support module, not installed here.</summary>
        [MenuItem("Borrowed Seconds/Build Windows Player")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/BorrowedSeconds.exe", false);

        const string BundleId = "com.nearbycoder.borrowedseconds";

        static void Build(BuildTarget target, string path, bool dev)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                Debug.LogError($"[Build] {target} Failed: build support for this platform isn't installed (add it in Unity Hub)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone) != BundleId)
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, BundleId);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = target,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"[Build] {target} {summary.result}: {summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalTime} -> {path}");
            if (Application.isBatchMode) EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
