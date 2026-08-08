using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Interaction;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Witnesses
{
    /// <summary>
    /// Owns who the witnesses are and who has spoken to them.
    ///
    /// THE SECRECY BOUNDARY runs through this class. It holds a reference to the
    /// generated CaseFile — the whole truth — and the only thing that ever leaves is
    /// a WitnessTestimony built by TestimonyWriter and sent to one client. There is
    /// no method here that broadcasts a statement.
    ///
    /// No NetworkObject on this component, deliberately, matching ActiveCaseManager:
    /// it cannot replicate even by accident. The RPCs live on a separate networked
    /// controller-free path by borrowing the pooled NPCs, which ARE networked.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class WitnessDirector : NetworkBehaviour
    {
        public static WitnessDirector Instance { get; private set; }

        [Tooltip("How many witnesses to seat. The cast has 6, index 0 is the defendant.")]
        public int MaxWitnesses = 5;

        /// <summary>Server-side, keyed by character name. Host memory only.</summary>
        private readonly Dictionary<string, WitnessRuntime> _witnesses = new();

        /// <summary>The generated case. Server only, and never leaves this class.</summary>
        private CaseFile _file;

        /// <summary>Statements THIS client has legitimately received. Local only.</summary>
        private static readonly Dictionary<string, WitnessTestimony> LocalStatements = new();

        /// <summary>Raised locally when a statement arrives or is re-opened.</summary>
        public event System.Action<WitnessTestimony> StatementReceived;

        /// <summary>Raised locally when the server refuses.</summary>
        public event System.Action<string> InterviewRefused;

        public static bool LocallyKnows(string witnessName) =>
            !string.IsNullOrEmpty(witnessName) && LocalStatements.ContainsKey(witnessName);

        public static IReadOnlyDictionary<string, WitnessTestimony> KnownStatements => LocalStatements;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            if (Instance == this) Instance = null;
            LocalStatements.Clear();
        }

        // ------------------------------------------------------------------
        // seating the cast
        // ------------------------------------------------------------------

        /// <summary>
        /// SERVER ONLY. Seats the generated cast in the lounge.
        ///
        /// Identities come from CaseFile.CastNames, not from an invented list, so
        /// the same seed always produces the same people in the same order. Index 0
        /// is the defendant and is skipped — they are a player, not a witness.
        /// </summary>
        public void ServerSeatWitnesses(CompleteCaseTruth truth)
        {
            if (!IsServer || truth == null) return;

            _file = truth.File;
            _witnesses.Clear();

            var pool = FindPool();
            foreach (var npc in pool) npc.ServerRelease();

            int seated = 0;
            for (int castIndex = 1; castIndex < _file.CastNames.Count && seated < MaxWitnesses; castIndex++)
            {
                string name = _file.CastNames[castIndex];
                if (string.IsNullOrEmpty(name)) continue;
                if (seated >= pool.Count)
                {
                    Debug.LogWarning("[Witness] Not enough NPCs in the lounge pool.");
                    break;
                }

                _witnesses[name] = new WitnessRuntime
                {
                    CharacterId = name,
                    CastIndex = castIndex,
                    NpcIndex = seated,
                };

                pool[seated].ServerAssign(name);
                seated++;
            }

            Debug.Log($"[Witness] Seated {seated} witnesses: {string.Join(", ", _witnesses.Keys)}");
        }

        private static List<WitnessNpc> FindPool() =>
            Object.FindObjectsByType<WitnessNpc>(FindObjectsInactive.Exclude)
                  .OrderBy(n => n.name).ToList();

        // ------------------------------------------------------------------
        // interviewing
        // ------------------------------------------------------------------

        /// <summary>Server-side pre-check, re-run every frame of the hold.</summary>
        public InteractionOutcome ServerCanInterview(string witnessName, ulong clientId)
        {
            if (!IsServer) return InteractionOutcome.RejectedUnavailable;
            if (_file == null) return InteractionOutcome.RejectedUnavailable;
            if (string.IsNullOrEmpty(witnessName)) return InteractionOutcome.RejectedUnavailable;
            if (!_witnesses.ContainsKey(witnessName)) return InteractionOutcome.RejectedUnknownTarget;

            return InteractionOutcome.Accepted;
        }

        public bool ServerKnows(string witnessName, ulong clientId) =>
            IsServer && !string.IsNullOrEmpty(witnessName) &&
            _witnesses.TryGetValue(witnessName, out var w) && w.IsKnownBy(clientId);

        /// <summary>
        /// SERVER ONLY. The hold completed with every check still passing.
        ///
        /// The statement is rendered once and cached, so a second player hears the
        /// same words — the statement is a fact about the witness, not about who is
        /// asking. Re-interviewing an already-known witness re-sends the cache and
        /// adds nothing, so knowledge records never duplicate.
        /// </summary>
        public void ServerCompleteInterview(string witnessName, ulong clientId)
        {
            if (!IsServer) return;
            if (_file == null || !_witnesses.TryGetValue(witnessName, out var witness)) return;

            witness.CachedStatement ??= TestimonyWriter.Build(_file, witness.CharacterId);

            bool firstTime = witness.GrantKnowledge(clientId);

            // Sent to ONE client. There is no broadcast overload of this call.
            ReceiveStatementClientRpc(witness.CachedStatement.Value, firstTime, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

            Debug.Log($"[Witness] {witnessName} gave a statement to client {clientId}" +
                      (firstTime ? " (first time)." : " (review, no new record).") +
                      $" Known by {witness.InterviewedBy.Count} player(s).");
        }

        /// <summary>Frees any witness this player was mid-interview with.</summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            foreach (var npc in FindPool())
                if (npc.LockedBy == clientId) npc.ServerForceRelease();
        }

        [ClientRpc]
        private void ReceiveStatementClientRpc(WitnessTestimony testimony, bool firstTime,
                                               ClientRpcParams p = default)
        {
            LocalStatements[testimony.WitnessId.ToString()] = testimony;
            StatementReceived?.Invoke(testimony);
        }

        // ------------------------------------------------------------------
        // developer inspection — HOST ONLY
        // ------------------------------------------------------------------

        /// <summary>
        /// Everything about a witness, formatted. HOST ONLY and never sent anywhere:
        /// it reads the ledger, the true occupancy and the agenda, all of which are
        /// exactly what players must not have.
        ///
        /// Returns empty on a client, so a modified build cannot call its way in —
        /// the data is not present on a client to begin with.
        /// </summary>
        public string DeveloperDump()
        {
            if (!IsServer || _file == null) return "";

            var sb = new System.Text.StringBuilder("WITNESS DEV DUMP (host only)\n");
            foreach (var w in _witnesses.Values)
            {
                sb.Append('\n').Append(w.CharacterId).Append("  cast#").Append(w.CastIndex).Append('\n');

                sb.Append("  actual timeline: ");
                for (int t = 0; t < World.Slots.Length; t++)
                    sb.Append(World.Slots[t]).Append('=')
                      .Append(World.Locations[_file.Occupancy[w.CastIndex][t]]).Append("  ");
                sb.Append('\n');

                if (_file.Obs.TryGetValue(w.CharacterId, out var obs))
                    foreach (var o in obs)
                        sb.Append("  obs: ").Append(o.Verb).Append(' ').Append(o.Subject)
                          .Append(" @ ").Append(World.Locations[o.Location])
                          .Append(' ').Append(World.Slots[o.Slot])
                          .Append(o.Corrupted ? "   <-- CORRUPTED" : "").Append('\n');

                foreach (var entry in _file.Ledger.Where(e => e.Witness == w.CharacterId))
                    sb.Append("  ledger: ").Append(entry.Kind).Append(" — ").Append(entry.TruthNote).Append('\n');

                if (_file.Protector == w.CharacterId) sb.Append("  AGENDA: protecting the perpetrator\n");
                if (_file.Perpetrator == w.CharacterId && _file.PerpClaimedLocation >= 0)
                    sb.Append("  AGENDA: self-preservation, claims ")
                      .Append(World.Locations[_file.PerpClaimedLocation]).Append('\n');

                sb.Append("  known by: ").Append(w.InterviewedBy.Count).Append(" player(s)\n");
            }
            return sb.ToString();
        }

        /// <summary>Server-side view for the audit. Host only.</summary>
        public IReadOnlyDictionary<string, WitnessRuntime> ServerWitnesses => _witnesses;
    }
}
