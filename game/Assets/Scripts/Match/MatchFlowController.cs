using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// Drives the match from lobby to the starting line, and owns readiness.
    ///
    /// WHY SEPARATE FROM CaseNetworkController: that one answers "does a case exist
    /// and who has been told what" — a data question. This one answers "what are the
    /// humans doing" — a session question. Merging them would mean regenerating a
    /// case rewinds the lobby, and every future phase (investigation, bell, trial)
    /// would pile onto a class whose job is secrecy.
    ///
    /// READINESS IS SERVER-AUTHORITATIVE. A client asks; the server records. The RPC
    /// takes no client id — it reads the sender from NGO's own metadata, so a
    /// modified client cannot mark anyone else ready, only itself.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class MatchFlowController : NetworkBehaviour
    {
        public static MatchFlowController Instance { get; private set; }

        [Tooltip("How many players must be seated and ready before the match may advance.")]
        public int RequiredPlayers = 4;

        [Tooltip("Advance with fewer players. Useful when testing with 2 instances.")]
        public bool AllowPartialTable = true;

        private readonly NetworkVariable<MatchPhase> _phase = new(
            MatchPhase.LobbyReady,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Public counts only — never which player is ready, and never why.</summary>
        private readonly NetworkVariable<int> _readyCount = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _seatedCount = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Server-side truth about who has pressed Ready. Never replicated wholesale.</summary>
        private readonly HashSet<ulong> _ready = new();

        public MatchPhase Phase => _phase.Value;
        public int ReadyCount => _readyCount.Value;
        public int SeatedCount => _seatedCount.Value;
        public int RequiredCount => AllowPartialTable ? Mathf.Max(1, _seatedCount.Value) : RequiredPlayers;

        /// <summary>This client's own briefing. Empty until it arrives.</summary>
        public RoleBriefing LocalBriefing { get; private set; } = RoleBriefing.Empty;
        public bool HasBriefing { get; private set; }

        /// <summary>True once this client has asked to be marked ready (prevents repeat clicks).</summary>
        public bool LocalReadySent { get; private set; }

        /// <summary>
        /// SERVER ONLY: has this client pressed Ready? Used to gate interaction.
        /// Returns false off the server rather than guessing — a client asking about
        /// its own readiness should use LocalReadySent, and asking about anyone
        /// else's is not information it is owed.
        /// </summary>
        public bool ServerIsReady(ulong clientId) => IsServer && _ready.Contains(clientId);

        public event System.Action BriefingChanged;
        public event System.Action PhaseChanged;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _phase.OnValueChanged += (_, to) =>
            {
                Debug.Log($"[Match] phase -> {to}");
                PhaseChanged?.Invoke();
            };

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // host: driving the flow
        // ------------------------------------------------------------------

        /// <summary>
        /// The whole start sequence. Called by the host after a case is generated,
        /// or directly from the debug panel's START MATCH button.
        /// </summary>
        public void HostStartMatch(ulong seed)
        {
            if (!IsServer) { Debug.LogWarning("[Match] Only the host can start."); return; }

            var caseController = CaseNetworkController.Instance;
            if (caseController == null) { Debug.LogError("[Match] No CaseNetworkController."); return; }

            _ready.Clear();
            _readyCount.Value = 0;

            _phase.Value = MatchPhase.AssigningRoles;
            _phase.Value = MatchPhase.GeneratingCase;

            // Generates the case AND deals the roster; briefings follow from the seats.
            caseController.HostGenerateCase(seed);

            _phase.Value = MatchPhase.DistributingBriefings;
            DistributeBriefings();

            _seatedCount.Value = PlayerRoster.Instance != null ? PlayerRoster.Instance.Count : 0;
            _phase.Value = MatchPhase.WaitingForPlayers;
        }

        /// <summary>One targeted message per player. Nothing here is broadcast.</summary>
        private void DistributeBriefings()
        {
            var truth = ActiveCaseManager.Instance?.Truth;
            var roster = PlayerRoster.Instance;
            if (truth == null || roster == null) return;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                SendBriefingTo(clientId, roster.RoleOf(clientId));
        }

        private void SendBriefingTo(ulong clientId, PlayerRole role)
        {
            var truth = ActiveCaseManager.Instance?.Truth;
            if (truth == null) return;

            var briefing = BriefingFactory.Build(truth, role);

            ReceiveBriefingClientRpc(briefing, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        // ------------------------------------------------------------------
        // readiness
        // ------------------------------------------------------------------

        /// <summary>Called by the local player's Ready button.</summary>
        public void RequestReady()
        {
            if (LocalReadySent) return;      // client-side guard against double clicks
            LocalReadySent = true;
            SubmitReadyServerRpc();
        }

        /// <summary>
        /// Note there is no clientId parameter. NGO fills in the sender, so a client
        /// can only ever mark ITSELF ready — spoofing another player is not a matter
        /// of validation, it is unrepresentable.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void SubmitReadyServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            // Server-side idempotency: a replayed or duplicated message changes nothing.
            if (!_ready.Add(sender)) return;

            // Only seated players count. An unassigned spectator cannot hold up the match.
            var roster = PlayerRoster.Instance;
            if (roster != null && roster.RoleOf(sender) == PlayerRole.Unassigned)
            {
                _ready.Remove(sender);
                Debug.Log($"[Match] Client {sender} pressed Ready but holds no seat — ignored.");
                return;
            }

            _readyCount.Value = _ready.Count;
            Debug.Log($"[Match] Ready {_readyCount.Value}/{RequiredCount} (client {sender}).");

            TryAdvance();
        }

        private void TryAdvance()
        {
            if (!IsServer) return;
            if (_phase.Value != MatchPhase.WaitingForPlayers) return;
            if (_ready.Count < RequiredCount) return;

            _phase.Value = MatchPhase.PreInvestigationReady;
            Debug.Log("[Match] ALL PLAYERS READY — PreInvestigationReady. " +
                      "The investigation has NOT started.");
        }

        // ------------------------------------------------------------------
        // joins and leaves
        // ------------------------------------------------------------------

        /// <summary>
        /// LATE JOIN, documented: the joiner is seated by PlayerRoster (never as
        /// Defendant), receives a briefing immediately, and is added to the required
        /// count. If the match had already advanced it STAYS advanced — rewinding
        /// everyone else because one person arrived late would be worse.
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            if (ActiveCaseManager.Instance?.Truth == null) return;

            var role = PlayerRoster.Instance != null
                ? PlayerRoster.Instance.RoleOf(clientId)
                : PlayerRole.Unassigned;

            if (role == PlayerRole.Unassigned) return;   // roster seats them; we may be early

            SendBriefingTo(clientId, role);
            _seatedCount.Value = PlayerRoster.Instance.Count;
            Debug.Log($"[Match] Client {clientId} briefed as {role} on join.");
        }

        /// <summary>
        /// A leaver's readiness is withdrawn, so the count cannot be inflated by
        /// someone who is no longer here. If the remaining players are all ready,
        /// their departure can itself complete the phase — which is correct.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            if (_ready.Remove(clientId))
                Debug.Log($"[Match] Client {clientId} left — readiness withdrawn.");

            _readyCount.Value = _ready.Count;
            _seatedCount.Value = PlayerRoster.Instance != null ? PlayerRoster.Instance.Count : 0;

            TryAdvance();
        }

        // ------------------------------------------------------------------
        // client: receive
        // ------------------------------------------------------------------

        [ClientRpc]
        private void ReceiveBriefingClientRpc(RoleBriefing briefing, ClientRpcParams rpcParams = default)
        {
            LocalBriefing = briefing;
            HasBriefing = true;

            Debug.Log($"[Match] Briefing received: role={briefing.Role}, team={briefing.Team}, " +
                      $"{(briefing.KnowsOwnGuilt ? $"guilt={briefing.IsActuallyGuilty}" : "guilt hidden")}");

            BriefingChanged?.Invoke();
        }

        /// <summary>Host-only reset, so a fresh case redeals seats and clears readiness.</summary>
        public void HostReset()
        {
            if (!IsServer) return;
            _ready.Clear();
            _readyCount.Value = 0;
            _phase.Value = MatchPhase.LobbyReady;
        }
    }
}
