using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Builds a standalone player containing ONLY the movement playground, so two
    /// instances can be run side by side to test multiplayer. Separate from
    /// BuildScript.cs, which builds the courthouse — this must not disturb it.
    ///
    /// Menu:     Case Closed > Prototype > 4. Build Playground Player
    /// Headless: Unity -batchmode -projectPath game
    ///             -executeMethod CaseClosed.EditorTools.Prototype.PrototypeBuildScript.BuildWindows
    /// Output:   game/Build/Prototype/PlayerPrototype.exe
    /// </summary>
    public static class PrototypeBuildScript
    {
        public const string OutputPath = "Build/Prototype/PlayerPrototype.exe";

        [MenuItem("Case Closed/Prototype/4. Build Playground Player", priority = 103)]
        public static void BuildFromMenu() => Build(exitAfter: false);

        public static void BuildWindows() => Build(exitAfter: true);

        /// <summary>
        /// Stamps the build with a version, the git commit and the time, so two
        /// players can confirm they are running the same thing before debugging a
        /// desync that is really a version mismatch.
        ///
        /// Written BEFORE BuildPlayer so it is picked up by the asset database and
        /// included in the player; writing it afterwards would ship the previous
        /// build's stamp, which is worse than no stamp at all.
        /// </summary>
        private static void WriteBuildStamp()
        {
            string hash = "nogit";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = System.IO.Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(4000);
                if (!string.IsNullOrEmpty(output)) hash = output;
            }
            catch (System.Exception e) { Debug.LogWarning("[Prototype] no git hash: " + e.Message); }

            string stamp = $"v{PlayerSettings.bundleVersion} · {hash} · " +
                           System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            const string folder = "Assets/Resources";
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

            System.IO.File.WriteAllText($"{folder}/{CaseClosed.Game.Prototype.Net.BuildStamp.ResourceName}.txt", stamp);
            AssetDatabase.Refresh();

            Debug.Log($"[Prototype] build stamp: {stamp}");
        }

        private static void Build(bool exitAfter)
        {
            WriteBuildStamp();

            // Windowed and small, so two copies fit on one screen for side-by-side testing.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 960;
            PlayerSettings.defaultScreenHeight = 540;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // vital: an unfocused instance must keep simulating

            var opts = new BuildPlayerOptions
            {
                // Courthouse first so the player boots into it; the movement
                // playground ships too, as a lab for testing the controller alone.
                scenes = new[]
                {
                    CaseClosed.EditorTools.Greybox.CourthouseBuilder.ScenePath,
                    PrototypeAssets.TestScenePath,
                },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Prototype] Build {report.summary.result}, " +
                      $"{report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalTime.TotalSeconds:F0}s -> {OutputPath}");

            if (exitAfter)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
