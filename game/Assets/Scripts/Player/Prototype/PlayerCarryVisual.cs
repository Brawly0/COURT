using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Archive;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// Puts a player into the carry pose when they are holding something.
    ///
    /// WHY NOTHING IS NETWORKED HERE. The obvious implementation adds a
    /// "Carrying" NetworkVariable written by the owner. That would be a second
    /// copy of a fact the server already owns, free to drift out of step with the
    /// folder itself, and owner-written to boot — a client could pose as carrying
    /// while holding nothing.
    ///
    /// Instead this is DERIVED. Every client already replicates each folder's
    /// custody and carrier id, so "is this player holding something" is a local
    /// question with a free answer. Zero extra bandwidth, server-authoritative by
    /// construction, and it cannot disagree with the object it describes. Same
    /// reasoning that fixed the IsSpeaking leak in the voice layer.
    ///
    /// Runs on EVERY machine for EVERY player copy, which is exactly why remote
    /// players are seen carrying without a single byte being sent for the pose.
    /// </summary>
    [RequireComponent(typeof(PlayerCarrySocket))]
    public class PlayerCarryVisual : MonoBehaviour
    {
        [Tooltip("Leave empty and it finds the Animator on this object or a child.")]
        public Animator Animator;

        [Tooltip("Name of the upper-body layer holding the carry pose.")]
        public string CarryLayerName = "Carry";

        [Tooltip("Seconds for the carry pose to blend in and out. Snapping reads as a glitch.")]
        public float BlendSeconds = 0.18f;

        private static readonly int CarryingHash = UnityEngine.Animator.StringToHash("Carrying");

        private NetworkObject _networkObject;
        private int _layerIndex = -1;
        private float _weight;

        /// <summary>Whether this body is holding something, as of this frame. Debug readout.</summary>
        public bool IsCarrying { get; private set; }

        /// <summary>The item this body holds, or null. Debug readout.</summary>
        public PhysicalEvidence Carried { get; private set; }

        private void Awake()
        {
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
            _networkObject = GetComponent<NetworkObject>();

            if (Animator != null)
            {
                _layerIndex = Animator.GetLayerIndex(CarryLayerName);
                if (_layerIndex < 0)
                    Debug.LogWarning($"[Carry] No '{CarryLayerName}' layer on the Animator — " +
                                     "the folder will attach but the arms will not come up.");
            }
        }

        /// <summary>
        /// LateUpdate so the layer weight is set after the locomotion state machine
        /// has already been driven for the frame.
        /// </summary>
        private void LateUpdate()
        {
            Carried = ResolveCarried();
            IsCarrying = Carried != null;

            if (Animator == null) return;

            Animator.SetBool(CarryingHash, IsCarrying);

            if (_layerIndex < 0) return;

            // Ease rather than jump. A hard 0->1 on an override layer pops the arms.
            float target = IsCarrying ? 1f : 0f;
            _weight = BlendSeconds <= 0f
                ? target
                : Mathf.MoveTowards(_weight, target, Time.deltaTime / BlendSeconds);

            Animator.SetLayerWeight(_layerIndex, _weight);
        }

        /// <summary>
        /// Scan the evidence bodies for one whose carrier is this player. The pool is
        /// a handful of objects, so a scan is cheaper than the bookkeeping needed to
        /// avoid it — and it has no state of its own to go stale.
        /// </summary>
        private PhysicalEvidence ResolveCarried()
        {
            if (_networkObject == null || !_networkObject.IsSpawned) return null;

            return PhysicalEvidence.FindCarriedBy(_networkObject.OwnerClientId);
        }
    }
}
