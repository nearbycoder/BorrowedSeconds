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

        static void Build(BuildTarget target, string path, bool dev)
        {
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
