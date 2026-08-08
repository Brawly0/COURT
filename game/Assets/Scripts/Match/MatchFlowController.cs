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
            Debug.Log("[Match] ALL PLAYERS READY — PreInvestigationReady.");

            ServerBeginCountdown();
        }

        // ------------------------------------------------------------------
        // investigation
        // ------------------------------------------------------------------

        [Header("Investigation")]
        [Tooltip("Production length of the investigation. 15 minutes.")]
        public float InvestigationDurationSeconds = 900f;

        [Tooltip("Shorter length for testing.")]
        public float DevelopmentDurationSeconds = 300f;

        [Tooltip("Use the development length. Turn OFF for production timing.")]
        public bool UseDevelopmentDuration = true;

        [Tooltip("Seconds of 3-2-1 before the investigation opens.")]
        public float CountdownSeconds = 3f;

        [Tooltip("Seconds players get to walk to the courtroom before stragglers are moved.")]
        public float CourtroomWalkSeconds = 18f;

        /// <summary>
        /// THE AUTHORITATIVE DEADLINE. A timestamp, not a countdown.
        ///
        /// Replicating an end time means one write when the phase starts and none
        /// afterwards — every client derives its own MM:SS from a number that cannot
        /// drift, and a client that joins late or stalls for two seconds still agrees
        /// with everyone else. Ticking a replicated integer down once a second would
        /// be a message per second per player, and each client's display would be a
        /// slightly different lie.
        /// </summary>
        private readonly NetworkVariable<double> _phaseEndsAt = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>How long the current phase was given, so a bar can show a fraction.</summary>
        private readonly NetworkVariable<float> _phaseTotalSeconds = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Server time, shared by construction — NGO synchronises it for us.</summary>
        private double Now => NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ServerTime.Time
            : Time.timeAsDouble;

        /// <summary>Seconds left in whatever the current phase is counting. Never negative.</summary>
        public float SecondsRemaining =>
            _phaseEndsAt.Value <= 0d ? 0f : Mathf.Max(0f, (float)(_phaseEndsAt.Value - Now));

        public float PhaseTotalSeconds => _phaseTotalSeconds.Value;

        /// <summary>The one answer to "may investigation interactions run right now".</summary>
        public bool InvestigationActive => _phase.Value == MatchPhase.Investigation;

        /// <summary>True once time is up, in either post-investigation state.</summary>
        public bool InvestigationOver =>
            _phase.Value is MatchPhase.InvestigationEnding
                        or MatchPhase.CourtroomTransition
                        or MatchPhase.CourtroomReady;

        private void ServerBeginCountdown()
        {
            if (!IsServer) return;
            ServerSetPhaseClock(MatchPhase.InvestigationCountdown, CountdownSeconds);
            Debug.Log($"[Match] Countdown — investigation opens in {CountdownSeconds:F0}s.");
        }

        /// <summary>
        /// One place that sets a phase and its deadline together, so the two can
        /// never disagree. Every timed transition goes through here.
        /// </summary>
        private void ServerSetPhaseClock(MatchPhase phase, float seconds)
        {
            _phase.Value = phase;
            _phaseTotalSeconds.Value = seconds;
            _phaseEndsAt.Value = seconds > 0f ? Now + seconds : 0d;
        }

        /// <summary>
        /// The server's clock, and the only thing that moves the match forward once
        /// it is running. Clients have no path to any of these transitions.
        /// </summary>
        private void Update()
        {
            if (!IsServer) return;
            if (_phaseEndsAt.Value <= 0d) return;
            if (Now < _phaseEndsAt.Value) return;

            switch (_phase.Value)
            {
                case MatchPhase.InvestigationCountdown:
                    ServerSetPhaseClock(MatchPhase.Investigation, ActiveDuration);
                    Debug.Log($"[Match] INVESTIGATE — {ActiveDuration:F0}s on the clock" +
                              $"{(UseDevelopmentDuration ? " (development timing)" : "")}.");
                    break;

                case MatchPhase.Investigation:
                    ServerEndInvestigation();
                    break;

                case MatchPhase.InvestigationEnding:
                    ServerSetPhaseClock(MatchPhase.CourtroomTransition, CourtroomWalkSeconds);
                    Debug.Log($"[Match] Report to the courtroom — {CourtroomWalkSeconds:F0}s.");
                    break;

                case MatchPhase.CourtroomTransition:
                    ServerSeatEveryoneInCourt();
                    break;

                default:
                    _phaseEndsAt.Value = 0d;   // nothing is being timed
                    break;
            }
        }

        public float ActiveDuration =>
            UseDevelopmentDuration ? DevelopmentDurationSeconds : InvestigationDurationSeconds;

        /// <summary>
        /// Time is up.
        ///
        /// THE RULE, chosen for consistency and documented rather than implied:
        /// nothing NEW may start, and anything still being held is cancelled. Lab
        /// work already running is allowed to finish, because the server started that
        /// clock and killing it mid-run would make the machine's own state a lie —
        /// but a finished result can no longer be collected, because collecting is a
        /// new action. So the lab cannot be used to smuggle work past the bell.
        /// </summary>
        private void ServerEndInvestigation()
        {
            ServerSetPhaseClock(MatchPhase.InvestigationEnding, 6f);

            // Cancel every hold in flight. Nobody finishes a search on the bell.
            Interaction.InteractionNetworkController.Instance?.ServerCancelAllHolds();

            SendCaseNotesToEveryone();

            Debug.Log("[Match] INVESTIGATION OVER — new actions refused, holds cancelled.");
        }

        /// <summary>
        /// Moves anyone not already in the courtroom, then locks the phase.
        ///
        /// The teleport exists so one player standing still cannot hold the match
        /// hostage — the walk is a courtesy with a deadline, not a requirement.
        /// </summary>
        private void ServerSeatEveryoneInCourt()
        {
            int moved = CourtroomSeating.ServerSeatAll();
            ServerSetPhaseClock(MatchPhase.CourtroomReady, 0f);
            Debug.Log($"[Match] CourtroomReady — {moved} player(s) moved into position.");
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

        // ------------------------------------------------------------------
        // private case notes
        // ------------------------------------------------------------------

        /// <summary>The local player's own notes. Empty until the bell.</summary>
        public CaseNotes LocalNotes { get; private set; } = CaseNotes.Empty;
        public bool HasNotes { get; private set; }
        public event System.Action NotesChanged;

        /// <summary>
        /// SERVER ONLY. Builds one packet per player from the ledgers that already
        /// exist, and sends each to exactly that player.
        ///
        /// Note what it reads: KnownBy, InterviewedBy, RegisteredByClientId. All
        /// per-player sets the game was already maintaining. Nothing here consults
        /// the CaseFile, so there is no route by which a note could contain a fact
        /// its reader had not earned.
        /// </summary>
        private void SendCaseNotesToEveryone()
        {
            if (!IsServer) return;

            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
                ReceiveNotesClientRpc(BuildNotesFor(clientId), new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
                });
        }

        private static CaseNotes BuildNotesFor(ulong clientId)
        {
            var evidence = new System.Collections.Generic.List<string>();
            var registered = new System.Collections.Generic.List<string>();
            var witnesses = new System.Collections.Generic.List<string>();

            var archive = Archive.ArchiveDirector.Instance;
            if (archive != null)
            {
                foreach (var item in archive.ServerEvidence.Values)
                {
                    if (item?.Source == null) continue;

                    // Only what THIS player read, and titled as they last saw it —
                    // an unprocessed sample still reads as unprocessed in their notes.
                    if (item.IsKnownBy(clientId))
                        evidence.Add(item.Source.TitleFor(item.IsProcessed));

                    if (item.IsRegistered && item.RegisteredByClientId == clientId)
                        registered.Add(item.EvidenceId);
                }
            }

            var witnessDirector = Witnesses.WitnessDirector.Instance;
            if (witnessDirector != null)
                foreach (var witness in witnessDirector.ServerWitnesses.Values)
                    if (witness.IsKnownBy(clientId)) witnesses.Add(witness.CharacterId);

            return new CaseNotes
            {
                EvidenceLearned = Clip(evidence, "Nothing recorded."),
                WitnessesInterviewed = Clip(witnesses, "Nobody interviewed."),
                EvidenceRegistered = Clip(registered, "Nothing registered."),
                EvidenceCount = evidence.Count,
                WitnessCount = witnesses.Count,
                RegisteredCount = registered.Count,
            };
        }

        private static Unity.Collections.FixedString512Bytes Clip(
            System.Collections.Generic.List<string> lines, string empty)
        {
            if (lines.Count == 0) return empty;

            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                string candidate = sb.Length == 0 ? line : "\n" + line;
                // Byte budget, not character budget.
                if (System.Text.Encoding.UTF8.GetByteCount(sb + candidate) > 480) { sb.Append("\n..."); break; }
                sb.Append(candidate);
            }
            return sb.ToString();
        }

        [ClientRpc]
        private void ReceiveNotesClientRpc(CaseNotes notes, ClientRpcParams rpcParams = default)
        {
            LocalNotes = notes;
            HasNotes = true;
            NotesChanged?.Invoke();
        }

        /// <summary>Host-only reset, so a fresh case redeals seats and clears readiness.</summary>
        public void HostReset()
        {
            if (!IsServer) return;
            _ready.Clear();
            _readyCount.Value = 0;
            _phaseEndsAt.Value = 0d;
            _phaseTotalSeconds.Value = 0f;
            _phase.Value = MatchPhase.LobbyReady;
            HasNotes = false;
        }
    }
}
