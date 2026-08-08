using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game.Archive;

namespace CaseClosed.EditorTools.Greybox
{
    /// <summary>
    /// The Forensics Lab: a bay in the atrium's north-EAST corner, mirroring the
    /// Evidence Locker opposite it.
    ///
    /// WHY HERE. The same reasoning that placed the locker: all four atrium walls
    /// already have doors, and the greybox's founding rule is that every route passes
    /// through the atrium with no wing-to-wing shortcuts. A lab bridging the Archive
    /// and the Lounge directly would be exactly that shortcut.
    ///
    /// Putting it opposite the locker also gives the room a readable grammar — you
    /// come in from the Archive, the lab is on your left and the locker on your
    /// right, and the courtroom is straight ahead. Process, then register, then try
    /// it. Placed clear of the pillar at (7, 5).
    ///
    /// Menu: Case Closed > Greybox > Build Forensics Lab
    /// </summary>
    public static class ForensicsLabBuilder
    {
        private const string RootName = "ForensicsLab";

        private const float WestX = 8.2f;
        private const float EastX = 13.6f;
        private const float SouthZ = 3.4f;
        private const float NorthZ = 9.6f;
        private const float PartitionHeight = 3.2f;

        [MenuItem("Case Closed/Greybox/Build Forensics Lab", priority = 3)]
        public static void BuildFromMenu()
        {
            var lab = Build();
            EditorUtility.DisplayDialog("Forensics Lab built",
                lab == null ? "Could not build — is the courthouse scene open?"
                            : "North-east corner of the atrium.\n\n" +
                              "Forensic samples appear on the intake bench. Carry one to " +
                              "the machine and hold E to load it.",
                "OK");
        }

        public static GameObject Build()
        {
            GreyboxKit.BuildMaterials();

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(RootName).transform;

            // Mirror of the locker's shell: a partition on the inner side, a counter
            // across part of the front, open to the atrium.
            GreyboxKit.WallAlongZ("Partition_West", root, SouthZ, NorthZ, WestX,
                                  PartitionHeight, GreyboxKit.Wall);

            GreyboxKit.Box("FloorMark", root,
                new Vector3((WestX + EastX) * 0.5f, 0.02f, (SouthZ + NorthZ) * 0.5f),
                new Vector3(EastX - WestX - 0.4f, 0.04f, NorthZ - SouthZ - 0.4f), GreyboxKit.Metal);

            GreyboxKit.SignText("Sign_Lab", root,
                new Vector3(WestX + 0.15f, 3.9f, (SouthZ + NorthZ) * 0.5f),
                Quaternion.Euler(0f, -90f, 0f), "FORENSICS LAB", 0.85f);

            var intake = BuildIntake(root);
            BuildMachine(root);

            GreyboxKit.Volume("ForensicsLab", root, WestX, SouthZ, EastX, NorthZ, 4f);

            WireIntake(intake);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[Lab] Forensics Lab built.");
            return root.gameObject;
        }

        /// <summary>Where the generator's Lab-tray samples appear.</summary>
        private static Transform BuildIntake(Transform root)
        {
            float x = (WestX + EastX) * 0.5f;

            GreyboxKit.Box("IntakeBench", root, new Vector3(x, 0.5f, SouthZ + 1.1f),
                new Vector3(3.0f, 1.0f, 0.8f), GreyboxKit.Wood);
            GreyboxKit.Box("IntakeStripe", root, new Vector3(x, 1.02f, SouthZ + 0.72f),
                new Vector3(3.0f, 0.05f, 0.03f), GreyboxKit.Accent);
            GreyboxKit.SignText("Sign_Intake", root,
                new Vector3(x, 1.9f, SouthZ + 1.5f), Quaternion.identity, "INTAKE", 0.4f);

            // An empty pivot marking where samples are laid out. Referenced by the
            // custody director, so moving the bench moves the samples with it.
            var marker = new GameObject("IntakePoint").transform;
            marker.SetParent(root, false);
            marker.position = new Vector3(x, 1.05f, SouthZ + 1.1f);
            return marker;
        }

        private static void BuildMachine(Transform root)
        {
            float x = (WestX + EastX) * 0.5f;
            float z = NorthZ - 1.5f;

            GreyboxKit.Box("MachineBody", root, new Vector3(x, 0.75f, z),
                new Vector3(2.2f, 1.5f, 1.0f), GreyboxKit.Metal);
            GreyboxKit.Box("MachineHood", root, new Vector3(x, 1.62f, z + 0.1f),
                new Vector3(2.2f, 0.25f, 0.8f), GreyboxKit.Wall);

            var panel = GreyboxKit.Box("ForensicsMachine", root,
                new Vector3(x, 1.28f, z - 0.52f), new Vector3(0.9f, 0.55f, 0.08f), GreyboxKit.Accent);

            var collider = panel.GetComponent<BoxCollider>();
            if (collider == null) collider = panel.AddComponent<BoxCollider>();

            // Not static: it is interactive, and a batched collider/renderer that no
            // longer agrees with its transform is a trap already paid for once.
            GameObjectUtility.SetStaticEditorFlags(panel, 0);
            panel.isStatic = false;

            panel.AddComponent<NetworkObject>();

            var machine = panel.AddComponent<ForensicsMachine>();
            machine.Prompt = "Load Evidence";
            machine.HoldDuration = 1.5f;
            machine.MaxDistance = 2.5f;
            machine.RequiresLineOfSight = true;
            machine.UseProductionTiming = false;    // development: ~5s instead of 90s
            machine.DevelopmentTimeScale = 0.055f;
        }

        private static void WireIntake(Transform intake)
        {
            var custody = Object.FindAnyObjectByType<EvidenceCustodyDirector>();
            if (custody == null)
            {
                Debug.LogWarning("[Lab] No EvidenceCustodyDirector in the scene to wire the intake to.");
                return;
            }
            custody.LabIntake = intake;
            EditorUtility.SetDirty(custody);
        }
    }
}
