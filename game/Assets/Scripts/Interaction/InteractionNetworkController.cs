using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Match;

namespace CaseClosed.Game.Interaction
{
    /// <summary>
    /// The single door between "a client wants something" and "it happened".
    ///
    /// Every interaction in the game passes through here, which is the point: the
    /// distance check, the permission check and the hold timer exist once. A future
    /// shelf or terminal cannot forget them, because it never gets to decide.
    ///
    /// WHAT THE CLIENT MAY SAY: "I would like to interact with object 47." That is
    /// the whole vocabulary. It sends no position, no result, and no completion time.
    /// The server resolves the id against NGO's spawn table, measures the distance
    /// itself, and runs its own clock — so a modified client can ask for anything
    /// and still get nothing it has not earned.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class InteractionNetworkController : NetworkBehaviour
    {
        public static InteractionNetworkController Instance { get; private set; }

        [Header("Validation")]
        [Tooltip("Slack added to each object's MaxDistance, absorbing latency between " +
                 "the client aiming and the server measuring.")]
        public float DistanceTolerance = 1.0f;

        [Tooltip("What blocks line of sight. Players are ignored automatically.")]
        public LayerMask LineOfSightBlockers = ~0;

        [Tooltip("Eye height above the player's feet, used for distance and sight checks.")]
        public float EyeHeight = 1.5f;

        /// <summary>A hold in progress. Server-side only; clients never see this struct.</summary>
        private class ActiveHold
        {
            public ulong ClientId;
            public NetworkInteractable Target;
            public float StartTime;
        }

        private readonly Dictionary<ulong, ActiveHold> _holds = new();

        /// <summary>Raised on the local client when the server answers.</summary>
        public event System.Action<InteractionResponse> ResponseReceived;

        /// <summary>Local hold progress 0..1, for the prompt's progress bar.</summary>
        public float LocalHoldProgress { get; private set; }
        public ulong LocalHoldTarget { get; private set; } = NetworkInteractable.NoOwner;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // client -> server
        // ------------------------------------------------------------------

        /// <summary>Called by the local detector when the player presses the key.</summary>
        public void RequestBegin(ulong targetId)
        {
            LocalHoldTarget = targetId;
            LocalHoldProgress = 0f;
            BeginInteractionServerRpc(targetId);
        }

        /// <summary>Called when the player releases the key or loses the target.</summary>
        public void RequestCancel()
        {
            if (LocalHoldTarget == NetworkInteractable.NoOwner) return;
            LocalHoldTarget = NetworkInteractable.NoOwner;
            LocalHoldProgress = 0f;
            CancelInteractionServerRpc();
        }

        /// <summary>
        /// No clientId parameter, deliberately. NGO fills in the sender, so a client
        /// can only ever act as itself — impersonating another player is not something
        /// this API can express, which is stronger than validating against it.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void BeginInteractionServerRpc(ulong targetId, ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            var target = ResolveTarget(targetId);
            if (target == null)
            {
                Respond(sender, targetId, InteractionOutcome.RejectedUnknownTarget);
                return;
            }

            var verdict = ValidateAll(sender, target);
            if (verdict != InteractionOutcome.Accepted)
            {
                Respond(sender, targetId, verdict);
                return;
            }

            if (target.IsHold) BeginHold(sender, target);
            else CompleteInstant(sender, target);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CancelInteractionServerRpc(ServerRpcParams rpcParams = default)
            => ServerAbandonHold(rpcParams.Receive.SenderClientId, InteractionOutcome.Cancelled);

        // ------------------------------------------------------------------
        // server: validation
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves a claimed id through NGO's spawn table. An id the server does not
        /// know, or one that is not interactable, simply yields nothing — which is how
        /// a fabricated target dies.
        /// </summary>
        private NetworkInteractable ResolveTarget(ulong targetId)
        {
            if (NetworkManager.Singleton == null) return null;
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj))
                return null;

            return netObj != null ? netObj.GetComponent<NetworkInteractable>() : null;
        }

        /// <summary>Every shared rule, in the order that fails cheapest first.</summary>
        private InteractionOutcome ValidateAll(ulong clientId, NetworkInteractable target)
        {
            if (!TryGetPlayer(clientId, out var player)) return InteractionOutcome.RejectedNotOwner;
            if (!PlayerStateAllows(clientId)) return InteractionOutcome.RejectedPlayerState;
            if (!target.IsAvailable) return InteractionOutcome.RejectedUnavailable;
            if (target.IsLockedByOther(clientId)) return InteractionOutcome.RejectedBusy;
            if (!PermissionAllows(clientId, target)) return InteractionOutcome.RejectedWrongRole;

            Vector3 eyes = player.position + Vector3.up * EyeHeight;
            Vector3 point = target.InteractionPoint;

            float allowed = target.MaxDistance + DistanceTolerance;
            if ((point - eyes).sqrMagnitude > allowed * allowed) return InteractionOutcome.RejectedTooFar;

            if (target.RequiresLineOfSight && !HasLineOfSight(eyes, target))
                return InteractionOutcome.RejectedNoLineOfSight;

            return target.ServerValidate(clientId);
        }

        private bool TryGetPlayer(ulong clientId, out Transform player)
        {
            player = null;
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out var client)) return false;
            if (client.PlayerObject == null || !client.PlayerObject.IsSpawned) return false;

            player = client.PlayerObject.transform;
            return true;
        }

        /// <summary>
        /// Match-level gates. Only the states that exist today are checked; the rest
        /// are named so the extension point is obvious rather than implied.
        /// </summary>
        private bool PlayerStateAllows(ulong clientId)
        {
            var flow = MatchFlowController.Instance;
            if (flow == null) return true;   // no match layer yet: interaction is free

            switch (flow.Phase)
            {
                // No case dealt yet. The building is just a building — poking things
                // is fine, and gating it here made the test area look broken.
                case MatchPhase.LobbyReady:
                    return true;

                // A case has been dealt and everyone is reading their card. Movement
                // is frozen and hands are busy until they confirm.
                case MatchPhase.AssigningRoles:
                case MatchPhase.GeneratingCase:
                case MatchPhase.DistributingBriefings:
                case MatchPhase.WaitingForPlayers:
                    return flow.ServerIsReady(clientId);

                case MatchPhase.PreInvestigationReady:
                    return true;

                default:
                    return false;
            }

            // FUTURE GATES (not yet modelled): contempt / holding cell, stunned,
            // hands full, courtroom floor control.
        }

        private bool PermissionAllows(ulong clientId, NetworkInteractable target)
        {
            if (target.RequiredTeam == PlayerTeam.None && target.RequiredRole == PlayerRole.Unassigned)
                return true;

            var roster = PlayerRoster.Instance;
            if (roster == null) return false;   // restricted object with no roster: refuse

            var role = roster.RoleOf(clientId);
            if (target.RequiredRole != PlayerRole.Unassigned && role != target.RequiredRole) return false;
            if (target.RequiredTeam != PlayerTeam.None && RoleInfo.TeamOf(role) != target.RequiredTeam) return false;

            return true;
        }

        /// <summary>
        /// Clear line from the eyes to the object. Hits on the target itself do not
        /// count as blocking, and neither do other players — being behind someone
        /// should not stop you opening a door.
        /// </summary>
        private bool HasLineOfSight(Vector3 eyes, NetworkInteractable target)
        {
            Vector3 point = target.InteractionPoint;
            Vector3 delta = point - eyes;
            float distance = delta.magnitude;
            if (distance < 0.05f) return true;

            var hits = Physics.RaycastAll(eyes, delta / distance, distance,
                                          LineOfSightBlockers, QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<NetworkInteractable>() == target) continue;
                if (hit.collider.GetComponentInParent<PlayerRosterMarker>() != null) continue;
                if (hit.collider.GetComponentInParent<CaseClosed.Game.Prototype.PlayerMovement>() != null) continue;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // server: execution
        // ------------------------------------------------------------------

        private void CompleteInstant(ulong clientId, NetworkInteractable target)
        {
            target.ServerExecute(clientId);
            Respond(clientId, target.NetworkObjectId, InteractionOutcome.Completed, target.Prompt + " - done");
        }

        private void BeginHold(ulong clientId, NetworkInteractable target)
        {
            if (!target.ServerTryLock(clientId))
            {
                Respond(clientId, target.NetworkObjectId, InteractionOutcome.RejectedBusy);
                return;
            }

            // One hold per player: starting a new one abandons the old.
            ServerAbandonHold(clientId, InteractionOutcome.Cancelled, notify: false);

            _holds[clientId] = new ActiveHold
            {
                ClientId = clientId,
                Target = target,
                StartTime = Time.time,
            };

            Respond(clientId, target.NetworkObjectId, InteractionOutcome.Started, target.Prompt + "...");
        }

        /// <summary>
        /// The hold clock. Runs on the SERVER, once per frame, re-checking the same
        /// conditions that let the hold start — so walking away, losing sight, or the
        /// object becoming unavailable all cancel it without the client's cooperation.
        ///
        /// Completion is decided here and nowhere else. The client is never asked how
        /// long it held the key.
        /// </summary>
        private void Update()
        {
            if (!IsServer || _holds.Count == 0) return;

            var finished = new List<ulong>();

            foreach (var pair in _holds)
            {
                var hold = pair.Value;

                if (hold.Target == null || !hold.Target.IsSpawned)
                {
                    finished.Add(pair.Key);
                    continue;
                }

                var verdict = ValidateAll(hold.ClientId, hold.Target);
                if (verdict != InteractionOutcome.Accepted)
                {
                    hold.Target.ServerCancel(hold.ClientId);
                    hold.Target.ServerReleaseLock(hold.ClientId);
                    Respond(hold.ClientId, hold.Target.NetworkObjectId, InteractionOutcome.Cancelled);
                    finished.Add(pair.Key);
                    continue;
                }

                // Per-client, so an object can be cheaper for someone who already
                // earned it. Still the SERVER's clock and the server's opinion of
                // how long this client owes.
                if (Time.time - hold.StartTime < hold.Target.HoldDurationFor(hold.ClientId)) continue;

                hold.Target.ServerExecute(hold.ClientId);
                hold.Target.ServerReleaseLock(hold.ClientId);
                Respond(hold.ClientId, hold.Target.NetworkObjectId,
                        InteractionOutcome.Completed, hold.Target.Prompt + " - done");
                finished.Add(pair.Key);
            }

            foreach (ulong clientId in finished) _holds.Remove(clientId);
        }

        /// <summary>Ends a player's hold, releasing whatever it had claimed.</summary>
        public void ServerAbandonHold(ulong clientId, InteractionOutcome outcome, bool notify = true)
        {
            if (!IsServer) return;
            if (!_holds.TryGetValue(clientId, out var hold)) return;

            if (hold.Target != null)
            {
                hold.Target.ServerCancel(clientId);
                hold.Target.ServerReleaseLock(clientId);
                if (notify) Respond(clientId, hold.Target.NetworkObjectId, outcome);
            }

            _holds.Remove(clientId);
        }

        /// <summary>
        /// A disconnect must never leave a shelf locked forever. Every lock that
        /// client held is released, whether or not it was mid-hold.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            ServerAbandonHold(clientId, InteractionOutcome.Cancelled, notify: false);

            foreach (var interactable in FindObjectsByType<NetworkInteractable>(FindObjectsSortMode.None))
                if (interactable.LockedBy == clientId) interactable.ServerForceRelease();
        }

        // ------------------------------------------------------------------
        // server -> client
        // ------------------------------------------------------------------

        private void Respond(ulong clientId, ulong targetId, InteractionOutcome outcome, string message = null)
        {
            var response = new InteractionResponse
            {
                TargetId = targetId,
                Outcome = outcome,
            };
            response = InteractionResponse.Ok(targetId, outcome,
                message ?? InteractionResponse.DefaultMessage(outcome));

            RespondClientRpc(response, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        [ClientRpc]
        private void RespondClientRpc(InteractionResponse response, ClientRpcParams rpcParams = default)
        {
            if (response.Outcome != InteractionOutcome.Started)
            {
                LocalHoldTarget = NetworkInteractable.NoOwner;
                LocalHoldProgress = 0f;
            }
            ResponseReceived?.Invoke(response);
        }

        /// <summary>
        /// Local progress estimate for the bar. Cosmetic only — the server's clock is
        /// the one that decides, and this is allowed to be slightly wrong.
        /// </summary>
        private void LateUpdate()
        {
            if (LocalHoldTarget == NetworkInteractable.NoOwner) { LocalHoldProgress = 0f; return; }

            var target = ResolveTarget(LocalHoldTarget);
            if (target == null || !target.IsHold) return;

            // Cosmetic only — the bar predicts our own cost so it does not crawl
            // through a hold the server will finish instantly. The server's clock
            // still decides; this just avoids the bar disagreeing with the outcome.
            float cost = Mathf.Max(0.01f, target.HoldDurationFor(NetworkManager.Singleton.LocalClientId));
            LocalHoldProgress = Mathf.Clamp01(LocalHoldProgress + Time.deltaTime / cost);
        }
    }
}
