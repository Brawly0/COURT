using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Match;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// Owns what is inside every drawer, and who has found what.
    ///
    /// THE ANSWER KEY LIVES HERE AND ONLY HERE. The placement map is a plain
    /// Dictionary on a NetworkBehaviour — never a NetworkVariable, never in an RPC
    /// parameter. A client cannot read it because it was never sent; there is no
    /// permission to get wrong.
    ///
    /// The only thing that ever leaves this class is a single EvidenceDiscovery,
    /// addressed to the one player who earned it by completing a search.
    ///
    /// Placement is rebuilt whenever a case is dealt, keyed off the case seed plus a
    /// salt, so a layout is reproducible for debugging without touching the case.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class ArchiveDirector : NetworkBehaviour
    {
        public static ArchiveDirector Instance { get; private set; }

        [Header("Placement")]
        [Tooltip("Combined with the case seed. Change it to reshuffle the Archive " +
                 "without changing the case.")]
        public ulong PlacementSalt = 90210;

        [Tooltip("Fraction of non-evidence containers that hold junk rather than nothing.")]
        [Range(0f, 1f)] public float JunkFraction = 0.5f;

        // ---- server-only state; none of this is replicated ----
        private readonly Dictionary<int, ContainerContents> _placement = new();
        private readonly Dictionary<string, EvidenceInstance> _evidence = new();

        /// <summary>Raised on the discovering client only.</summary>
        public event System.Action<EvidenceDiscovery> EvidenceDiscovered;

        /// <summary>Raised on the searching client when a container held nothing useful.</summary>
        public event System.Action<string> SearchCameUpEmpty;

        public bool HasPlacement => _placement.Count > 0;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (IsServer && MatchFlowController.Instance != null)
                MatchFlowController.Instance.PhaseChanged += OnPhaseChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && MatchFlowController.Instance != null)
                MatchFlowController.Instance.PhaseChanged -= OnPhaseChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Placement happens once the case is dealt and everyone has been briefed —
        /// the point at which the building needs to contain something.
        /// </summary>
        private void OnPhaseChanged()
        {
            if (!IsServer) return;
            var phase = MatchFlowController.Instance.Phase;

            if (phase == MatchPhase.PreInvestigationReady || phase == MatchPhase.WaitingForPlayers)
                ServerBuildPlacement();
        }

        // ------------------------------------------------------------------
        // placement
        // ------------------------------------------------------------------

        /// <summary>Rebuilds the layout for the active case. Server only.</summary>
        public void ServerBuildPlacement()
        {
            if (!IsServer) return;

            var truth = ActiveCaseManager.Instance?.Truth;
            if (truth == null) { Debug.LogWarning("[Archive] No case - nothing to place."); return; }

            var containers = FindContainers();
            if (containers.Count == 0) { Debug.LogWarning("[Archive] No containers in the scene."); return; }

            _placement.Clear();
            _evidence.Clear();

            foreach (var pair in ArchivePlacement.Distribute(
                         truth.File, containers.Count, PlacementSalt, JunkFraction))
                _placement[pair.Key] = pair.Value;

            // Runtime state for every Archive-suitable item, whether or not it fit.
            foreach (var item in ArchiveEvidenceIndex.ArchiveItems(truth.File))
                _evidence[item.EvidenceId] = new EvidenceInstance { EvidenceId = item.EvidenceId, Source = item };

            foreach (var container in containers) container.ServerResetState();

            // Recall every physical body too. Without this a folder from the previous
            // case stays lying on the floor carrying an EvidenceId that has just been
            // reset to Undiscovered — a world object with no matching record, which
            // is the exact custody/knowledge desync the split model exists to prevent.
            EvidenceCustodyDirector.Instance?.ServerRecallAllBodies();

            Debug.Log($"[Archive] Placement built for seed {truth.Seed} salt {PlacementSalt}: " +
                      $"{containers.Count} containers, {_evidence.Count} archive evidence item(s).\n" +
                      ArchivePlacement.Describe(_placement));
        }

        /// <summary>Reshuffle with the same case. Development tool.</summary>
        public void ServerReshuffle(ulong newSalt)
        {
            if (!IsServer) return;
            PlacementSalt = newSalt;
            ServerBuildPlacement();
        }

        private static List<ArchiveContainer> FindContainers() =>
            Object.FindObjectsByType<ArchiveContainer>(FindObjectsInactive.Exclude)
                  .OrderBy(c => c.ContainerIndex)
                  .ToList();

        // ------------------------------------------------------------------
        // search resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Called by ArchiveContainer on completion. SERVER ONLY.
        /// Returns true if real evidence was discovered.
        /// </summary>
        public bool ServerResolveSearch(ArchiveContainer container, ulong clientId)
        {
            if (!IsServer || container == null) return false;

            if (!_placement.TryGetValue(container.ContainerIndex, out var contents))
            {
                SendEmpty(clientId, "Nothing useful found.");
                return false;
            }

            if (!contents.HasEvidence)
            {
                SendEmpty(clientId, contents.HasJunk ? contents.JunkText : "Nothing useful found.");
                return false;
            }

            if (!_evidence.TryGetValue(contents.EvidenceId, out var instance))
            {
                SendEmpty(clientId, "Nothing useful found.");
                return false;
            }

            var team = PlayerRoster.Instance != null
                ? RoleInfo.TeamOf(PlayerRoster.Instance.RoleOf(clientId))
                : PlayerTeam.None;

            // The one-way transition. If it returns false the item was already found,
            // and nobody gets a second copy.
            if (!instance.TryMarkFound(clientId, team, container.ContainerIndex, Time.time))
            {
                SendEmpty(clientId, "Someone has already been through this.");
                return false;
            }

            Debug.Log($"[Archive] {instance.EvidenceId} discovered by client {clientId} " +
                      $"({team}) in container {container.ContainerIndex}.");

            // Discovery reveals a PHYSICAL item rather than teleporting it into an
            // invisible inventory. Knowing what a document says and holding it are
            // different things, and the player now has the first without the second.
            Vector3 spot = container.RevealPoint;
            instance.PlaceInWorld(spot);
            EvidenceCustodyDirector.Instance?.ServerRevealEvidence(instance.EvidenceId, spot);

            SendDiscovery(clientId, instance, container.ContainerIndex);
            return true;
        }

        /// <summary>
        /// The only path by which evidence content reaches a client, and it is
        /// addressed to one person. Nothing here says whether the item matters.
        /// </summary>
        private void SendDiscovery(ulong clientId, EvidenceInstance instance, int containerIndex)
        {
            var discovery = new EvidenceDiscovery
            {
                EvidenceId = instance.EvidenceId,
                Title = Clip128(instance.Source.Title),
                Kind = Clip64(KindOf(instance.Source)),
                Description = Clip512(instance.Source.Contents),
                ContainerIndex = containerIndex,
            };

            ReceiveDiscoveryClientRpc(discovery, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        private void SendEmpty(ulong clientId, string message)
        {
            ReceiveEmptyClientRpc(Clip128(message), new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        [ClientRpc]
        private void ReceiveDiscoveryClientRpc(EvidenceDiscovery discovery, ClientRpcParams p = default)
            => EvidenceDiscovered?.Invoke(discovery);

        [ClientRpc]
        private void ReceiveEmptyClientRpc(Unity.Collections.FixedString128Bytes message,
                                           ClientRpcParams p = default)
            => SearchCameUpEmpty?.Invoke(message.ToString());

        /// <summary>Presentation label only — never a relevance hint.</summary>
        private static string KindOf(IndexedEvidence item)
        {
            string title = item.Title.ToLowerInvariant();
            if (title.Contains("log")) return "Access Record";
            if (title.Contains("schedule")) return "Schedule";
            if (title.Contains("tape") || title.Contains("cctv")) return "Footage";
            return "Document";
        }

        // ------------------------------------------------------------------
        // developer inspection (host only)
        // ------------------------------------------------------------------

        /// <summary>HOST ONLY. The complete answer key — never send this anywhere.</summary>
        public string DeveloperPlacementDump()
        {
            if (!IsServer) return "(host only)";
            if (_placement.Count == 0) return "(no placement yet)";

            var text = new System.Text.StringBuilder();
            text.Append("ARCHIVE PLACEMENT  salt=").Append(PlacementSalt).Append('\n');
            text.Append(ArchivePlacement.Describe(_placement));

            // Both dimensions, side by side — they answer different questions and
            // reading them together is how you spot a custody bug.
            text.Append("\nEVIDENCE  (knowledge | custody)\n");
            foreach (var instance in _evidence.Values)
            {
                text.Append($"  {instance.EvidenceId}  {instance.Knowledge,-12} | {instance.Custody}");

                if (instance.IsCarried) text.Append($" by client {instance.CarrierClientId}");
                if (instance.IsFound)
                    text.Append($"   first found by {instance.FirstFoundByClientId} ({instance.FirstFoundByTeam})");
                if (instance.KnownBy.Count > 0)
                    text.Append($"   known by [{string.Join(",", instance.KnownBy)}]");

                text.Append('\n');
            }

            return text.ToString();
        }

        /// <summary>Server-side read for the audit.</summary>
        public IReadOnlyDictionary<int, ContainerContents> ServerPlacement => _placement;
        public IReadOnlyDictionary<string, EvidenceInstance> ServerEvidence => _evidence;

        private static Unity.Collections.FixedString64Bytes Clip64(string v) => Shorten(v, 58);
        private static Unity.Collections.FixedString128Bytes Clip128(string v) => Shorten(v, 120);
        private static Unity.Collections.FixedString512Bytes Clip512(string v) => Shorten(v, 500);

        /// <summary>Byte budget, not characters — the generator's prose has em-dashes.</summary>
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
