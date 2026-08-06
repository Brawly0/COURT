using CaseClosed.Game;
using CaseClosed.Game.Prototype;
using CaseClosed.Game.Prototype.Net;
using CaseClosed.Game.Prototype.Voice;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Builds the placeholder character out of boxes and saves it as a prefab.
    ///
    /// Everything is generated in-project — no imported models, no store assets.
    /// The look is a generic courthouse suit: boxy body, oversized head, flat face,
    /// readable silhouette at gameplay distance. Original geometry, not a copy of
    /// any existing character.
    ///
    /// The rig is a plain Transform hierarchy of empty pivots ("joints") with box
    /// meshes hanging off them. No skinning, no Avatar. That is the whole trick
    /// that makes procedural animation possible without a modelling package —
    /// and it means you can swap in a real skinned model later by keeping the same
    /// joint names.
    ///
    /// Menu: Case Closed > Prototype > 1. Build Character Prefab
    /// </summary>
    public static class PrototypeCharacterBuilder
    {
        // ---- proportions, in metres. Total height ~1.81m ----
        // Slightly big head + slightly short limbs = readable and a bit goofy,
        // which is what a chaotic multiplayer game wants.
        private const float HipY = 0.92f;
        private const float ThighLength = 0.46f;
        private const float ShinLength = 0.36f;
        private const float TorsoHeight = 0.53f;
        private const float ShoulderY = 0.44f;   // local to hips
        private const float UpperArmLength = 0.30f;
        private const float ForearmLength = 0.28f;
        private const float NeckY = 0.53f;       // local to hips
        private const float HeadHeight = 0.36f;

        [MenuItem("Case Closed/Prototype/1. Build Character Prefab", priority = 100)]
        public static GameObject BuildFromMenu()
        {
            var prefab = Build();
            EditorUtility.DisplayDialog("Character built",
                $"Saved to {PrototypeAssets.CharacterPrefabPath}", "OK");
            return prefab;
        }

        public static GameObject Build()
        {
            PrototypeAssets.EnsureAllFolders();

            var skin = PrototypeAssets.GetOrCreateMaterial("Proto_Skin", new Color(0.85f, 0.68f, 0.55f));
            var suit = PrototypeAssets.GetOrCreateMaterial("Proto_Suit", new Color(0.15f, 0.16f, 0.20f));
            var shirt = PrototypeAssets.GetOrCreateMaterial("Proto_Shirt", new Color(0.91f, 0.91f, 0.88f));
            var tie = PrototypeAssets.GetOrCreateMaterial("Proto_Tie", new Color(0.48f, 0.11f, 0.13f));
            var shoe = PrototypeAssets.GetOrCreateMaterial("Proto_Shoe", new Color(0.07f, 0.07f, 0.08f));
            var hair = PrototypeAssets.GetOrCreateMaterial("Proto_Hair", new Color(0.17f, 0.12f, 0.09f));
            var eyeWhite = PrototypeAssets.GetOrCreateMaterial("Proto_EyeWhite", new Color(0.96f, 0.96f, 0.95f));
            var eyeDark = PrototypeAssets.GetOrCreateMaterial("Proto_EyeDark", new Color(0.05f, 0.05f, 0.07f));

            var root = new GameObject("PlayerPrototype");
            var visual = PrototypeAssets.Joint("Visual", root.transform, Vector3.zero);
            var hips = PrototypeAssets.Joint("Hips", visual, new Vector3(0f, HipY, 0f));

            BuildLegs(hips, suit, shoe);
            var torso = BuildTorso(hips, suit, shirt, tie);
            BuildArms(torso, suit, skin);
            BuildHead(torso, skin, hair, eyeWhite, eyeDark);

            AddComponents(root);

            PrototypeAssets.EnsureFolder(PrototypeAssets.PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrototypeAssets.CharacterPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Prototype] Character prefab written to {PrototypeAssets.CharacterPrefabPath}");
            return prefab;
        }

        private static void BuildLegs(Transform hips, Material suit, Material shoe)
        {
            // Mirrored pair. side = -1 is the character's left (it faces +Z, so +X is its right).
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                float side = left ? -1f : 1f;
                string s = left ? "L" : "R";

                var thigh = PrototypeAssets.Joint($"Leg_{s}", hips, new Vector3(side * 0.12f, 0f, 0f));
                PrototypeAssets.Box($"LegMesh_{s}", thigh,
                    new Vector3(0f, -ThighLength * 0.5f, 0f), new Vector3(0.17f, ThighLength, 0.17f), suit);

                var shin = PrototypeAssets.Joint($"Shin_{s}", thigh, new Vector3(0f, -ThighLength, 0f));
                PrototypeAssets.Box($"ShinMesh_{s}", shin,
                    new Vector3(0f, -ShinLength * 0.5f, 0f), new Vector3(0.15f, ShinLength, 0.15f), suit);

                var foot = PrototypeAssets.Joint($"Foot_{s}", shin, new Vector3(0f, -ShinLength, 0f));
                PrototypeAssets.Box($"FootMesh_{s}", foot,
                    new Vector3(0f, -0.05f, 0.05f), new Vector3(0.16f, 0.10f, 0.27f), shoe);
            }
        }

        private static Transform BuildTorso(Transform hips, Material suit, Material shirt, Material tie)
        {
            var torso = PrototypeAssets.Joint("Torso", hips, Vector3.zero);

            PrototypeAssets.Box("TorsoMesh", torso,
                new Vector3(0f, TorsoHeight * 0.5f, 0f), new Vector3(0.47f, TorsoHeight, 0.27f), suit);

            // Shirt panel and tie sit just proud of the jacket front so they read
            // as separate garments without any texture work.
            PrototypeAssets.Box("ShirtMesh", torso,
                new Vector3(0f, 0.34f, 0.129f), new Vector3(0.17f, 0.32f, 0.03f), shirt);
            PrototypeAssets.Box("TieMesh", torso,
                new Vector3(0f, 0.29f, 0.143f), new Vector3(0.06f, 0.28f, 0.02f), tie);

            return torso;
        }

        private static void BuildArms(Transform torso, Material suit, Material skin)
        {
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                float side = left ? -1f : 1f;
                string s = left ? "L" : "R";

                var arm = PrototypeAssets.Joint($"Arm_{s}", torso, new Vector3(side * 0.29f, ShoulderY, 0f));
                PrototypeAssets.Box($"ArmMesh_{s}", arm,
                    new Vector3(0f, -UpperArmLength * 0.5f, 0f), new Vector3(0.12f, UpperArmLength, 0.12f), suit);

                var forearm = PrototypeAssets.Joint($"Forearm_{s}", arm, new Vector3(0f, -UpperArmLength, 0f));
                PrototypeAssets.Box($"ForearmMesh_{s}", forearm,
                    new Vector3(0f, -ForearmLength * 0.5f, 0f), new Vector3(0.11f, ForearmLength, 0.11f), suit);

                var hand = PrototypeAssets.Joint($"Hand_{s}", forearm, new Vector3(0f, -ForearmLength, 0f));
                PrototypeAssets.Box($"HandMesh_{s}", hand,
                    new Vector3(0f, -0.055f, 0f), new Vector3(0.115f, 0.115f, 0.10f), skin);
            }
        }

        private static void BuildHead(Transform torso, Material skin, Material hair,
                                      Material eyeWhite, Material eyeDark)
        {
            var head = PrototypeAssets.Joint("Head", torso, new Vector3(0f, NeckY, 0f));

            PrototypeAssets.Box("NeckMesh", head, new Vector3(0f, 0.02f, 0f), new Vector3(0.13f, 0.08f, 0.13f), skin);
            PrototypeAssets.Box("HeadMesh", head, new Vector3(0f, 0.18f, 0f), new Vector3(0.35f, HeadHeight, 0.33f), skin);

            // Hair as a slab across the crown, dropping slightly down the back.
            PrototypeAssets.Box("HairMesh", head, new Vector3(0f, 0.365f, -0.015f), new Vector3(0.36f, 0.07f, 0.35f), hair);
            PrototypeAssets.Box("HairBackMesh", head, new Vector3(0f, 0.26f, -0.16f), new Vector3(0.36f, 0.20f, 0.04f), hair);

            // Face. Front of the head is z = +0.165; each feature sits a hair proud
            // of that so it never z-fights with the skull.
            const float faceZ = 0.167f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                string s = i == 0 ? "L" : "R";

                PrototypeAssets.Box($"Eye_{s}", head, new Vector3(side * 0.075f, 0.205f, faceZ),
                    new Vector3(0.095f, 0.10f, 0.015f), eyeWhite);
                PrototypeAssets.Box($"Pupil_{s}", head, new Vector3(side * 0.075f, 0.20f, faceZ + 0.008f),
                    new Vector3(0.045f, 0.055f, 0.012f), eyeDark);
                PrototypeAssets.Box($"Brow_{s}", head, new Vector3(side * 0.077f, 0.281f, faceZ),
                    new Vector3(0.105f, 0.028f, 0.015f), hair);
            }

            PrototypeAssets.Box("Mouth", head, new Vector3(0f, 0.087f, faceZ),
                new Vector3(0.115f, 0.022f, 0.015f), eyeDark);
        }

        /// <summary>
        /// Wires the runtime components. Values here are the defaults you will
        /// actually play with — every one is exposed in the Inspector.
        /// </summary>
        private static void AddComponents(GameObject root)
        {
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.78f;
            controller.radius = 0.28f;
            controller.center = new Vector3(0f, 0.89f, 0f);
            controller.slopeLimit = 47f;      // anything steeper slides
            controller.stepOffset = 0.42f;    // clears the 0.2m playground stairs easily
            controller.skinWidth = 0.03f;
            controller.minMoveDistance = 0f;  // 0 avoids the controller ignoring tiny moves

            root.AddComponent<PlayerInputReader>();
            root.AddComponent<PlayerMovement>();

            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false; // code drives movement, never animation
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var driver = root.AddComponent<PlayerAnimatorDriver>();
            driver.Animator = animator;

            AddNetworkComponents(root, animator);
        }

        /// <summary>
        /// The multiplayer layer. Order matters: NetworkObject must exist before any
        /// NetworkBehaviour, or Unity refuses to attach them.
        ///
        /// None of this affects single-player — outside a session these components
        /// are inert, and PLAY OFFLINE strips them entirely.
        /// </summary>
        private static void AddNetworkComponents(GameObject root, Animator animator)
        {
            root.AddComponent<NetworkObject>();

            // Owner-authoritative transform replication. Reuses the existing class
            // from Scripts/Net rather than a second copy — it has no case/courthouse
            // dependencies, and its file-naming comment documents a real prefab-binding
            // bug that is not worth rediscovering.
            var netTransform = root.AddComponent<ClientNetworkTransform>();
            netTransform.InLocalSpace = false;
            netTransform.Interpolate = true;   // smooths the ~20Hz tick into every frame

            root.AddComponent<PrototypeNetPlayer>();

            var sync = root.AddComponent<PlayerNetworkSync>();
            sync.Animator = animator;

            AddVoiceComponents(root);
        }

        /// <summary>
        /// Proximity voice. The AudioSource lives on the character itself, which is
        /// the whole trick: Unity then does distance and direction for free, and the
        /// voice genuinely comes out of the body you can see.
        /// </summary>
        private static void AddVoiceComponents(GameObject root)
        {
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;   // 3D. 0 here would make every voice global.
            source.dopplerLevel = 0f;
            source.priority = 64;       // above ambience, below anything critical

            root.AddComponent<VoiceCapture>();
            root.AddComponent<VoicePlayback>();

            var voice = root.AddComponent<PlayerVoice>();
            voice.Capture = root.GetComponent<VoiceCapture>();
            voice.Playback = root.GetComponent<VoicePlayback>();
            voice.MaxVoiceDistance = 18f;
            voice.MinVoiceVolume = 0f;
            voice.FalloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        }
    }
}
