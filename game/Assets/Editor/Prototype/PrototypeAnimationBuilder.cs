using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Generates the placeholder animation clips and the Animator Controller.
    ///
    /// The clips are written in code as rotation curves on the rig's joints. That
    /// is the "basic animation solution we can replace later": no modelling
    /// package, no downloads, and every pose is a number you can nudge in this
    /// file. When real animations arrive, drop them into the same states in the
    /// controller and delete this builder — nothing else changes.
    ///
    /// The state machine is a real Animator Controller, not an if/else chain:
    ///   Locomotion (1D blend tree on Speed: Idle -> Walk -> Run -> Sprint)
    ///   Jump -> Fall -> Land -> back to Locomotion
    /// All transitions read measured Speed / Grounded / VerticalSpeed.
    ///
    /// Menu: Case Closed > Prototype > 2. Build Animations
    /// </summary>
    public static class PrototypeAnimationBuilder
    {
        // Joint paths, relative to the object holding the Animator (the prefab root).
        private const string Hips = "Visual/Hips";
        private const string Torso = "Visual/Hips/Torso";
        private const string Head = "Visual/Hips/Torso/Head";
        private const string LegL = "Visual/Hips/Leg_L";
        private const string LegR = "Visual/Hips/Leg_R";
        private const string ShinL = "Visual/Hips/Leg_L/Shin_L";
        private const string ShinR = "Visual/Hips/Leg_R/Shin_R";
        private const string ArmL = "Visual/Hips/Torso/Arm_L";
        private const string ArmR = "Visual/Hips/Torso/Arm_R";
        private const string ForearmL = "Visual/Hips/Torso/Arm_L/Forearm_L";
        private const string ForearmR = "Visual/Hips/Torso/Arm_R/Forearm_R";

        private const float HipRestY = 0.92f;

        // Arms splay slightly out from the body so the silhouette reads as a person
        // and not a plank. Constant across every clip so blending stays stable.
        private const float ArmSplayL = 7f;
        private const float ArmSplayR = -7f;

        // Blend thresholds. These MUST match the speeds on PlayerMovement, or the
        // character will run while playing a walk.
        private const float WalkSpeed = 1.9f;
        private const float RunSpeed = 4.3f;
        private const float SprintSpeed = 7.0f;

        [MenuItem("Case Closed/Prototype/2. Build Animations", priority = 101)]
        public static AnimatorController BuildFromMenu()
        {
            var controller = Build();
            EditorUtility.DisplayDialog("Animations built",
                $"7 clips + controller saved to {PrototypeAssets.AnimationFolder}", "OK");
            return controller;
        }

        public static AnimatorController Build()
        {
            PrototypeAssets.EnsureAllFolders();

            var idle = BuildIdle();
            var walk = BuildStride("Proto_Walk", 1.00f, legSwing: 24f, armSwing: 18f, kneeBend: 32f, bob: 0.022f, lean: -3f);
            var run = BuildStride("Proto_Run", 0.68f, legSwing: 42f, armSwing: 38f, kneeBend: 55f, bob: 0.040f, lean: -9f);
            var sprint = BuildStride("Proto_Sprint", 0.52f, legSwing: 56f, armSwing: 52f, kneeBend: 72f, bob: 0.055f, lean: -16f);
            var jump = BuildJump();
            var fall = BuildFall();
            var land = BuildLand();

            var controller = BuildController(idle, walk, run, sprint, jump, fall, land);
            BindToCharacterPrefab(controller);
            return controller;
        }

        /// <summary>
        /// Assigns the controller to the character PREFAB.
        ///
        /// This has to happen here, not in the character builder: the controller does
        /// not exist yet when the prefab is created (step 1 runs before step 2).
        ///
        /// It also has to be the prefab and not a scene instance. Netcode spawns
        /// players from the prefab at runtime, so a controller assigned only to a
        /// scene copy reaches nobody — the characters animate in the editor and stand
        /// frozen in-game. That is exactly the bug this line fixes.
        /// </summary>
        private static void BindToCharacterPrefab(AnimatorController controller)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeAssets.CharacterPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[Prototype] No character prefab yet - run step 1, then step 2 again.");
                return;
            }

            var animator = prefab.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("[Prototype] Character prefab has no Animator.");
                return;
            }

            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[Prototype] Animator controller bound to the character prefab.");
        }

        // ------------------------------------------------------------------
        // clip construction
        // ------------------------------------------------------------------

        private static AnimationClip BuildIdle()
        {
            var clip = NewClip("Proto_Idle", loop: true);

            // Barely-there breathing. An idle that is perfectly still looks broken.
            SetPositionY(clip, Hips, Key(0f, HipRestY), Key(1.1f, HipRestY - 0.013f), Key(2.2f, HipRestY));
            SetEuler(clip, Torso, Curve(Key(0f, 1.5f), Key(1.1f, 3f), Key(2.2f, 1.5f)));
            SetEuler(clip, Head, Curve(Key(0f, 0f), Key(1.1f, -2f), Key(2.2f, 0f)));
            SetEuler(clip, ArmL, Curve(Key(0f, 2f), Key(1.1f, 4f), Key(2.2f, 2f)), 0f, ArmSplayL);
            SetEuler(clip, ArmR, Curve(Key(0f, 2f), Key(1.1f, 4f), Key(2.2f, 2f)), 0f, ArmSplayR);
            SetEuler(clip, ForearmL, Curve(Key(0f, 6f), Key(2.2f, 6f)));
            SetEuler(clip, ForearmR, Curve(Key(0f, 6f), Key(2.2f, 6f)));
            SetEuler(clip, LegL, Curve(Key(0f, 0f), Key(2.2f, 0f)));
            SetEuler(clip, LegR, Curve(Key(0f, 0f), Key(2.2f, 0f)));
            SetEuler(clip, ShinL, Curve(Key(0f, 2f), Key(2.2f, 2f)));
            SetEuler(clip, ShinR, Curve(Key(0f, 2f), Key(2.2f, 2f)));

            return Save(clip);
        }

        /// <summary>
        /// One walk/run/sprint cycle. Same curve shape every time, only the
        /// amplitudes and the duration change — which is exactly why walk, run and
        /// sprint blend into each other cleanly instead of popping.
        ///
        /// Negative X on a leg swings it forward (the joint's children hang down
        /// -Y, and the character faces +Z).
        /// </summary>
        private static AnimationClip BuildStride(string name, float length, float legSwing,
                                                 float armSwing, float kneeBend, float bob, float lean)
        {
            var clip = NewClip(name, loop: true);

            float h = length * 0.5f;
            float q = length * 0.25f;
            float tq = length * 0.75f;

            // Legs, half a cycle out of phase with each other.
            SetEuler(clip, LegL, Curve(Key(0f, -legSwing), Key(h, legSwing), Key(length, -legSwing)));
            SetEuler(clip, LegR, Curve(Key(0f, legSwing), Key(h, -legSwing), Key(length, legSwing)));

            // Knees bend on the recovery half, when the foot is coming back through.
            SetEuler(clip, ShinL, Curve(Key(0f, 4f), Key(q, kneeBend * 0.25f), Key(h, kneeBend), Key(tq, kneeBend * 0.35f), Key(length, 4f)));
            SetEuler(clip, ShinR, Curve(Key(0f, kneeBend), Key(q, kneeBend * 0.35f), Key(h, 4f), Key(tq, kneeBend * 0.25f), Key(length, kneeBend)));

            // Arms counter-swing against the legs. That opposition is most of what
            // sells a walk as a walk.
            SetEuler(clip, ArmL, Curve(Key(0f, armSwing), Key(h, -armSwing), Key(length, armSwing)), 0f, ArmSplayL);
            SetEuler(clip, ArmR, Curve(Key(0f, -armSwing), Key(h, armSwing), Key(length, -armSwing)), 0f, ArmSplayR);
            SetEuler(clip, ForearmL, Curve(Key(0f, 10f), Key(h, 30f), Key(length, 10f)));
            SetEuler(clip, ForearmR, Curve(Key(0f, 30f), Key(h, 10f), Key(length, 30f)));

            // Two bobs per stride — one per footfall.
            SetPositionY(clip, Hips,
                Key(0f, HipRestY), Key(q, HipRestY + bob), Key(h, HipRestY),
                Key(tq, HipRestY + bob), Key(length, HipRestY));

            // Faster = more forward lean.
            SetEuler(clip, Torso, Curve(Key(0f, lean), Key(h, lean - 1.5f), Key(length, lean)));
            SetEuler(clip, Head, Curve(Key(0f, -lean * 0.5f), Key(length, -lean * 0.5f)));

            return Save(clip);
        }

        private static AnimationClip BuildJump()
        {
            var clip = NewClip("Proto_Jump", loop: false);

            // Push off, then tuck. Arms swing up and overhead.
            SetEuler(clip, LegL, Curve(Key(0f, 10f), Key(0.16f, -28f), Key(0.42f, -18f)));
            SetEuler(clip, LegR, Curve(Key(0f, 10f), Key(0.16f, -14f), Key(0.42f, -6f)));
            SetEuler(clip, ShinL, Curve(Key(0f, 25f), Key(0.16f, 62f), Key(0.42f, 48f)));
            SetEuler(clip, ShinR, Curve(Key(0f, 25f), Key(0.16f, 40f), Key(0.42f, 26f)));
            SetEuler(clip, ArmL, Curve(Key(0f, 20f), Key(0.18f, -105f), Key(0.42f, -118f)), 0f, ArmSplayL * 2f);
            SetEuler(clip, ArmR, Curve(Key(0f, 20f), Key(0.18f, -105f), Key(0.42f, -118f)), 0f, ArmSplayR * 2f);
            SetEuler(clip, ForearmL, Curve(Key(0f, 10f), Key(0.42f, 22f)));
            SetEuler(clip, ForearmR, Curve(Key(0f, 10f), Key(0.42f, 22f)));
            SetEuler(clip, Torso, Curve(Key(0f, 6f), Key(0.42f, -6f)));
            SetPositionY(clip, Hips, Key(0f, HipRestY - 0.03f), Key(0.18f, HipRestY + 0.02f), Key(0.42f, HipRestY));

            return Save(clip);
        }

        private static AnimationClip BuildFall()
        {
            var clip = NewClip("Proto_Fall", loop: true);

            // Loose, slightly flailing. Legs apart, arms high — reads instantly as
            // "not in control" from across the room.
            SetEuler(clip, LegL, Curve(Key(0f, -20f), Key(0.35f, -12f), Key(0.7f, -20f)));
            SetEuler(clip, LegR, Curve(Key(0f, 12f), Key(0.35f, 20f), Key(0.7f, 12f)));
            SetEuler(clip, ShinL, Curve(Key(0f, 40f), Key(0.35f, 28f), Key(0.7f, 40f)));
            SetEuler(clip, ShinR, Curve(Key(0f, 20f), Key(0.35f, 34f), Key(0.7f, 20f)));
            SetEuler(clip, ArmL, Curve(Key(0f, -120f), Key(0.35f, -138f), Key(0.7f, -120f)), 0f, ArmSplayL * 2.5f);
            SetEuler(clip, ArmR, Curve(Key(0f, -138f), Key(0.35f, -120f), Key(0.7f, -138f)), 0f, ArmSplayR * 2.5f);
            SetEuler(clip, ForearmL, Curve(Key(0f, 25f), Key(0.7f, 25f)));
            SetEuler(clip, ForearmR, Curve(Key(0f, 25f), Key(0.7f, 25f)));
            SetEuler(clip, Torso, Curve(Key(0f, -4f), Key(0.35f, 2f), Key(0.7f, -4f)));
            SetPositionY(clip, Hips, Key(0f, HipRestY), Key(0.7f, HipRestY));

            return Save(clip);
        }

        private static AnimationClip BuildLand()
        {
            var clip = NewClip("Proto_Land", loop: false);

            // Absorb the impact then stand up. The dip is what makes a landing feel
            // like it had weight.
            SetPositionY(clip, Hips,
                Key(0f, HipRestY - 0.02f), Key(0.09f, HipRestY - 0.15f), Key(0.30f, HipRestY));
            SetEuler(clip, LegL, Curve(Key(0f, -14f), Key(0.09f, -26f), Key(0.30f, 0f)));
            SetEuler(clip, LegR, Curve(Key(0f, 8f), Key(0.09f, 20f), Key(0.30f, 0f)));
            SetEuler(clip, ShinL, Curve(Key(0f, 30f), Key(0.09f, 55f), Key(0.30f, 3f)));
            SetEuler(clip, ShinR, Curve(Key(0f, 30f), Key(0.09f, 55f), Key(0.30f, 3f)));
            SetEuler(clip, ArmL, Curve(Key(0f, -70f), Key(0.09f, -30f), Key(0.30f, 2f)), 0f, ArmSplayL * 1.6f);
            SetEuler(clip, ArmR, Curve(Key(0f, -70f), Key(0.09f, -30f), Key(0.30f, 2f)), 0f, ArmSplayR * 1.6f);
            SetEuler(clip, ForearmL, Curve(Key(0f, 20f), Key(0.30f, 6f)));
            SetEuler(clip, ForearmR, Curve(Key(0f, 20f), Key(0.30f, 6f)));
            SetEuler(clip, Torso, Curve(Key(0f, 4f), Key(0.09f, 14f), Key(0.30f, 1.5f)));

            return Save(clip);
        }

        // ------------------------------------------------------------------
        // controller
        // ------------------------------------------------------------------

        private static AnimatorController BuildController(
            AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip sprint,
            AnimationClip jump, AnimationClip fall, AnimationClip land)
        {
            string path = PrototypeAssets.ControllerPath;
            AssetDatabase.DeleteAsset(path); // rebuild from scratch, no stale states

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Land", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;

            // Ground locomotion is one state containing a blend tree, so Idle/Walk/
            // Run/Sprint cross-fade continuously off real speed rather than snapping
            // between four separate states.
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            tree.AddChild(run, RunSpeed);
            tree.AddChild(sprint, SprintSpeed);

            var jumpState = machine.AddState("Jump");
            jumpState.motion = jump;
            var fallState = machine.AddState("Fall");
            fallState.motion = fall;
            var landState = machine.AddState("Land");
            landState.motion = land;

            machine.defaultState = locomotion;

            // Order matters: the jump trigger is checked before the "left the
            // ground" test, so jumping enters Jump and not Fall.
            var toJump = locomotion.AddTransition(jumpState);
            toJump.hasExitTime = false;
            toJump.duration = 0.05f;
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            var toFall = locomotion.AddTransition(fallState);
            toFall.hasExitTime = false;
            toFall.duration = 0.12f;
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            // Apex reached: rising turned into falling.
            var jumpToFall = jumpState.AddTransition(fallState);
            jumpToFall.hasExitTime = false;
            jumpToFall.duration = 0.12f;
            jumpToFall.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalSpeed");

            // Very short hop that touches down before the apex.
            var jumpToLand = jumpState.AddTransition(landState);
            jumpToLand.hasExitTime = false;
            jumpToLand.duration = 0.05f;
            jumpToLand.AddCondition(AnimatorConditionMode.If, 0f, "Land");

            var fallToLand = fallState.AddTransition(landState);
            fallToLand.hasExitTime = false;
            fallToLand.duration = 0.06f;
            fallToLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            // Let the landing play out, then hand control back to locomotion.
            var landToLocomotion = landState.AddTransition(locomotion);
            landToLocomotion.hasExitTime = true;
            landToLocomotion.exitTime = 0.55f;
            landToLocomotion.duration = 0.14f;

            // ...unless we left the ground again mid-landing.
            var landToFall = landState.AddTransition(fallState);
            landToFall.hasExitTime = false;
            landToFall.duration = 0.08f;
            landToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Prototype] Animator controller written to {path}");
            return controller;
        }

        // ------------------------------------------------------------------
        // curve helpers
        // ------------------------------------------------------------------

        private static AnimationClip NewClip(string name, bool loop)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static AnimationClip Save(AnimationClip clip)
        {
            string path = $"{PrototypeAssets.AnimationFolder}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static Keyframe Key(float time, float value) => new Keyframe(time, value);

        private static AnimationCurve Curve(params Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            // Smooth every key so the poses ease instead of snapping between them.
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        /// <summary>
        /// Writes all three euler components. Unity wants the full set — animating
        /// only .x leaves the other two undefined and Unity logs a warning about
        /// missing curves.
        /// </summary>
        private static void SetEuler(AnimationClip clip, string path, AnimationCurve x,
                                     float constantY = 0f, float constantZ = 0f)
        {
            float end = x.length > 0 ? x[x.length - 1].time : 1f;

            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.x", x);
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.y",
                Curve(Key(0f, constantY), Key(end, constantY)));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z",
                Curve(Key(0f, constantZ), Key(end, constantZ)));
        }

        private static void SetPositionY(AnimationClip clip, string path, params Keyframe[] keys)
        {
            var y = Curve(keys);
            float end = keys[keys.Length - 1].time;

            clip.SetCurve(path, typeof(Transform), "localPosition.x", Curve(Key(0f, 0f), Key(end, 0f)));
            clip.SetCurve(path, typeof(Transform), "localPosition.y", y);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", Curve(Key(0f, 0f), Key(end, 0f)));
        }
    }
}
