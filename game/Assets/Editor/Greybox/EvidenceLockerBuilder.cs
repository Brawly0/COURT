using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game.Archive;

namespace CaseClosed.EditorTools.Greybox
{
    /// <summary>
    /// The Evidence Locker: a walled bay in the atrium's north-west corner.
    ///
    /// WHY A BAY AND NOT A FOURTH WING. All four atrium walls already have doors —
    /// west to the Archive, east to the Lounge, north to the Courtroom, south to the
    /// test area — so a new wing would mean cutting a new opening and redesigning
    /// circulation. More importantly, the greybox's founding rule is that every
    /// route passes through the atrium and there are no wing-to-wing shortcuts. A
    /// locker room bridging Archive and Courtroom directly would be the exact
    /// shortcut that rule exists to prevent.
    ///
    /// The north-west corner is the natural spot: you come in from the Archive
    /// through the west door carrying a folder, the locker is immediately on your
    /// right, and the Courtroom door is straight ahead to the north. It is on the
    /// path rather than a detour, and it is still in the room where everyone can
    /// see you hand something in.
    ///
    /// Placed clear of the existing pillar at (-7, 5) and inside the existing atrium
    /// floor slab, so nothing already in the scene has to move.
    ///
    /// Menu: Case Closed > Greybox > Build Evidence Locker
    /// </summary>
    public static class EvidenceLockerBuilder
    {
        private const string RootName = "EvidenceLocker";

        // Atrium is x [-14, 14], z [-10, 10]. Pillar at (-7, 5) is 1.4 wide, so its
        // west face is at -7.7; the bay stops at -8.2 to leave a clean gap.
        private const float WestX = -13.6f;
        private const float EastX = -8.2f;
        private const float SouthZ = 3.4f;
        private const float NorthZ = 9.6f;
        private const float PartitionHeight = 3.2f;

        [MenuItem("Case Closed/Greybox/Build Evidence Locker", priority = 1)]
        public static void BuildFromMenu()
        {
            var locker = Build();
            EditorUtility.DisplayDialog("Evidence Locker built",
                locker == null
                    ? "Could not build — is the courthouse scene open?"
                    : "North-west corner of the atrium.\n\n" +
                      "Carry a folder to the desk and hold E to register it.",
                "OK");
        }

        public static GameObject Build()
        {
            GreyboxKit.BuildMaterials();

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);   // replace, never stack

            var root = new GameObject(RootName).transform;

            BuildShell(root);
            BuildLockers(root);
            var terminal = BuildRegistrationDesk(root);

            GreyboxKit.Volume("EvidenceLocker", root, WestX, SouthZ, EastX, NorthZ, 4f);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[Locker] Evidence Locker built. Terminal at {terminal.transform.position}.");
            return root.gameObject;
        }

        /// <summary>
        /// A partition on the east side and a counter across part of the south,
        /// leaving a walk-in gap. Deliberately NOT a sealed room: handing evidence in
        /// should be visible from the atrium floor, because being seen doing it is
        /// information other players are entitled to.
        /// </summary>
        private static void BuildShell(Transform root)
        {
            // East partition, stopping short of the north wall so the bay reads as
            // open rather than enclosed.
            GreyboxKit.WallAlongZ("Partition_East", root, SouthZ, NorthZ, EastX,
                                  PartitionHeight, GreyboxKit.Wall);

            // Waist-high intake counter along the south edge, with a gap at the east
            // end to walk through.
            GreyboxKit.Box("IntakeCounter", root,
                new Vector3((WestX + EastX - 2.6f) * 0.5f, 0.55f, SouthZ),
                new Vector3(EastX - WestX - 2.6f, 1.1f, 0.7f), GreyboxKit.Wood);

            // Floor marking so the bay is legible from across the atrium.
            GreyboxKit.Box("FloorMark", root,
                new Vector3((WestX + EastX) * 0.5f, 0.02f, (SouthZ + NorthZ) * 0.5f),
                new Vector3(EastX - WestX - 0.4f, 0.04f, NorthZ - SouthZ - 0.4f), GreyboxKit.Accent);

            // Signs: one facing the atrium (how you find it), one inside.
            GreyboxKit.SignText("Sign_Locker", root,
                new Vector3(EastX - 0.15f, 3.9f, (SouthZ + NorthZ) * 0.5f),
                Quaternion.Euler(0f, 90f, 0f), "EVIDENCE LOCKER", 0.85f);

            GreyboxKit.SignText("Sign_Locker_Inner", root,
                new Vector3((WestX + EastX) * 0.5f, 3.2f, NorthZ - 0.15f),
                Quaternion.Euler(0f, 180f, 0f), "EVIDENCE LOCKER", 0.7f);
        }

        /// <summary>Secure shelving along the west wall. Scenery — nothing interactive.</summary>
        private static void BuildLockers(Transform root)
        {
            for (int i = 0; i < 4; i++)
            {
                float z = SouthZ + 1.3f + i * 1.45f;
                GreyboxKit.Box($"LockerBank_{i:00}", root,
                    new Vector3(WestX + 0.45f, 1.1f, z),
                    new Vector3(0.8f, 2.2f, 1.25f), GreyboxKit.Metal);
            }
        }

        /// <summary>
        /// The desk and the terminal on it. The terminal is a separate child with its
        /// own collider so the interaction ray targets the screen rather than the
        /// whole desk — the same reason the carry test bench splits its panel out.
        /// </summary>
        private static GameObject BuildRegistrationDesk(Transform root)
        {
            float deskX = (WestX + EastX) * 0.5f + 1.1f;
            float deskZ = NorthZ - 1.4f;

            GreyboxKit.Box("RegistrationDesk", root,
                new Vector3(deskX, 0.5f, deskZ), new Vector3(2.4f, 1.0f, 0.9f), GreyboxKit.Wood);

            var terminal = GreyboxKit.Box("RegistrationTerminal", root,
                new Vector3(deskX, 1.32f, deskZ - 0.1f),
                new Vector3(0.62f, 0.5f, 0.1f), GreyboxKit.Accent);

            // GreyboxKit.Box strips colliders (they are scenery by default), and an
            // interactable without one can never be hit by the interaction ray.
            var collider = terminal.GetComponent<BoxCollider>();
            if (collider == null) collider = terminal.AddComponent<BoxCollider>();

            // Scenery is marked static by GreyboxKit; the terminal must not be, or
            // it joins a static batch and its collider/renderer stop agreeing with
            // its transform. Same trap that made carried evidence invisible.
            GameObjectUtility.SetStaticEditorFlags(terminal, 0);
            terminal.isStatic = false;

            // NetworkObject before any NetworkBehaviour, or Unity refuses to attach.
            terminal.AddComponent<NetworkObject>();

            var registration = terminal.AddComponent<RegistrationTerminal>();
            registration.Prompt = "Register Evidence";
            registration.HoldDuration = 2.5f;     // long enough to be interruptible
            registration.MaxDistance = 2.5f;
            registration.RequiresLineOfSight = true;

            return terminal;
        }
    }
}
