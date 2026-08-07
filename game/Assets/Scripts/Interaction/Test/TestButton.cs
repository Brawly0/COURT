using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Interaction.Test
{
    /// <summary>
    /// TEST OBJECT — instant interaction with a server-owned counter.
    ///
    /// The simplest possible path through the system: press once, the server changes
    /// state, everyone sees it. The count lives in a NetworkVariable rather than
    /// riding in the response, so a late joiner sees the current value without
    /// anyone having to re-send it.
    /// </summary>
    public class TestButton : NetworkInteractable
    {
        private readonly NetworkVariable<int> _presses = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _lastPresser = new(
            NoOwner, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Presses => _presses.Value;
        public ulong LastPresser => _lastPresser.Value;

        public override string PromptFor(ulong clientId) =>
            _presses.Value == 0 ? "Press Button" : $"Press Button  ({_presses.Value})";

        public override void ServerExecute(ulong clientId)
        {
            _presses.Value++;
            _lastPresser.Value = clientId;
            Debug.Log($"[Interact] Button pressed by client {clientId} (total {_presses.Value}).");
        }

        private void OnGUI()
        {
            if (_lastPresser.Value == NoOwner) return;
            WorldLabel.Draw(transform.position + Vector3.up * 1.4f,
                $"pressed {_presses.Value}x\nlast: player {_lastPresser.Value}");
        }
    }
}
