using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Interaction
{
    /// <summary>
    /// Finds what the local player is looking at, and asks the server about it.
    ///
    /// PURELY LOCAL AND PURELY ADVISORY. It decides what to show a prompt for and
    /// which id to name in a request — nothing else. Every consequence is decided
    /// server-side, so a tampered detector can aim at anything and still achieve
    /// only what the server would have allowed anyway.
    ///
    /// Runs on the owner alone: remote copies of a player must not be raycasting,
    /// and a shared scene component would raycast once for everybody.
    /// </summary>
    public class PlayerInteractionDetector : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("How far the player can reach, measured from their EYES - not from the camera.")]
        public float Range = 3.5f;

        [Tooltip("Eye height above the player's feet. Must match the server's EyeHeight, " +
                 "or the client will offer prompts the server then refuses as too far.")]
        public float EyeHeight = 1.5f;

        [Tooltip("Radius of the probe. A little thickness makes small objects far less fiddly.")]
        public float ProbeRadius = 0.18f;

        [Tooltip("What the probe can hit.")]
        public LayerMask Mask = ~0;

        [Header("Input")]
        public Key InteractKey = Key.E;

        [Header("Debug")]
        [Tooltip("Draw the probe in the Scene view.")]
        public bool DrawDebugRay = false;

        /// <summary>What the local player is currently looking at, or null.</summary>
        public NetworkInteractable Target { get; private set; }

        /// <summary>True while the key is down on a hold-type target.</summary>
        public bool Holding { get; private set; }

        /// <summary>
        /// Raised the instant the key goes down on a valid target, before the server
        /// has answered. The UI flashes on this so a press feels acknowledged even
        /// over a slow link — the flash is not a claim that anything succeeded.
        /// </summary>
        public event System.Action Pressed;

        private Camera _camera;
        private Transform _self;

        private void Awake() => _camera = Camera.main;

        /// <summary>Called by the local player once it knows which body is ours.</summary>
        public void Bind(Transform localPlayer)
        {
            _self = localPlayer;
            _camera = Camera.main;
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            UpdateTarget();
            UpdateInput();
        }

        /// <summary>
        /// Sphere-cast from the PLAYER'S EYES, along the CAMERA'S facing.
        ///
        /// This split matters and is easy to get wrong. Casting from the camera —
        /// the obvious first instinct, and what this did originally — is fine in
        /// first person but nonsense in third: the camera sits ~4.5 m behind the
        /// character, so a 3.5 m ray expires a metre behind their back and can never
        /// reach anything they are standing in front of. Nothing was ever targetable.
        ///
        /// Origin from the body, direction from the camera: you reach for what you
        /// are looking at, from where you actually are. It also matches how the
        /// server measures distance, so the prompt never promises something the
        /// server will refuse.
        ///
        /// A sphere rather than a ray so a doorknob does not need pixel-perfect aim.
        /// </summary>
        private void UpdateTarget()
        {
            var previous = Target;
            Target = null;

            Vector3 origin = _self != null
                ? _self.position + Vector3.up * EyeHeight
                : _camera.transform.position;
            Vector3 direction = _camera.transform.forward;

            var hits = Physics.SphereCastAll(origin, ProbeRadius, direction, Range,
                                             Mask, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                // Never target ourselves — our own capsule is right in front of the camera.
                if (_self != null && hit.collider.transform.IsChildOf(_self)) continue;

                var interactable = hit.collider.GetComponentInParent<NetworkInteractable>();
                if (interactable == null || !interactable.IsSpawned) continue;
                if (!interactable.IsAvailable) continue;

                if (hit.distance < best) { best = hit.distance; Target = interactable; }
            }

            // Target changed or vanished mid-hold: abandon it rather than silently
            // continuing to hold something we are no longer looking at.
            if (Holding && Target != previous) ReleaseHold();

            if (DrawDebugRay)
                Debug.DrawRay(origin, direction * Range,
                    Target != null ? Color.green : Color.red);
        }

        private void UpdateInput()
        {
            var keyboard = Keyboard.current;
            var controller = InteractionNetworkController.Instance;
            if (keyboard == null || controller == null) return;
            if (!System.Enum.IsDefined(typeof(Key), InteractKey)) return;

            var key = keyboard[InteractKey];

            if (key.wasPressedThisFrame && Target != null)
            {
                controller.RequestBegin(Target.NetworkObjectId);
                Holding = Target.IsHold;
                Pressed?.Invoke();
            }

            // Releasing cancels a hold. Instant interactions have already resolved.
            if (Holding && key.wasReleasedThisFrame) ReleaseHold();
        }

        private void ReleaseHold()
        {
            Holding = false;
            InteractionNetworkController.Instance?.RequestCancel();
        }

        private void OnDisable()
        {
            if (Holding) ReleaseHold();
            Target = null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!DrawDebugRay || _camera == null) return;

            Vector3 origin = _self != null
                ? _self.position + Vector3.up * EyeHeight
                : _camera.transform.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + _camera.transform.forward * Range);
            Gizmos.DrawWireSphere(origin + _camera.transform.forward * Range, ProbeRadius);
        }
    }
}
