using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Headless player build:
    ///   Unity -batchmode -projectPath game -executeMethod CaseClosed.EditorTools.BuildScript.BuildWindows
    /// Output: game/Build/CaseClosed.exe (windowed 1600x900 — graybox-friendly).
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Case Closed/Build Windows Player")]
        public static void BuildWindowsFromMenu() => Build(exitAfter: false);

        public static void BuildWindows() => Build(exitAfter: true);

        private static void Build(bool exitAfter)
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.productName = "CASE CLOSED (graybox)";

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Courthouse.unity" },
                locationPathName = "Build/CaseClosed.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[CaseClosed] Build result: {report.summary.result}, " +
                      $"size {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"time {report.summary.totalTime.TotalSeconds:F0}s");
            if (exitAfter)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
