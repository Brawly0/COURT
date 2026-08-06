using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Prototype.Net
{
    /// <summary>
    /// WHY THIS EXISTS: the owner gate. Every machine in the session holds a copy
    /// of every player, but only ONE of those copies is really "you". This decides
    /// which is which, exactly once, when the object spawns.
    ///
    /// Your copy      -> input + movement + camera run normally.
    /// Everyone else's -> a puppet. No input, no movement, no camera. Its transform
    ///                    is written by the network, and its animation is driven by
    ///                    PlayerNetworkSync.
    ///
    /// Nothing else in the prototype knows the game is networked. PlayerMovement,
    /// PlayerInputReader and PlayerCameraRig are untouched by this milestone — the
    /// gate just switches them off on copies that are not ours.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PrototypeNetPlayer : NetworkBehaviour
    {
        [Header("Spawn")]
        [Tooltip("Where player 0 appears. Later players are spaced out along X from here.")]
        public Vector3 SpawnOrigin = new Vector3(0f, 0.3f, -2f);

        [Tooltip("Gap between player spawn slots, so nobody spawns inside anybody.")]
        public float SpawnSpacing = 1.8f;

        [Header("Respawn")]
        [Tooltip("Fall below this height and you are put back at your spawn slot.")]
        public float RespawnBelowY = -10f;

        [Tooltip("Manual respawn key, for when you get stuck on the test course.")]
        public UnityEngine.InputSystem.Key RespawnKey = UnityEngine.InputSystem.Key.R;

        private PlayerInputReader _input;
        private PlayerMovement _movement;
        private PlayerAnimatorDriver _animatorDriver;
        private CharacterController _controller;

        public override void OnNetworkSpawn()
        {
            _input = GetComponent<PlayerInputReader>();
            _movement = GetComponent<PlayerMovement>();
            _animatorDriver = GetComponent<PlayerAnimatorDriver>();
            _controller = GetComponent<CharacterController>();

            gameObject.name = IsOwner
                ? $"Player {OwnerClientId} (you)"
                : $"Player {OwnerClientId} (remote)";

            if (IsOwner) SetUpLocalPlayer();
            else SetUpRemotePuppet();

            Debug.Log($"[Net] spawned client {OwnerClientId} — owner={IsOwner}");
        }

        private void SetUpLocalPlayer()
        {
            Teleport(SpawnSlot(OwnerClientId));
            AttachCameraAndHud();
        }

        /// <summary>
        /// Strip everything that would fight the network for control of this
        /// transform, or that would let one keyboard drive two characters.
        /// </summary>
        private void SetUpRemotePuppet()
        {
            if (_input != null) _input.enabled = false;
            if (_movement != null) _movement.enabled = false;

            // The local driver reads PlayerMovement, which we just switched off.
            // PlayerNetworkSync feeds this character's Animator instead.
            if (_animatorDriver != null) _animatorDriver.enabled = false;

            // CRITICAL — learned the hard way in NetPlayer.cs: an enabled
            // CharacterController overwrites transform writes coming from
            // NetworkTransform, which freezes remote players where they spawned.
            // A remote copy is a pure puppet and must not have one running.
            if (_controller != null) _controller.enabled = false;
        }

        /// <summary>
        /// The camera and debug HUD live in the scene, not on the prefab — there is
        /// only ever one of each, and they must follow whichever character is ours.
        /// </summary>
        private void AttachCameraAndHud()
        {
            var rig = FindAnyObjectByType<PlayerCameraRig>();
            if (rig != null)
            {
                rig.Target = transform;
                rig.Input = _input;
                if (_movement != null) _movement.CameraTransform = rig.transform;
            }
            else
            {
                Debug.LogWarning("[Net] no PlayerCameraRig in the scene - local player has no camera.");
            }

            var hud = FindAnyObjectByType<PlayerDebugHud>();
            if (hud != null)
            {
                hud.Movement = _movement;
                hud.AnimatorDriver = _animatorDriver;
            }
        }

        private void Update()
        {
            // Owner-authoritative: only the machine that owns this character is
            // allowed to move it, respawn included.
            if (!IsOwner) return;

            if (transform.position.y < RespawnBelowY) Respawn();

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard[RespawnKey].wasPressedThisFrame) Respawn();
        }

        private void Respawn()
        {
            Teleport(SpawnSlot(OwnerClientId));
            Debug.Log($"[Net] client {OwnerClientId} respawned");
        }

        /// <summary>
        /// Deterministic spawn slot per client id, so two players never overlap.
        ///
        /// Prefers named markers in the scene (PlayerSpawn_0, _1, ...) so spawn
        /// placement is a level-design decision rather than a hardcoded formula —
        /// which matters for voice testing, where the distance between spawns is the
        /// thing under test. Falls back to the spaced-out formula if none exist.
        /// </summary>
        private Vector3 SpawnSlot(ulong clientId)
        {
            var marker = GameObject.Find($"PlayerSpawn_{clientId}");
            if (marker != null) return marker.transform.position;

            // Alternate left/right of the origin: 0, +1, -1, +2, -2 ...
            int index = (int)clientId;
            int step = (index + 1) / 2;
            float side = (index % 2 == 0) ? 1f : -1f;
            return SpawnOrigin + new Vector3(step * SpawnSpacing * side, 0f, 0f);
        }

        /// <summary>
        /// CharacterController refuses direct transform writes while enabled, so it
        /// has to be switched off around the move. This is the standard NGO/CC
        /// teleport dance and the reason respawning looks like more code than it is.
        /// </summary>
        private void Teleport(Vector3 position)
        {
            bool hadController = _controller != null && _controller.enabled;
            if (hadController) _controller.enabled = false;

            transform.SetPositionAndRotation(position, Quaternion.identity);

            if (hadController) _controller.enabled = true;
        }
    }
}
