using System.Linq;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Interaction;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// A bench that puts real evidence in front of you, for testing carrying.
    ///
    /// WHY THIS EXISTS: reaching a folder normally means starting a match,
    /// generating a case, walking 40 m to the Archive and searching containers until
    /// one of the two archive-suitable items turns up. That is the right flow for
    /// playing and a terrible one for checking whether a folder sits in the hands.
    ///
    /// WHAT IT IS NOT: a fake. It generates a real case, builds real placement, and
    /// reveals real <see cref="EvidenceInstance"/> records through the ordinary
    /// custody path. Pickup, carry, drop, knowledge and the audit all behave exactly
    /// as they do in a match — the bench only skips the walking. A dummy prop with
    /// its own carry logic would test the prop, not the game.
    ///
    /// Pressing it again re-deals: existing bodies are recalled first, so repeat
    /// presses are idempotent and a folder left across the map comes home.
    /// </summary>
    public class CarryTestDispenser : NetworkInteractable
    {
        [Header("Test evidence")]
        [Tooltip("Case seed used when no match has generated one yet.")]
        public ulong TestSeed = 4242;

        [Tooltip("How many folders to lay out. The generator yields 2 archive items per case.")]
        public int Count = 2;

        [Tooltip("Gap between folders along the bench.")]
        public float Spacing = 0.55f;

        [Tooltip("Height above the bench origin the folders appear at.")]
        public float SurfaceHeight = 0.95f;

        [Tooltip("How far in front of the bench they sit, so they are reachable.")]
        public float Reach = 0.35f;

        public override string PromptFor(ulong clientId) => "Deal Test Evidence";

        /// <summary>
        /// SERVER ONLY. Reached only after the interaction layer has already checked
        /// distance, sight, player state and the lock.
        /// </summary>
        public override void ServerExecute(ulong clientId)
        {
            var caseManager = FindAnyObjectByType<ActiveCaseManager>();
            if (caseManager == null)
            {
                Debug.LogWarning("[CarryTest] No ActiveCaseManager in the scene.");
                return;
            }

            if (!caseManager.HasCase)
            {
                caseManager.Store(CaseGenerationService.Generate(TestSeed));
                Debug.Log($"[CarryTest] No match running — generated case from seed {TestSeed}.");
            }

            var director = ArchiveDirector.Instance;
            var custody = EvidenceCustodyDirector.Instance;
            if (director == null || custody == null)
            {
                Debug.LogWarning("[CarryTest] Archive or custody director missing.");
                return;
            }

            if (!director.HasPlacement) director.ServerBuildPlacement();

            // Recall first, so pressing this twice re-deals rather than accumulating
            // orphaned bodies — the stale-body bug from the custody milestone.
            custody.ServerRecallAllBodies();

            var items = director.ServerEvidence.Values.Take(Mathf.Max(1, Count)).ToList();
            if (items.Count == 0)
            {
                Debug.LogWarning("[CarryTest] Placement produced no evidence to deal.");
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var instance = items[i];

                // Knowledge, through the real record. Possession would grant reading
                // rights anyway; marking it found here is what makes it pickable.
                instance.TryMarkFound(clientId, PlayerTeam.None, -1, Time.time);
                instance.GrantKnowledge(clientId);

                Vector3 spot = SlotPosition(i, items.Count);
                instance.PlaceInWorld(spot);
                custody.ServerRevealEvidence(instance.EvidenceId, spot);
            }

            Debug.Log($"[CarryTest] Dealt {items.Count} folders for client {clientId}: " +
                      string.Join(", ", items.Select(e => e.EvidenceId)));
        }

        /// <summary>Laid out along the bench, centred, and lifted onto its surface.</summary>
        private Vector3 SlotPosition(int index, int total)
        {
            float offset = (index - (total - 1) * 0.5f) * Spacing;
            return transform.position
                   + transform.right * offset
                   + transform.forward * Reach
                   + Vector3.up * SurfaceHeight;
        }
    }
}
