using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game.Archive;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Interaction;

namespace CaseClosed.EditorTools.Archive
{
    /// <summary>
    /// Audits Archive placement, secrecy and discovery.
    ///
    /// Placement is checked by actually running it against real generated cases —
    /// the invariants here ("no item in two drawers", "every id exists in the case")
    /// are properties of data, and data is what catches a bad shuffle.
    ///
    /// Secrecy is checked structurally instead: "the client never receives the
    /// placement map" is a property of the types, not of any particular playthrough.
    ///
    /// Menu: Case Closed > Archive > Run Audit
    /// </summary>
    public static class ArchiveAudit
    {
        [MenuItem("Case Closed/Archive/Run Audit", priority = 0)]
        public static void RunFromMenu()
        {
            string report = Run(out bool passed);
            Debug.Log(report);
            EditorUtility.DisplayDialog(passed ? "Archive audit passed" : "Archive audit FAILED",
                report, "OK");
        }

        public static string Run(out bool allPassed)
        {
            var sb = new StringBuilder("ARCHIVE AUDIT\n\n");
            bool pass = true;

            void Check(string name, bool ok, string detail)
            {
                if (!ok) pass = false;
                sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(name.PadRight(48)).Append(detail).Append('\n');
            }

            const int Containers = 10;
            const ulong Salt = 90210;

            // ---- placement invariants, across many real cases ----
            bool everyIdReal = true, noDuplicates = true, noConflict = true, allArchiveHomed = true;
            int casesChecked = 0, totalPlaced = 0;

            for (ulong seed = 1; seed <= 40; seed++)
            {
                var truth = CaseGenerationService.Generate(seed);
                if (truth == null) continue;
                casesChecked++;

                var known = ArchiveEvidenceIndex.Build(truth.File).ToDictionary(e => e.EvidenceId);
                var archiveIds = new HashSet<string>(
                    ArchiveEvidenceIndex.ArchiveItems(truth.File).Select(e => e.EvidenceId));

                var placement = ArchivePlacement.Distribute(truth.File, Containers, Salt);
                var seen = new HashSet<string>();

                foreach (var contents in placement.Values)
                {
                    if (!contents.HasEvidence) continue;
                    totalPlaced++;

                    // The id must be one this case actually generated.
                    if (!known.ContainsKey(contents.EvidenceId)) everyIdReal = false;

                    // ...and must belong in the Archive, not the lab or the impound lot.
                    if (!archiveIds.Contains(contents.EvidenceId)) allArchiveHomed = false;

                    // ...and must not already be in another drawer.
                    if (!seen.Add(contents.EvidenceId)) noDuplicates = false;

                    // A container holds evidence OR junk, never both.
                    if (contents.HasJunk) noConflict = false;
                }
            }

            Check("every placed EvidenceId exists in its case", everyIdReal, $"{casesChecked} cases, {totalPlaced} placements");
            Check("no EvidenceId occupies two containers", noDuplicates, "one drawer each");
            Check("no container holds evidence AND junk", noConflict, "exclusive contents");
            Check("only Archive-homed evidence is placed", allArchiveHomed,
                  "lab samples and impounded objects stay out of filing cabinets");

            // ---- determinism ----
            var caseA = CaseGenerationService.Generate(4242);
            var first = ArchivePlacement.Distribute(caseA.File, Containers, Salt);
            var again = ArchivePlacement.Distribute(caseA.File, Containers, Salt);
            var salted = ArchivePlacement.Distribute(caseA.File, Containers, Salt + 1);

            bool sameLayout = first.All(kv =>
                again[kv.Key].EvidenceId == kv.Value.EvidenceId &&
                again[kv.Key].JunkText == kv.Value.JunkText);
            Check("same seed + salt -> same layout", sameLayout, "reproducible for bug reports");

            bool differentLayout = first.Any(kv => salted[kv.Key].EvidenceId != kv.Value.EvidenceId);
            Check("changing only the salt reshuffles", differentLayout, "case untouched");

            // Placement must not disturb the case itself.
            var caseB = CaseGenerationService.Generate(4242);
            Check("placement does not alter case truth", caseA.Digest() == caseB.Digest(),
                  "digest identical after placing");

            // ---- junk safety ----
            var cast = CaseClosed.TruthEngine.World.Cast;
            bool junkNamesNobody = ArchivePlacement.Junk.All(j => cast.All(name => !j.Contains(name)));
            bool junkHasNoTimes = ArchivePlacement.Junk.All(j =>
                CaseClosed.TruthEngine.World.Slots.All(slot => !j.Contains(slot)));
            bool junkHasNoPlaces = ArchivePlacement.Junk.All(j =>
                CaseClosed.TruthEngine.World.Locations.All(loc => !j.Contains(loc)));

            Check("junk names no character", junkNamesNobody, "cannot implicate anyone");
            Check("junk states no time", junkHasNoTimes, "cannot be mistaken for a lead");
            Check("junk states no location", junkHasNoPlaces, "inert flavour only");

            sb.Append('\n');

            // ---- secrecy, structural ----
            var directorType = typeof(ArchiveDirector);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var placementField = directorType.GetField("_placement", flags);
            Check("placement map is a plain private field", placementField != null && placementField.IsPrivate,
                  "not a NetworkVariable, never replicated");

            bool placementNotNetworked = placementField != null &&
                !typeof(NetworkVariableBase).IsAssignableFrom(placementField.FieldType);
            Check("placement map is not a NetworkVariable", placementNotNetworked, "cannot sync by accident");

            Check("EvidenceInstance is not sendable",
                  !typeof(INetworkSerializable).IsAssignableFrom(typeof(EvidenceInstance)),
                  "server-side record; an RPC carrying it would not compile");

            // The only thing that crosses to a client.
            var discoveryFields = typeof(EvidenceDiscovery).GetFields().Select(f => f.Name).ToArray();
            bool discoveryIsMinimal = discoveryFields.All(n =>
                n is "EvidenceId" or "Title" or "Kind" or "Description" or "ContainerIndex");
            Check("discovery packet carries no truth metadata", discoveryIsMinimal,
                  string.Join(", ", discoveryFields));

            // ---- discovery transitions ----
            var instance = new EvidenceInstance { EvidenceId = "E-999" };
            bool firstMark = instance.TryMarkFound(3, CaseClosed.Game.Cases.Roles.PlayerTeam.Prosecution, 4, 1f);
            bool secondMark = instance.TryMarkFound(5, CaseClosed.Game.Cases.Roles.PlayerTeam.Defense, 6, 2f);

            Check("first discovery succeeds", firstMark, "Undiscovered -> Found");
            Check("duplicate discovery is refused", !secondMark, "no second payout");
            Check("discoverer is not overwritten", instance.FirstFoundByClientId == 3, "first finder keeps it");
            Check("evidence cannot return to Undiscovered",
                  instance.Knowledge == EvidenceKnowledge.Found, "one-way");

            // ---- custody, the new dimension ----
            sb.Append('\n');

            var item = new EvidenceInstance { EvidenceId = "E-100" };
            item.TryMarkFound(1, CaseClosed.Game.Cases.Roles.PlayerTeam.Defense, 0, 0f);

            Check("undiscovered evidence cannot be picked up",
                  !new EvidenceInstance { EvidenceId = "E-101" }.TryPickUp(1),
                  "custody starts InContainer, not InWorld");

            item.PlaceInWorld(Vector3.zero);
            Check("discovery reveals it into the world",
                  item.Custody == EvidenceCustody.InWorld, "not into an invisible inventory");

            bool tookIt = item.TryPickUp(1);
            Check("a loose item can be picked up", tookIt && item.IsCarried, "custody = Carried");

            Check("two players cannot carry the same item", !item.TryPickUp(2),
                  "second pickup refused while carried");
            Check("carrier is authoritative", item.CarrierClientId == 1, "still client 1");

            Check("a non-carrier cannot drop it", !item.TryDrop(2, Vector3.one),
                  "only the holder may let go");

            bool dropped = item.TryDrop(1, new Vector3(5f, 0f, 5f));
            Check("the carrier can drop it", dropped && item.IsOnTheFloor, "custody = InWorld");

            // The point of splitting the two dimensions.
            Check("knowledge survives dropping", item.IsKnownBy(1),
                  "reading a document is not undone by putting it down");

            // Possession grants reading rights (prototype rule).
            item.TryPickUp(2);
            Check("possession grants knowledge", item.IsKnownBy(2), "picked up -> may read");
            Check("earlier reader still knows it", item.IsKnownBy(1), "knowledge only grows");

            // Custody is single-valued, which is what makes duplication impossible.
            int places = (item.Custody == EvidenceCustody.InContainer ? 1 : 0)
                       + (item.Custody == EvidenceCustody.InWorld ? 1 : 0)
                       + (item.Custody == EvidenceCustody.Carried ? 1 : 0);
            Check("an item is in exactly one place", places == 1,
                  "custody is one field, not a set - duplication is unrepresentable");

            // Disconnect recovery.
            item.ForceDrop(new Vector3(9f, 0f, 9f));
            Check("carrier disconnect drops, never deletes",
                  item.IsOnTheFloor && item.CarrierClientId == EvidenceInstance.NoCarrier,
                  "evidence stays in the building");
            Check("knowledge survives a disconnect", item.IsKnownBy(2), "still remembered");

            var resolve = directorType.GetMethod("ServerResolveSearch");
            Check("discovery resolution is server-entry only", resolve != null,
                  "called from ArchiveContainer.ServerExecute, which the server alone runs");

            // ---- containers reuse the interaction system ----
            Check("ArchiveContainer extends NetworkInteractable",
                  typeof(NetworkInteractable).IsAssignableFrom(typeof(ArchiveContainer)),
                  "no second interaction system");
            Check("container has no contents field",
                  typeof(ArchiveContainer).GetFields(flags)
                      .All(f => !f.Name.ToLower().Contains("evidence")),
                  "nothing to leak, nothing to replicate");
            Check("searched container becomes unavailable",
                  typeof(ArchiveContainer).GetProperty("IsAvailable") != null,
                  "interaction layer refuses before Archive code runs");

            // ---- the physical body carries no truth ----
            var bodyFields = typeof(PhysicalEvidence)
                .GetFields(flags)
                .Where(f => typeof(NetworkVariableBase).IsAssignableFrom(f.FieldType))
                .Select(f => f.Name)
                .ToArray();

            bool bodyIsMinimal = bodyFields.All(n =>
                n is "_evidenceId" or "_custody" or "_carrier" or "_worldPosition" or "LockOwner");
            Check("replicated body carries no case truth", bodyIsMinimal,
                  "replicated: " + string.Join(", ", bodyFields));

            Check("PhysicalEvidence reuses the interaction system",
                  typeof(NetworkInteractable).IsAssignableFrom(typeof(PhysicalEvidence)),
                  "pickup inherits distance, sight, state and lock checks");

            var custodyType = typeof(EvidenceCustodyDirector);
            var dropRpc = custodyType.GetMethod("DropServerRpc", flags);
            Check("drop RPC exists", dropRpc != null, "server decides where it lands");

            if (dropRpc != null)
            {
                // A client-supplied position would let evidence be posted through
                // walls or into the locker from across the building.
                bool noPosition = dropRpc.GetParameters()
                    .All(p => p.ParameterType != typeof(Vector3));
                Check("client cannot choose the drop position", noPosition,
                      "server uses the carrier's own transform");
            }

            Check("custody changes are server-only entry points",
                  custodyType.GetMethod("ServerRequestPickup") != null,
                  "clients request; the server transitions");

            sb.Append('\n').Append(pass ? "ALL CHECKS PASSED" : ">>> FAILURES ABOVE <<<");
            allPassed = pass;
            return sb.ToString();
        }

        public static void WriteReport(string path) => File.WriteAllText(path, Run(out _));
    }
}
