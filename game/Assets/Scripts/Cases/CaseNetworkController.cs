using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// THE ONLY THING THAT PUTS CASE DATA ON THE WIRE. If it is not sent from this
    /// file, it is not sent at all.
    ///
    /// Three channels, deliberately different shapes:
    ///
    ///   Lifecycle      NetworkVariable  -> everyone. Knowing a case exists is public.
    ///   PublicCaseInfo NetworkVariable  -> everyone, and late joiners get it free,
    ///                                      because NetworkVariables sync on spawn.
    ///   PlayerCaseView targeted ClientRpc -> one player. A NetworkVariable would be
    ///                                      readable by every client by design, which
    ///                                      is exactly wrong for private briefings.
    ///
    /// CompleteCaseTruth appears nowhere here and cannot: it is a managed class that
    /// does not implement INetworkSerializable, so passing it to an RPC does not
    /// compile.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CaseNetworkController : NetworkBehaviour
    {
        public static CaseNetworkController Instance { get; private set; }

        private readonly NetworkVariable<CaseLifecycleState> _state = new(
            CaseLifecycleState.NoCase,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<PublicCaseInfo> _publicInfo = new(
            PublicCaseInfo.Empty,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public CaseLifecycleState State => _state.Value;
        public PublicCaseInfo PublicInfo => _publicInfo.Value;

        /// <summary>This machine's own private view. Empty until the host sends it.</summary>
        public PlayerCaseView LocalView { get; private set; } = PlayerCaseView.Empty;
        public bool HasLocalView { get; private set; }

        // Server-side bookkeeping: who has acknowledged their private view.
        private readonly HashSet<ulong> _acknowledged = new();

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            _state.OnValueChanged += (_, now) => Debug.Log($"[Case] lifecycle -> {now}");
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
        // host: generate
        // ------------------------------------------------------------------

        /// <summary>
        /// Generate and publish. Host only — a client calling this does nothing,
        /// because there is no RPC path to it. Case creation is not a request
        /// clients can make.
        /// </summary>
        public void HostGenerateCase(ulong seed)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Case] Only the host can generate a case.");
                return;
            }

            _state.Value = CaseLifecycleState.Generating;

            var truth = CaseGenerationService.Generate(seed);
            if (truth == null)
            {
                _state.Value = CaseLifecycleState.NoCase;
                return;
            }

            ActiveCaseManager.Instance?.Store(truth);

            // Seat the cast in the Witness Lounge. Server-side and takes the truth
            // directly: identities come from CaseFile.CastNames, so the same seed
            // always produces the same people. Nothing about them is replicated
            // beyond a display name.
            Witnesses.WitnessDirector.Instance?.ServerSeatWitnesses(truth);

            // Public briefing goes out to everyone, now and to anyone who joins later.
            _publicInfo.Value = CaseViewFactory.BuildPublicInfo(truth);
            _state.Value = CaseLifecycleState.Loaded;

            // Deal the table BEFORE sending private views — each view is built from
            // the seat, so the roster has to exist first.
            Roles.PlayerRoster.Instance?.ServerDeal(seed, NetworkManager.Singleton.ConnectedClientsIds);

            _acknowledged.Clear();
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                SendPrivateViewTo(clientId, truth);
        }

        /// <summary>
        /// The seat this player was dealt. Reads the roster rather than deciding
        /// anything itself — one source of truth for who is who, so a private view
        /// and a name tag can never disagree.
        /// </summary>
        private PlayerRole RoleFor(ulong clientId)
            => Roles.PlayerRoster.Instance != null
                ? Roles.PlayerRoster.Instance.RoleOf(clientId)
                : PlayerRole.Unassigned;

        private void SendPrivateViewTo(ulong clientId, CompleteCaseTruth truth)
        {
            var view = CaseViewFactory.BuildPlayerView(truth, RoleFor(clientId));

            ReceivePlayerViewClientRpc(view, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        // ------------------------------------------------------------------
        // client: receive
        // ------------------------------------------------------------------

        [ClientRpc]
        private void ReceivePlayerViewClientRpc(PlayerCaseView view, ClientRpcParams rpcParams = default)
        {
            LocalView = view;
            HasLocalView = true;

            Debug.Log($"[Case] Received private view: role={view.Role}" +
                      (view.KnowsOwnGuilt ? $", guilty={view.IsActuallyGuilty}" : ", guilt hidden"));

            AcknowledgeViewServerRpc();
        }

        // Any client may acknowledge its own briefing, so ownership is not required.
        [ServerRpc(RequireOwnership = false)]
        private void AcknowledgeViewServerRpc(ServerRpcParams rpcParams = default)
        {
            _acknowledged.Add(rpcParams.Receive.SenderClientId);

            // Ready once everyone currently connected has their briefing.
            if (_state.Value == CaseLifecycleState.Loaded &&
                _acknowledged.Count >= NetworkManager.Singleton.ConnectedClientsIds.Count)
            {
                _state.Value = CaseLifecycleState.Ready;
            }
        }

        // ------------------------------------------------------------------
        // connection churn
        // ------------------------------------------------------------------

        /// <summary>
        /// LATE JOIN. Public info arrives on its own — NetworkVariables replicate to
        /// a client the moment it spawns, so a late joiner sees the title, briefing
        /// and lifecycle without any work here.
        ///
        /// The private view does need sending explicitly, which is what this does.
        ///
        /// The old second-defendant bug is fixed: the joiner is SEATED into the
        /// existing table rather than triggering a fresh deal, so the Defendant seat
        /// is never handed out twice. They fill whichever side is thinner.
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;

            var truth = ActiveCaseManager.Instance?.Truth;
            if (truth == null) return;   // no case yet; they will be dealt in normally

            var role = Roles.PlayerRoster.Instance?.ServerSeatLateJoiner(clientId) ?? PlayerRole.Unassigned;
            Debug.Log($"[Case] Client {clientId} joined mid-case as {role} — sending private view.");

            SendPrivateViewTo(clientId, truth);
        }

        /// <summary>
        /// A player leaving does NOT destroy the case. The host owns the truth; a
        /// client disconnecting only removes a listener.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            _acknowledged.Remove(clientId);
            Roles.PlayerRoster.Instance?.ServerRemove(clientId);

            Debug.Log($"[Case] Client {clientId} left. Case retained " +
                      $"(seed {ActiveCaseManager.Instance?.Truth?.Seed.ToString() ?? "-"}). " +
                      $"Table: {Roles.PlayerRoster.Instance?.Describe() ?? "-"}");
        }
    }
}
