using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Interaction.Test
{
    /// <summary>
    /// TEST OBJECT — restricted to one team or role.
    ///
    /// Enforcement is entirely server-side. The prompt still appears for everyone on
    /// purpose: hiding it would tell a player something about what they are not, and
    /// seat membership is public anyway. Pressing it simply refuses.
    /// </summary>
    public class TestRestrictedObject : NetworkInteractable
    {
        private readonly NetworkVariable<int> _uses = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _lastUser = new(
            NoOwner, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Uses => _uses.Value;

        public override void ServerExecute(ulong clientId)
        {
            _uses.Value++;
            _lastUser.Value = clientId;
            Debug.Log($"[Interact] Restricted object used by client {clientId} " +
                      $"(requires team={RequiredTeam}, role={RequiredRole}).");
        }

        private void OnGUI() =>
            WorldLabel.Draw(transform.position + Vector3.up * 1.4f,
                $"{RequiredTeam} only\nused {_uses.Value}x");
    }
}
