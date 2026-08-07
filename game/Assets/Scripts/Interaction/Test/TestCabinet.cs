using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Interaction.Test
{
    /// <summary>
    /// TEST OBJECT — hold interaction, exclusive, single-use.
    ///
    /// This is the one that matters. It is the exact shape every Archive shelf and
    /// evidence object will take: two players cannot search it at once, the lock
    /// releases on completion, cancellation or disconnect, and it yields its result
    /// exactly once — so a duplicated or replayed request cannot produce a
    /// duplicated reward.
    /// </summary>
    public class TestCabinet : NetworkInteractable
    {
        private readonly NetworkVariable<bool> _searched = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _searchedBy = new(
            NoOwner, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool WasSearched => _searched.Value;
        public ulong SearchedBy => _searchedBy.Value;

        /// <summary>Once emptied it stops offering itself — no prompt, no requests.</summary>
        public override bool IsAvailable => !_searched.Value;

        public override string PromptFor(ulong clientId) =>
            _searched.Value ? "Already searched" : "Search Cabinet";

        /// <summary>
        /// Second line of defence against duplicate completion. The exclusivity lock
        /// should make this unreachable — but "should" is not worth a duplicated
        /// piece of evidence once this pattern is carrying the real game.
        /// </summary>
        public override InteractionOutcome ServerValidate(ulong clientId) =>
            _searched.Value ? InteractionOutcome.RejectedUnavailable : InteractionOutcome.Accepted;

        public override void ServerExecute(ulong clientId)
        {
            if (_searched.Value) return;   // idempotent whatever happens upstream

            _searched.Value = true;
            _searchedBy.Value = clientId;
            Debug.Log($"[Interact] Cabinet searched by client {clientId}.");
        }

        public override void ServerCancel(ulong clientId) =>
            Debug.Log($"[Interact] Cabinet search abandoned by client {clientId}.");

        private void OnGUI()
        {
            string text = _searched.Value
                ? $"SEARCHED\nby player {_searchedBy.Value}"
                : IsLocked ? "being searched..." : "unsearched";
            WorldLabel.Draw(transform.position + Vector3.up * 1.6f, text);
        }
    }
}
