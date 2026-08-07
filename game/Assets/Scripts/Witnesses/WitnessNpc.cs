using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Interaction;

namespace CaseClosed.Game.Witnesses
{
    /// <summary>
    /// The person standing in the lounge. A pooled scene object, assigned to a
    /// generated character when a case starts.
    ///
    /// WHAT IT REPLICATES: a display name and whether it is in use. That is the
    /// complete list. A client reading every field learns "this is Nadia and she is
    /// here", which it can already see by looking at her. Everything that could be
    /// reasoned back toward the case — her observations, her true movements, whether
    /// she is protecting anyone — lives in WitnessRuntime on the host.
    ///
    /// Exclusivity comes free from NetworkInteractable's lock: one witness talks to
    /// one player at a time, and LockOwner is already replicated so every client can
    /// see she is busy without asking the server.
    ///
    /// Pooled rather than spawned, for the same reason the evidence bodies are: it
    /// needs no NGO prefab registration, and the cast size is small and known.
    /// </summary>
    public class WitnessNpc : NetworkInteractable
    {
        [Tooltip("Seconds to take a first statement.")]
        public float InterviewSeconds = 3f;

        [Tooltip("Seconds to re-read a statement you already took. Near zero.")]
        public float ReviewSeconds = 0.15f;

        private readonly NetworkVariable<FixedString64Bytes> _displayName = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _assigned = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Renderer[] _renderers;

        public string DisplayName => _displayName.Value.ToString();
        public bool Assigned => _assigned.Value;

        /// <summary>An unassigned pool entry is invisible and cannot be talked to.</summary>
        public override bool IsAvailable => _assigned.Value;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            HoldDuration = InterviewSeconds;   // IsHold must be true for the pool entry
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _assigned.OnValueChanged += (_, __) => ApplyVisibility();
            ApplyVisibility();
        }

        /// <summary>Server-side assignment when a case begins. Pool entries are reused.</summary>
        public void ServerAssign(string displayName)
        {
            if (!IsServer) return;
            _displayName.Value = displayName;
            _assigned.Value = true;
        }

        public void ServerRelease()
        {
            if (!IsServer) return;
            _displayName.Value = default;
            _assigned.Value = false;
            ServerForceRelease();
        }

        /// <summary>
        /// Reads the LOCAL knowledge ledger, which this client legitimately owns —
        /// it holds the statements it was sent. So the prompt can promise a cheap
        /// review before the server is asked, and the server independently agrees
        /// because it keeps the authoritative set.
        /// </summary>
        public override string PromptFor(ulong clientId)
        {
            string name = DisplayName;
            if (string.IsNullOrEmpty(name)) return "Nobody Here";

            if (IsLockedByOther(clientId)) return $"{name} is speaking with someone";

            return WitnessDirector.LocallyKnows(name)
                ? $"Review {name}'s Statement"
                : $"Interview {name}";
        }

        /// <summary>Cheap for a player who already took this statement.</summary>
        public override float HoldDurationFor(ulong clientId)
        {
            var director = WitnessDirector.Instance;
            if (director != null && director.ServerKnows(DisplayName, clientId)) return ReviewSeconds;
            return InterviewSeconds;
        }

        public override InteractionOutcome ServerValidate(ulong clientId)
        {
            var director = WitnessDirector.Instance;
            if (director == null) return InteractionOutcome.RejectedUnavailable;
            if (!_assigned.Value) return InteractionOutcome.RejectedUnavailable;

            // A fabricated id cannot reach here: the interaction layer resolved this
            // object through NGO's spawn table, and the witness name comes from our
            // own replicated field, never from the client.
            return director.ServerCanInterview(DisplayName, clientId);
        }

        public override void ServerExecute(ulong clientId) =>
            WitnessDirector.Instance?.ServerCompleteInterview(DisplayName, clientId);

        /// <summary>
        /// Abandoned. Nothing to undo — knowledge is only ever granted in
        /// ServerExecute, so an interrupted interview grants nothing and the lock
        /// releases, freeing the witness for someone else.
        /// </summary>
        public override void ServerCancel(ulong clientId) { }

        private void ApplyVisibility()
        {
            if (_renderers == null) return;
            foreach (var r in _renderers) if (r != null) r.enabled = _assigned.Value;
        }
    }
}
