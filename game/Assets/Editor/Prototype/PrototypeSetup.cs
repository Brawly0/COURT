using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Runs all three prototype builders in order, then opens the playground.
    /// Safe to re-run at any time — every builder overwrites its own output and
    /// touches nothing belonging to the courthouse.
    ///
    /// Menu: Case Closed > Prototype > Build Everything
    /// Headless: Unity -batchmode -executeMethod CaseClosed.EditorTools.Prototype.PrototypeSetup.Run
    /// </summary>
    public static class PrototypeSetup
    {
        [MenuItem("Case Closed/Prototype/Build Everything (character + animation + scene)", priority = 0)]
        public static void BuildEverything()
        {
            // The playground replaces the open scene, so don't silently bin edits.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Run();

            EditorSceneManager.OpenScene(PrototypeAssets.TestScenePath);
            EditorUtility.DisplayDialog(
                "Movement prototype ready",
                "Character, animations and playground built.\n\n" +
                "Press Play, then:\n" +
                "WASD move  ·  Shift sprint  ·  Ctrl walk  ·  Space jump\n" +
                "Mouse look  ·  Esc release cursor  ·  F1 toggle HUD",
                "Play time");
        }

        public static void Run()
        {
            PrototypeAssets.EnsureAllFolders();

            PrototypeCharacterBuilder.Build();
            PrototypeAnimationBuilder.Build();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            PrototypePlaygroundBuilder.Build();

            Debug.Log("[Prototype] Build complete.");
        }
    }
}
