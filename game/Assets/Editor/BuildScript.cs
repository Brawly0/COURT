using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Player build. Output goes to a plain, findable folder on the Desktop:
    ///   C:/Users/&lt;you&gt;/Desktop/CASE CLOSED GAME/CASE CLOSED.exe
    /// Scene 0 is the Revit courthouse; scene 1 is the procedural one.
    /// Menu: Case Closed > Build Windows Player.
    /// </summary>
    public static class BuildScript
    {
        private static string OutDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
                         "CASE CLOSED GAME");

        [MenuItem("Case Closed/Build Windows Player")]
        public static void BuildWindowsFromMenu() => Build(exitAfter: false);

        public static void BuildWindows() => Build(exitAfter: true);

        [MenuItem("Case Closed/Open Build Folder")]
        public static void OpenBuildFolder()
        {
            Directory.CreateDirectory(OutDir);
            EditorUtility.RevealInFinder(Path.Combine(OutDir, "CASE CLOSED.exe"));
        }

        private static void Build(bool exitAfter)
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.productName = "CASE CLOSED";
            PlayerSettings.companyName = "Case Closed";

            var scenes = new[] { "Assets/Scenes/CourthouseRVT.unity", "Assets/Scenes/Courthouse.unity" }
                .Where(s => File.Exists(s)).ToArray();

            Directory.CreateDirectory(OutDir);
            string exePath = Path.Combine(OutDir, "CASE CLOSED.exe");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
                Debug.Log($"[CaseClosed] BUILD OK -> {exePath}  ({s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:F0}s)");
            else
                Debug.LogError($"[CaseClosed] BUILD {s.result}: {s.totalErrors} errors");

            if (exitAfter) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
