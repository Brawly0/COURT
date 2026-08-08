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

        [Tooltip("Height the drop is projected from. Roughly where the folder is held.")]
        public float DropChestHeight = 1.2f;

        [Tooltip("Half-width kept clear of walls when placing a dropped item.")]
        public float DropClearance = 0.22f;

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

            // The machine holds a body AND an id. Clearing bodies without clearing
            // the machine would leave last case's sample "inside" it, holding an id
            // that has just been recycled — the stale-body bug wearing a lab coat.
            ServerResetLab();
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
            instance.Record(CustodyEventType.PickedUp, clientId, TeamOf(clientId),
                            Time.time, body.transform.position);

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

            instance.Record(CustodyEventType.Dropped, clientId, TeamOf(clientId), Time.time, position);

            Debug.Log($"[Custody] {instance.EvidenceId} dropped by client {clientId} at {position}.");
            NotifyCarry(clientId, instance.Source.Title, false);
        }

        /// <summary>
        /// In front of the carrier, on the floor, and never through a wall.
        ///
        /// THREE STEPS, EACH FIXING A REAL FAILURE:
        ///
        /// 1. Sphere-cast FORWARD first. Projecting blindly to DropForward puts the
        ///    folder on the far side of any wall the player happens to be facing —
        ///    stand nose-to-wall in the Archive and the evidence lands in the
        ///    corridor. The cast shortens the reach to whatever is actually clear.
        ///
        /// 2. Raycast DOWN from there, so it rests on the floor rather than hovering
        ///    at chest height or sinking through it.
        ///
        /// 3. Fall back to the carrier's own feet if either fails. Their feet are
        ///    provably a reachable spot on the floor — they are standing on it —
        ///    which is the one position that cannot be inside geometry.
        ///
        /// None of this consults the client. A client-supplied position could post
        /// evidence anywhere on the map.
        /// </summary>
        private Vector3 ComputeDropPosition(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out var client))
                return Vector3.zero;

            var player = client.PlayerObject;
            if (player == null) return Vector3.zero;

            Vector3 feet = player.transform.position;
            Vector3 chest = feet + Vector3.up * DropChestHeight;
            Vector3 forward = player.transform.forward;

            // 1. How far forward is actually clear? A sphere rather than a ray, so the
            //    folder does not squeeze into a gap narrower than itself.
            float reach = DropForward;
            if (Physics.SphereCast(chest, DropClearance, forward, out var blocker,
                                   DropForward, ~0, QueryTriggerInteraction.Ignore))
                reach = Mathf.Max(0f, blocker.distance - DropClearance);

            Vector3 ahead = chest + forward * reach;

            // 2. Down to the floor.
            if (Physics.Raycast(ahead, Vector3.down, out var ground, DropChestHeight + 2f,
                                ~0, QueryTriggerInteraction.Ignore))
                return ground.point + Vector3.up * DropHeight;

            // 3. Nothing underneath the forward point — a ledge, a gap, the void.
            //    Their own feet are known-good floor.
            return feet + Vector3.up * DropHeight;
        }

        // ------------------------------------------------------------------
        // forensics lab
        // ------------------------------------------------------------------

        [Header("Lab")]
        [Tooltip("Where forensic samples appear. The generator files them in the Lab " +
                 "tray, so they start here rather than being carried in.")]
        public Transform LabIntake;

        /// <summary>
        /// SERVER ONLY. Marks which items the lab must handle and puts them on the
        /// intake bench.
        ///
        /// Compatibility is DERIVED from the generator's own FoundAt string via
        /// ArchiveEvidenceIndex — "Lab tray (processing: 90s)" — so no evidence id is
        /// ever hard-coded and a generator change flows straight through.
        /// </summary>
        public void ServerPrepareLab()
        {
            if (!IsServer) return;

            var director = ArchiveDirector.Instance;
            if (director == null || LabIntake == null) return;

            int prepared = 0;
            foreach (var instance in director.ServerEvidence.Values)
            {
                if (instance?.Source == null) continue;
                if (!instance.Source.RequiresProcessing) continue;

                instance.Processing = EvidenceProcessingState.Unprocessed;
                instance.ProcessingSeconds = instance.Source.ProcessingSeconds;

                // Discovered by being in the lab at all: the sample is sitting in the
                // tray in plain sight. What it SAYS is still redacted until processed.
                instance.TryMarkFound(0UL, PlayerTeam.None, -1, Time.time);

                Vector3 spot = LabIntake.position + LabIntake.right * (prepared * 0.55f - 0.3f);
                instance.PlaceInWorld(spot);
                ServerRevealEvidence(instance.EvidenceId, spot);
                prepared++;
            }

            Debug.Log($"[Lab] Prepared {prepared} forensic sample(s) on the intake bench.");
        }

        /// <summary>The redacted label for the machine display. Never the result.</summary>
        public string RedactedTitleOf(string evidenceId)
        {
            var instance = FindInstance(evidenceId);
            return instance?.Source?.TitleFor(instance.IsProcessed) ?? "sample";
        }

        public bool ServerLoadIntoMachine(ulong clientId, EvidenceInstance instance, Vector3 machinePosition)
        {
            if (!IsServer || instance == null) return false;
            if (!instance.TryLoadIntoMachine(clientId)) return false;

            // The physical body leaves the world: it is inside the machine now, so it
            // must not be pickable and must not be drawn at anyone's carry socket.
            if (_bodies.TryGetValue(instance.EvidenceId, out var body))
                body.ServerStow();

            instance.Record(CustodyEventType.LoadedIntoLab, clientId, TeamOf(clientId),
                            Time.time, machinePosition);

            NotifyCarry(clientId, instance.Source.Title, false);   // clears the carry HUD
            return true;
        }

        public void ServerFinishProcessing(string evidenceId, Vector3 machinePosition)
        {
            if (!IsServer) return;
            var instance = FindInstance(evidenceId);
            if (instance == null || !instance.TryFinishProcessing()) return;

            instance.Record(CustodyEventType.ProcessingComplete, EvidenceInstance.NoCarrier,
                            PlayerTeam.None, Time.time, machinePosition);

            // NOTE: no ClientRpc here. Completion is public via the machine's own
            // replicated state; the RESULT stays put until somebody collects it.
        }

        /// <summary>
        /// Takes the finished sample back out, and grants the forensic result to the
        /// collector ALONE. Teammates learn nothing automatically — if you want them
        /// to know whose prints are on it, tell them.
        /// </summary>
        public bool ServerCollectFromMachine(ulong clientId, string evidenceId, Vector3 machinePosition)
        {
            if (!IsServer) return false;

            var instance = FindInstance(evidenceId);
            if (instance == null) return false;
            if (CountCarried(clientId) >= CarryLimit) { Notify(clientId, "Your hands are full."); return false; }
            if (!instance.TryCollectFromMachine(clientId)) return false;

            if (_bodies.TryGetValue(instance.EvidenceId, out var body))
                body.ServerSetCarried(clientId);

            instance.Record(CustodyEventType.CollectedFromLab, clientId, TeamOf(clientId),
                            Time.time, machinePosition);

            instance.GrantResultKnowledge(clientId);
            SendKnowledge(clientId, instance);          // now un-redacted, to one client
            NotifyCarry(clientId, instance.Source.Title, true);
            return true;
        }

        /// <summary>Empties every machine. Called whenever placement is rebuilt.</summary>
        public void ServerResetLab()
        {
            if (!IsServer) return;
            foreach (var machine in Object.FindObjectsByType<ForensicsMachine>(FindObjectsInactive.Exclude))
                machine.ServerResetMachine();
        }

        // ------------------------------------------------------------------
        // registration
        // ------------------------------------------------------------------

        /// <summary>
        /// SERVER ONLY. Registers whatever this player is actually carrying.
        ///
        /// NOTE THE SIGNATURE: no EvidenceId. The terminal cannot pass one and the
        /// client never sent one — the server looks up the carrier's own item. That
        /// is what makes "register someone else's evidence" and "register a
        /// fabricated id" unrepresentable rather than merely rejected.
        ///
        /// By the time this runs, RegistrationTerminal.ServerValidate has passed on
        /// every frame of the hold, so distance, sight, the lock, discovery, sole
        /// carriership and "not already registered" all still held a moment ago.
        /// The transition re-checks them anyway, because a validation that ran a
        /// frame earlier is not a guarantee.
        /// </summary>
        public void ServerRegisterCarried(ulong clientId, Vector3 terminalPosition)
        {
            if (!IsServer) return;

            var instance = CarriedBy(clientId);
            if (instance == null) { Notify(clientId, "No evidence carried."); return; }
            if (instance.IsRegistered) { Notify(clientId, "Already registered."); return; }

            var team = TeamOf(clientId);
            if (!instance.TryRegister(clientId, team, Time.time))
            {
                Notify(clientId, "Registration interrupted.");
                return;
            }

            // The paper physically leaves play: out of the hands, into the locker.
            // Recalling the body is what makes the folder vanish from the carrier's
            // socket on every machine, because presentation is derived from custody.
            if (_bodies.TryGetValue(instance.EvidenceId, out var body))
            {
                body.ServerRelease();
                _bodies.Remove(instance.EvidenceId);
            }

            Debug.Log($"[Custody] {instance.EvidenceId} REGISTERED by client {clientId} ({team}). " +
                      $"History: {instance.History.Count} events.");

            // Two separate messages, deliberately. The registrant learns which
            // document; everyone learns only that a registration happened.
            RegistrationResultClientRpc(Clip128(instance.Source.Title), new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

            PublicRegistrationClientRpc(new EvidenceRegistrationNotice
            {
                EvidenceId = Clip64(instance.EvidenceId),
                Team = (byte)team,
                MatchTime = Time.time,
            });

            NotifyCarry(clientId, instance.Source.Title, false);   // clears the carry HUD
        }

        private static PlayerTeam TeamOf(ulong clientId) =>
            PlayerRoster.Instance != null
                ? RoleInfo.TeamOf(PlayerRoster.Instance.RoleOf(clientId))
                : PlayerTeam.None;

        /// <summary>Raised on every client when any evidence is registered. Safe payload.</summary>
        public event System.Action<EvidenceRegistrationNotice> RegistrationAnnounced;

        /// <summary>Raised on the registering client only, with the item's title.</summary>
        public event System.Action<string> LocalRegistrationSucceeded;

        [ClientRpc]
        private void PublicRegistrationClientRpc(EvidenceRegistrationNotice notice)
            => RegistrationAnnounced?.Invoke(notice);

        [ClientRpc]
        private void RegistrationResultClientRpc(Unity.Collections.FixedString128Bytes title,
                                                 ClientRpcParams p = default)
            => LocalRegistrationSucceeded?.Invoke(title.ToString());

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
                if (!instance.ForceDrop(where)) continue;   // registered items stay put

                if (_bodies.TryGetValue(instance.EvidenceId, out var body))
                    body.ServerSetInWorld(where);

                instance.Record(CustodyEventType.DroppedOnDisconnect, clientId,
                                TeamOf(clientId), Time.time, where);

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
            // REDACTED UNTIL PROCESSED. The generator puts the forensic answer in the
            // item's own name — "Fingerprint card: Nadia, Officer Dowd" — so sending
            // the real title to someone who has only picked the sample up would hand
            // over the lab result for free.
            var packet = new EvidenceDiscovery
            {
                EvidenceId = instance.EvidenceId,
                Title = Clip128(instance.Source.TitleFor(instance.IsProcessed)),
                Kind = Clip64(instance.Source.RequiresProcessing ? "Forensic sample" : "Document"),
                Description = Clip512(instance.Source.RequiresProcessing && !instance.IsProcessed
                    ? "Not yet analysed. The forensics lab can process this."
                    : instance.Source.Contents),
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
