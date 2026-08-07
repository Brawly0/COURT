using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game.Archive;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Drops a "deal test evidence" bench into the existing interaction test area.
    ///
    /// WHY A BENCH AND NOT A LOOSE FOLDER: a folder lying on the floor would need a
    /// backing EvidenceInstance to be pickable, and one invented by hand would be a
    /// second, fake path through custody — the thing most likely to pass while the
    /// real path is broken. The bench instead runs the real generator, real
    /// placement and real reveal, and simply does it next to you.
    ///
    /// Idempotent: re-running replaces the existing bench rather than stacking a
    /// second one, so it is safe to run after any scene change.
    ///
    /// Menu: Case Closed > Prototype > 5. Build Carry Test Bench
    /// </summary>
    public static class CarryTestBenchBuilder
    {
        private const string RootName = "InteractionTestArea";
        private const string BenchName = "CarryTestBench";

        // On the test pad, between the sign at z=-25 and the prop row at z=-32, so
        // it is the first thing reached walking down from the atrium.
        private static readonly Vector3 BenchPosition = new Vector3(0f, 0f, -28f);

        [MenuItem("Case Closed/Prototype/5. Build Carry Test Bench", priority = 104)]
        public static void BuildFromMenu()
        {
            var bench = Build();
            EditorUtility.DisplayDialog("Carry test bench built",
                bench == null
                    ? "Could not find the interaction test area."
                    : "Walk to the TEST PAD (south of the atrium) and press E on the bench.\n\n" +
                      "Two real folders appear. Pick one up with E, drop with G.",
                "OK");
        }

        public static GameObject Build()
        {
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                Debug.LogWarning($"[CarryTest] No '{RootName}' in the scene — " +
                                 "run Case Closed > Greybox > Build Courthouse first.");
                return null;
            }

            // Replace rather than duplicate.
            var existing = root.transform.Find(BenchName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var wood = PrototypeAssets.GetOrCreateMaterial("Proto_TestBench", new Color(0.42f, 0.30f, 0.19f));
            var accent = PrototypeAssets.GetOrCreateMaterial("Proto_TestAccent", new Color(0.95f, 0.72f, 0.25f));

            var bench = new GameObject(BenchName);
            bench.transform.SetParent(root.transform, false);
            bench.transform.position = BenchPosition;
            // Face the atrium, so players walking south meet it head-on and the
            // folders are dealt onto the near edge.
            bench.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            // Table: top plus two end panels. Deliberately waist height so a folder
            // dealt at 0.95 m sits where it can be seen and reached.
            PrototypeAssets.Box("BenchTop", bench.transform,
                new Vector3(0f, 0.88f, 0f), new Vector3(1.90f, 0.10f, 0.70f), wood);
            PrototypeAssets.Box("BenchLeg_L", bench.transform,
                new Vector3(-0.85f, 0.44f, 0f), new Vector3(0.14f, 0.78f, 0.62f), wood);
            PrototypeAssets.Box("BenchLeg_R", bench.transform,
                new Vector3(0.85f, 0.44f, 0f), new Vector3(0.14f, 0.78f, 0.62f), wood);

            // Amber strip along the front edge, matching the carry HUD accent, so the
            // bench reads as a dev fixture and not courthouse furniture.
            PrototypeAssets.Box("BenchStripe", bench.transform,
                new Vector3(0f, 0.88f, 0.36f), new Vector3(1.90f, 0.06f, 0.02f), accent);

            AddInteractable(bench, accent);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[CarryTest] Bench built at {BenchPosition} under {RootName}.");
            return bench;
        }

        /// <summary>
        /// The interactable is a separate child with its own collider, sitting just
        /// above the bench top. Putting it on the bench root would make the whole
        /// table a 2 m wide target and swallow prompts meant for anything behind it.
        /// </summary>
        private static void AddInteractable(GameObject bench, Material accent)
        {
            var panel = PrototypeAssets.Box("DealPanel", bench.transform,
                new Vector3(0f, 1.18f, -0.22f), new Vector3(0.52f, 0.42f, 0.08f), accent);

            var collider = panel.GetComponent<BoxCollider>();
            if (collider == null) collider = panel.AddComponent<BoxCollider>();

            // NetworkObject first — Unity refuses to attach a NetworkBehaviour
            // without one, and the ordering trap is documented in the character builder.
            panel.AddComponent<NetworkObject>();

            var dispenser = panel.AddComponent<CarryTestDispenser>();
            dispenser.Prompt = "Deal Test Evidence";
            dispenser.MaxDistance = 3.5f;
            dispenser.RequiresLineOfSight = true;
            dispenser.HoldDuration = 0f;      // instant: this is a test fixture
            dispenser.Count = 2;

            // Offsets are measured from the PANEL, which already sits at y = 1.18 —
            // not from the bench base. Setting a positive height here left the
            // folders hovering 27 cm above the table. -0.22 rests them on the top
            // surface (0.88 + half of 0.10 = 0.93).
            dispenser.SurfaceHeight = -0.22f;
            dispenser.Reach = 0.45f;
        }
    }
}
