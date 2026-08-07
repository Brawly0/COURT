using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// Owns who is holding what, and who knows what.
    ///
    /// TWO LEDGERS, KEPT APART ON PURPOSE:
    ///   knowledge — a set of players per evidence item, only ever grows
    ///   custody   — one location per evidence item, only ever single-valued
    ///
    /// That split is what lets a player hand over a folder without forgetting what
    /// was in it, and what makes duplication impossible: an item cannot be in a
    /// drawer and in a hand, because custody is one field, not a count.
    ///
    /// Everything here runs on the server. Clients may ask; they never decide.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class EvidenceCustodyDirector : NetworkBehaviour
    {
        public static EvidenceCustodyDirector Instance { get; private set; }

        [Header("Carrying")]
        [Tooltip("How many items one player may hold. One, deliberately - this is a " +
                 "physical game, not an RPG backpack.")]
        public int CarryLimit = 1;

        [Header("Dropping")]
        [Tooltip("How far in front of the carrier a dropped item lands.")]
        public float DropForward = 1.1f;

        [Tooltip("Height above the carrier's feet at which it is released.")]
        public float DropHeight = 0.35f;

        /// <summary>Physical objects, pooled in the scene. Server-side lookup by evidence id.</summary>
        private readonly Dictionary<string, PhysicalEvidence> _bodies = new();

        /// <summary>Raised on a client that has just been granted knowledge.</summary>
        public event System.Action<EvidenceDiscovery> KnowledgeGranted;

        /// <summary>Raised locally on pickup/drop, for the HUD.</summary>
        public event System.Action<string, bool> CarryChanged;   // (title, isCarrying)

        /// <summary>What this client is carrying, for the HUD. Empty when nothing.</summary>
        public string LocalCarriedTitle { get; private set; } = "";
        public bool LocalIsCarrying => !string.IsNullOrEmpty(LocalCarriedTitle);

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
        // revealing a discovered item into the world
        // ------------------------------------------------------------------

        /// <summary>
        /// Called by ArchiveDirector once a search succeeds. Takes a free body from
        /// the scene pool and puts it in front of the container.
        ///
        /// Pooled scene objects rather than runtime spawning: it needs no prefab
        /// registration, and the number of physical items a case can produce is
        /// small and known.
        /// </summary>
        public bool ServerRevealEvidence(string evidenceId, Vector3 position)
        {
            if (!IsServer) return false;
            if (_bodies.ContainsKey(evidenceId)) return true;   // already out

            var free = FindPool().FirstOrDefault(b => !b.InUse);
            if (free == null)
            {
                Debug.LogWarning("[Custody] No free evidence body in the pool.");
                return false;
            }

            free.ServerAssign(evidenceId, position);
            _bodies[evidenceId] = free;

            Debug.Log($"[Custody] {evidenceId} revealed into the world.");
            return true;
        }

        private static List<PhysicalEvidence> FindPool() =>
            Object.FindObjectsByType<PhysicalEvidence>(FindObjectsInactive.Exclude).ToList();

        /// <summary>
        /// Returns every physical body to the pool. Called when placement is rebuilt.
        ///
        /// Skipping this leaves folders from the previous case lying around, still
        /// carrying ids whose records have just been reset — pickable objects with no
        /// backing evidence. Custody must be cleared whenever knowledge is.
        /// </summary>
        public void ServerRecallAllBodies()
        {
            if (!IsServer) return;

            foreach (var body in FindPool()) body.ServerRelease();
            _bodies.Clear();
        }

        // ------------------------------------------------------------------
        // pickup
        // ------------------------------------------------------------------

        /// <summary>
        /// SERVER ONLY. Called from PhysicalEvidence.ServerExecute, which the
        /// interaction layer only reaches after distance, sight, player state and
        /// the exclusivity lock have all passed.
        /// </summary>
        public void ServerRequestPickup(PhysicalEvidence body, ulong clientId)
        {
            if (!IsServer || body == null) return;

            var instance = FindInstance(body.EvidenceId);
            if (instance == null) { Notify(clientId, "That is not evidence."); return; }

            // Cannot pick up something nobody has discovered.
            if (!instance.IsFound) { Notify(clientId, "Nothing to take."); return; }

            if (CountCarried(clientId) >= CarryLimit)
            {
                Notify(clientId, "Your hands are full.");
                return;
            }

            // The custody transition is the authority. If it refuses, somebody else
            // got there first.
            if (!instance.TryPickUp(clientId))
            {
                Notify(clientId, "Someone else has that.");
                return;
            }

            body.ServerSetCarried(clientId);

            Debug.Log($"[Custody] {instance.EvidenceId} picked up by client {clientId}.");

            // Possession grants reading rights, per the prototype rule.
            SendKnowledge(clientId, instance);
            NotifyCarry(clientId, instance.Source.Title, true);
        }

        // ------------------------------------------------------------------
        // drop
        // ------------------------------------------------------------------

        /// <summary>Called by the local player's drop key.</summary>
        public void RequestDrop() => DropServerRpc();

        /// <summary>
        /// No position parameter, deliberately. A client that could name the drop
        /// point could post evidence through a wall or into the locker from across
        /// the building. The server uses the carrier's own known transform.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void DropServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            ServerDropFor(sender);
        }

        private void ServerDropFor(ulong clientId)
        {
            if (!IsServer) return;

            var instance = CarriedBy(clientId);
            if (instance == null) { Notify(clientId, "You are not carrying anything."); return; }

            Vector3 position = ComputeDropPosition(clientId);
            if (!instance.TryDrop(clientId, position)) return;

            if (_bodies.TryGetValue(instance.EvidenceId, out var body))
                body.ServerSetInWorld(position);

            Debug.Log($"[Custody] {instance.EvidenceId} dropped by client {clientId} at {position}.");
            NotifyCarry(clientId, instance.Source.Title, false);
        }

        /// <summary>
        /// In front of the carrier, at floor level where possible. Raycast down so
        /// it lands on the floor rather than hovering or sinking through it.
        /// </summary>
        private Vector3 ComputeDropPosition(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out var client))
                return Vector3.zero;

            var player = client.PlayerObject;
            if (player == null) return Vector3.zero;

            Vector3 origin = player.transform.position + Vector3.up * 1.2f;
            Vector3 ahead = origin + player.transform.forward * DropForward;

            if (Physics.Raycast(ahead, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * DropHeight;

            return player.transform.position + player.transform.forward * DropForward
                   + Vector3.up * DropHeight;
        }

        // ------------------------------------------------------------------
        // disconnect
        // ------------------------------------------------------------------

        /// <summary>
        /// DOCUMENTED BEHAVIOUR: a carrier who disconnects drops what they were
        /// holding where they last stood. The item stays in the building and stays
        /// findable. Deleting it would silently remove a clue the case may need, and
        /// teleporting it back to its drawer would let a player hide evidence by
        /// pulling their network cable.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            var carried = ArchiveDirector.Instance?.ServerEvidence.Values
                .Where(e => e.IsCarried && e.CarrierClientId == clientId).ToList();
            if (carried == null) return;

            foreach (var instance in carried)
            {
                Vector3 where = LastKnownPosition(clientId, instance);
                instance.ForceDrop(where);

                if (_bodies.TryGetValue(instance.EvidenceId, out var body))
                    body.ServerSetInWorld(where);

                Debug.Log($"[Custody] Carrier {clientId} disconnected — " +
                          $"{instance.EvidenceId} dropped at {where}, still in play.");
            }
        }

        private Vector3 LastKnownPosition(ulong clientId, EvidenceInstance instance)
        {
            var manager = NetworkManager.Singleton;
            if (manager != null && manager.ConnectedClients.TryGetValue(clientId, out var client)
                && client.PlayerObject != null)
                return client.PlayerObject.transform.position + Vector3.up * DropHeight;

            // Their body is already gone: fall back to where the item last was.
            if (_bodies.TryGetValue(instance.EvidenceId, out var body))
                return body.transform.position;

            return instance.WorldPosition;
        }

        // ------------------------------------------------------------------
        // lookups
        // ------------------------------------------------------------------

        private static EvidenceInstance FindInstance(string evidenceId)
        {
            if (string.IsNullOrEmpty(evidenceId)) return null;
            var director = ArchiveDirector.Instance;
            if (director == null) return null;
            return director.ServerEvidence.TryGetValue(evidenceId, out var instance) ? instance : null;
        }

        public EvidenceInstance CarriedBy(ulong clientId)
        {
            var director = ArchiveDirector.Instance;
            if (director == null) return null;
            return director.ServerEvidence.Values
                .FirstOrDefault(e => e.IsCarried && e.CarrierClientId == clientId);
        }

        private int CountCarried(ulong clientId)
        {
            var director = ArchiveDirector.Instance;
            if (director == null) return 0;
            return director.ServerEvidence.Values.Count(e => e.IsCarried && e.CarrierClientId == clientId);
        }

        // ------------------------------------------------------------------
        // client messaging
        // ------------------------------------------------------------------

        private void SendKnowledge(ulong clientId, EvidenceInstance instance)
        {
            var packet = new EvidenceDiscovery
            {
                EvidenceId = instance.EvidenceId,
                Title = Clip128(instance.Source.Title),
                Kind = Clip64("Document"),
                Description = Clip512(instance.Source.Contents),
                ContainerIndex = instance.FoundInContainer,
            };

            ReceiveKnowledgeClientRpc(packet, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        private void Notify(ulong clientId, string message) =>
            NotifyClientRpc(Clip128(message), new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

        private void NotifyCarry(ulong clientId, string title, bool carrying) =>
            CarryChangedClientRpc(Clip128(title), carrying, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

        [ClientRpc]
        private void ReceiveKnowledgeClientRpc(EvidenceDiscovery packet, ClientRpcParams p = default)
            => KnowledgeGranted?.Invoke(packet);

        [ClientRpc]
        private void NotifyClientRpc(Unity.Collections.FixedString128Bytes message, ClientRpcParams p = default)
            => CarryChanged?.Invoke(message.ToString(), LocalIsCarrying);

        [ClientRpc]
        private void CarryChangedClientRpc(Unity.Collections.FixedString128Bytes title, bool carrying,
                                           ClientRpcParams p = default)
        {
            LocalCarriedTitle = carrying ? title.ToString() : "";
            CarryChanged?.Invoke(title.ToString(), carrying);
        }

        private static Unity.Collections.FixedString64Bytes Clip64(string v) => Shorten(v, 58);
        private static Unity.Collections.FixedString128Bytes Clip128(string v) => Shorten(v, 120);
        private static Unity.Collections.FixedString512Bytes Clip512(string v) => Shorten(v, 500);

        private static string Shorten(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (System.Text.Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            var sb = new System.Text.StringBuilder();
            int used = 0;
            foreach (char ch in value)
            {
                int size = System.Text.Encoding.UTF8.GetByteCount(new[] { ch });
                if (used + size > maxBytes - 3) break;
                sb.Append(ch);
                used += size;
            }
            return sb.Append("...").ToString();
        }
    }
}
