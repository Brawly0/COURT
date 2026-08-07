using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Interaction;

namespace CaseClosed.Game.Archive
{
    /// <summary>What a container looks like from the outside. Safe to replicate.</summary>
    public enum ContainerState : byte
    {
        Unsearched = 0,
        SearchedEmpty = 1,
        SearchedFound = 2,
    }

    /// <summary>
    /// A searchable Archive location: filing cabinet, shelf section, drawer, box.
    ///
    /// REUSES NetworkInteractable AND NOTHING ELSE. Hold timing, the exclusivity
    /// lock, distance, line of sight, cancellation on walk-away or disconnect all
    /// come from the interaction layer already built and tested. This class adds one
    /// thing: what happens on completion.
    ///
    /// THE SECRECY BOUNDARY IS THE POINT. This component knows nothing about its own
    /// contents — no field holds an EvidenceId, so there is nothing for a client to
    /// read, and no chance a future refactor replicates it by accident. It asks
    /// ArchiveDirector, which is host-only, at the moment of completion.
    ///
    /// What DOES replicate is ContainerState, because "already searched" must be
    /// visible to everyone or two players would grind the same empty drawer.
    /// </summary>
    public class ArchiveContainer : NetworkInteractable
    {
        [Header("Archive")]
        [Tooltip("Stable index used by placement. Assigned by the scene builder; " +
                 "must be unique and must not change between runs, or a saved layout " +
                 "would point at a different drawer.")]
        public int ContainerIndex;

        [Tooltip("Shown on the prompt: 'Filing Cabinet', 'Records Box'...")]
        public string ContainerKind = "Filing Cabinet";

        [Tooltip("Which way is OUT of this container, into the room. Discovered evidence " +
                 "is placed along it.\n\n" +
                 "Set explicitly rather than derived from transform.forward: the containers " +
                 "are built unrotated, so every forward is world +Z, and the north row would " +
                 "spawn its evidence inside the north wall.")]
        public Vector3 RevealDirection = Vector3.forward;

        /// <summary>Where a discovered item should appear: clear of the furniture, in the room.</summary>
        public Vector3 RevealPoint =>
            transform.position + RevealDirection.normalized * 1.1f + Vector3.up * 0.2f;

        private readonly NetworkVariable<ContainerState> _state = new(
            ContainerState.Unsearched,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public ContainerState State => _state.Value;
        public bool AlreadySearched => _state.Value != ContainerState.Unsearched;

        /// <summary>
        /// A searched container stops offering itself. This is what prevents a second
        /// payout: with IsAvailable false the interaction layer refuses the request
        /// before any Archive code runs.
        /// </summary>
        public override bool IsAvailable => !AlreadySearched;

        public override string PromptFor(ulong clientId) =>
            AlreadySearched ? "Already searched" : $"Search {ContainerKind}";

        /// <summary>
        /// Belt and braces behind IsAvailable. Cheap, and the thing it guards against
        /// — a duplicated piece of evidence — is expensive.
        /// </summary>
        public override InteractionOutcome ServerValidate(ulong clientId) =>
            AlreadySearched ? InteractionOutcome.RejectedUnavailable : InteractionOutcome.Accepted;

        /// <summary>
        /// Completion. Runs on the server only — the interaction layer guarantees
        /// that, and the hold clock it ran is the server's own.
        /// </summary>
        public override void ServerExecute(ulong clientId)
        {
            if (AlreadySearched) return;

            var director = ArchiveDirector.Instance;
            if (director == null)
            {
                _state.Value = ContainerState.SearchedEmpty;
                Debug.LogWarning("[Archive] No ArchiveDirector - container searched with nothing in it.");
                return;
            }

            bool foundSomething = director.ServerResolveSearch(this, clientId);
            _state.Value = foundSomething ? ContainerState.SearchedFound : ContainerState.SearchedEmpty;
        }

        public override void ServerCancel(ulong clientId) =>
            Debug.Log($"[Archive] Container {ContainerIndex} search abandoned by client {clientId}.");

        /// <summary>Host-only reset, so placement can be reshuffled while testing.</summary>
        public void ServerResetState()
        {
            if (IsServer) _state.Value = ContainerState.Unsearched;
        }
    }
}
