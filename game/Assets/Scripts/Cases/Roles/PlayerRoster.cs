using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Cases.Roles
{
    /// <summary>One seat at the table. Public information.</summary>
    public struct RosterEntry : INetworkSerializable, System.IEquatable<RosterEntry>
    {
        public ulong ClientId;
        public PlayerRole Role;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            byte role = (byte)Role;
            serializer.SerializeValue(ref role);
            Role = (PlayerRole)role;
        }

        // NetworkList requires equality to compute its change deltas.
        public bool Equals(RosterEntry other) => ClientId == other.ClientId && Role == other.Role;
    }

    /// <summary>
    /// WHY THIS EXISTS: who is prosecuting, who is defending and who is on trial is
    /// PUBLIC. In a courtroom everybody can see the seats. Hiding it would buy no
    /// secrecy and would make it impossible to draw a name tag or open the right
    /// office door.
    ///
    /// The secret is GUILT, not seat — and guilt never travels through here. It goes
    /// only in PlayerCaseView, only to the Defendant. Keeping the two on separate
    /// channels is what stops a future feature from widening the roster and leaking
    /// the mystery by accident.
    ///
    /// Server writes, everyone reads. Lives on the same NetworkObject as
    /// CaseNetworkController so there is one spawned case object, not two.
    /// </summary>
    public class PlayerRoster : NetworkBehaviour
    {
        public static PlayerRoster Instance { get; private set; }

        /// <summary>Replicated seats. Server-authoritative; clients cannot write.</summary>
        private readonly NetworkList<RosterEntry> _seats = new(
            new List<RosterEntry>(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Raised on every machine whenever the table changes.</summary>
        public event System.Action RosterChanged;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _seats.OnListChanged += OnSeatsChanged;
        }

        public override void OnNetworkDespawn()
        {
            _seats.OnListChanged -= OnSeatsChanged;
            if (Instance == this) Instance = null;
        }

        private void OnSeatsChanged(NetworkListEvent<RosterEntry> _) => RosterChanged?.Invoke();

        // ------------------------------------------------------------------
        // reading (every machine)
        // ------------------------------------------------------------------

        public int Count => _seats.Count;

        public PlayerRole RoleOf(ulong clientId)
        {
            foreach (var seat in _seats)
                if (seat.ClientId == clientId) return seat.Role;
            return PlayerRole.Unassigned;
        }

        /// <summary>This machine's own seat.</summary>
        public PlayerRole LocalRole =>
            NetworkManager.Singleton != null ? RoleOf(NetworkManager.Singleton.LocalClientId) : PlayerRole.Unassigned;

        /// <summary>True when nobody is on trial, because the Defendant disconnected.</summary>
        public bool DefendantMissing
        {
            get
            {
                foreach (var seat in _seats)
                    if (seat.Role == PlayerRole.Defendant) return false;
                return true;
            }
        }

        public IReadOnlyDictionary<ulong, PlayerRole> Snapshot()
        {
            var table = new Dictionary<ulong, PlayerRole>();
            foreach (var seat in _seats) table[seat.ClientId] = seat.Role;
            return table;
        }

        public string Describe() => RoleAssignment.Describe(Snapshot());

        // ------------------------------------------------------------------
        // writing (server only)
        // ------------------------------------------------------------------

        /// <summary>Deals the whole table. Called when a case is generated.</summary>
        public void ServerDeal(ulong caseSeed, IEnumerable<ulong> clientIds)
        {
            if (!IsServer) return;

            var table = RoleAssignment.Deal(caseSeed, clientIds);
            _seats.Clear();
            foreach (var pair in table)
                _seats.Add(new RosterEntry { ClientId = pair.Key, Role = pair.Value });

            Debug.Log($"[Roles] Dealt from seed {caseSeed} — {RoleAssignment.Describe(table)}");
        }

        /// <summary>
        /// Seats one late joiner. Returns Unassigned if there is no case to join,
        /// which is the correct answer rather than a silent default.
        /// </summary>
        public PlayerRole ServerSeatLateJoiner(ulong clientId)
        {
            if (!IsServer || _seats.Count == 0) return PlayerRole.Unassigned;

            var existing = new List<PlayerRole>();
            foreach (var seat in _seats)
            {
                if (seat.ClientId == clientId) return seat.Role;   // already seated
                existing.Add(seat.Role);
            }

            var role = RoleAssignment.AssignLateJoiner(existing);
            _seats.Add(new RosterEntry { ClientId = clientId, Role = role });

            Debug.Log($"[Roles] Client {clientId} joined late as {role}. Now: {Describe()}");
            return role;
        }

        /// <summary>
        /// Removes a departed player. If they were the Defendant the seat is left
        /// EMPTY on purpose — see RoleAssignment.VacantDefendant.
        /// </summary>
        public void ServerRemove(ulong clientId)
        {
            if (!IsServer) return;

            for (int i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].ClientId != clientId) continue;

                var role = _seats[i].Role;
                _seats.RemoveAt(i);

                if (role == PlayerRole.Defendant)
                    Debug.LogWarning("[Roles] THE DEFENDANT LEFT. Seat left vacant — " +
                                     "the trial system decides between in-absentia and a redeal. " +
                                     "Promoting someone would hand them the answer.");
                else
                    Debug.Log($"[Roles] Client {clientId} ({role}) left. Now: {Describe()}");
                return;
            }
        }

        public void ServerClear()
        {
            if (!IsServer) return;
            _seats.Clear();
        }
    }
}
