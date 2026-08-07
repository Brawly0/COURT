using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Match;
using CaseClosed.TruthEngine;

namespace CaseClosed.EditorTools.Cases
{
    /// <summary>
    /// Checks the two properties that actually matter about the case integration:
    /// the same seed reproduces the same case, and nothing hidden reaches a client.
    ///
    /// Secrecy is not something you can eyeball — the leak would be a field on a
    /// struct nobody re-read. So the audit asserts it structurally (the truth type
    /// cannot be serialised at all) AND by content (the crime's time and place do
    /// not appear in anything a client receives).
    ///
    /// Menu: Case Closed > Cases > Run Integration Audit
    /// </summary>
    public static class CaseIntegrationAudit
    {
        [MenuItem("Case Closed/Cases/Run Integration Audit", priority = 0)]
        public static void RunFromMenu()
        {
            string report = Run(out bool passed);
            Debug.Log(report);
            EditorUtility.DisplayDialog(
                passed ? "Case audit passed" : "Case audit FAILED",
                report, "OK");
        }

        public static string Run(out bool allPassed)
        {
            var sb = new StringBuilder();
            bool pass = true;

            void Check(string name, bool ok, string detail)
            {
                if (!ok) pass = false;
                sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(name.PadRight(44)).Append(detail).Append('\n');
            }

            sb.Append("CASE INTEGRATION AUDIT\n\n");

            // ---- structural: the truth cannot be put on the wire ----
            bool sendable = typeof(INetworkSerializable).IsAssignableFrom(typeof(CompleteCaseTruth));
            Check("CompleteCaseTruth NOT INetworkSerializable", !sendable,
                sendable ? "*** SENDABLE - LEAK ***" : "RPC would not compile");
            Check("CompleteCaseTruth is a managed class", !typeof(CompleteCaseTruth).IsValueType,
                "cannot be a NetworkVariable");

            // ---- determinism ----
            var a = CaseGenerationService.Generate(12345);
            var b = CaseGenerationService.Generate(12345);
            var c = CaseGenerationService.Generate(99999);

            Check("same seed -> identical case", a.Digest() == b.Digest(), "digests match");
            Check("different seed -> different case", a.Digest() != c.Digest(), "digests differ");
            Check("generated case is solvable", a.IsSolvable,
                $"{a.InferenceDepth} proof facts in {a.GenerationAttempts} attempt(s), " +
                $"{a.GenerationMilliseconds:0.0} ms");

            // ---- content: find a case whose perpetrator is NOT the defendant, so
            // that name leaking into public data would be a real leak ----
            CompleteCaseTruth sample = null;
            for (ulong seed = 1; seed < 80 && sample == null; seed++)
            {
                var t = CaseGenerationService.Generate(seed);
                if (!string.IsNullOrEmpty(t.File.Perpetrator) && t.File.Perpetrator != t.File.Defendant)
                    sample = t;
            }

            if (sample == null)
            {
                Check("found a non-defendant perpetrator case", false, "none in seeds 1-80");
            }
            else
            {
                sb.Append($"\n  (sample seed {sample.Seed}: perpetrator {sample.File.Perpetrator}, ")
                  .Append($"defendant {sample.File.Defendant}, guilty={sample.File.Guilty})\n\n");

                var pub = CaseViewFactory.BuildPublicInfo(sample);
                var blob = new StringBuilder()
                    .Append(pub.Title).Append('|').Append(pub.CrimeDescription).Append('|').Append(pub.Briefing);
                foreach (var name in pub.KnownCharacters) blob.Append('|').Append(name);
                string publicText = blob.ToString();

                // Cast names ARE public - everyone was visibly present. The secrets
                // are WHEN and WHERE, which is what players reconstruct.
                string where = World.Locations[sample.File.CrimeLocation];
                string when = World.Slots[sample.File.CrimeSlot];

                Check("public hides crime LOCATION", !publicText.Contains(where), $"'{where}' absent");
                Check("public hides crime TIME", !publicText.Contains(when), $"'{when}' absent");
                Check("public carries no observations",
                    !publicText.Contains(" saw ") && !publicText.Contains(" heard "), "none");

                var investigator = CaseViewFactory.BuildPlayerView(sample, PlayerRole.Prosecutor);
                var defendant = CaseViewFactory.BuildPlayerView(sample, PlayerRole.Defendant);

                Check("investigator: guilt flag gated off", !investigator.KnowsOwnGuilt, "KnowsOwnGuilt=false");

                // The gating check above is only meaningful against a GUILTY case:
                // if the defendant is innocent, "investigator sees false" could just
                // be the true answer leaking through and looking correct.
                CompleteCaseTruth guiltySample = null;
                for (ulong seed = 1; seed < 80 && guiltySample == null; seed++)
                {
                    var t = CaseGenerationService.Generate(seed);
                    if (t.File.Guilty) guiltySample = t;
                }

                if (guiltySample == null)
                {
                    Check("found a GUILTY case to test gating", false, "none in seeds 1-80");
                }
                else
                {
                    var guiltyInvestigator = CaseViewFactory.BuildPlayerView(guiltySample, PlayerRole.Prosecutor);
                    var guiltyDefendant = CaseViewFactory.BuildPlayerView(guiltySample, PlayerRole.Defendant);

                    Check("GUILTY case: investigator still sees false",
                        !guiltyInvestigator.IsActuallyGuilty,
                        $"seed {guiltySample.Seed} defendant IS guilty; investigator packet says False");
                    Check("GUILTY case: defendant sees true",
                        guiltyDefendant.IsActuallyGuilty, "defendant correctly told");
                    Check("GUILTY case: investigator briefing hides it",
                        !guiltyInvestigator.PrivateBriefing.ToString().ToLower().Contains("did take"),
                        "no admission text");
                }
                Check("investigator briefing hides perpetrator",
                    !investigator.PrivateBriefing.ToString().Contains(sample.File.Perpetrator),
                    $"'{sample.File.Perpetrator}' absent");
                Check("defendant is told their own guilt", defendant.KnowsOwnGuilt,
                    $"guilty={defendant.IsActuallyGuilty}");
                Check("defendant's guilt matches the truth",
                    defendant.IsActuallyGuilty == sample.File.Guilty, "consistent");
            }

            // ---- role dealing ----
            sb.Append('\n');

            var lobby4 = new ulong[] { 0, 1, 2, 3 };
            var deal1 = RoleAssignment.Deal(4242, lobby4);
            var deal2 = RoleAssignment.Deal(4242, lobby4);
            var deal3 = RoleAssignment.Deal(9999, lobby4);

            int defendants = 0, attorneys = 0, prosecutors = 0, investigators = 0, unassigned = 0;
            foreach (var r in deal1.Values)
            {
                if (r == PlayerRole.Defendant) defendants++;
                else if (r == PlayerRole.DefenseAttorney) attorneys++;
                else if (r == PlayerRole.Prosecutor) prosecutors++;
                else if (r == PlayerRole.Investigator) investigators++;
                else unassigned++;
            }

            Check("exactly one Defendant is dealt", defendants == 1, RoleAssignment.Describe(deal1));
            Check("nobody is left Unassigned", unassigned == 0, "all 4 seated");
            Check("4p table is the specified four roles",
                  defendants == 1 && attorneys == 1 && prosecutors == 1 && investigators == 1,
                  "Defendant + DefenseAttorney + Prosecutor + Investigator");
            Check("unique roles are unique", RoleAssignment.UniqueRolesAreUnique(deal1.Values),
                  "no duplicate Defendant/Attorney/Prosecutor");

            // Teams must come out 2 v 2 for the prototype table.
            int defenseSide = 0, prosecutionSide = 0;
            foreach (var r in deal1.Values)
            {
                var t = RoleInfo.TeamOf(r);
                if (t == PlayerTeam.Defense) defenseSide++;
                else if (t == PlayerTeam.Prosecution) prosecutionSide++;
            }
            Check("teams split 2 v 2", defenseSide == 2 && prosecutionSide == 2,
                  $"{defenseSide} defense / {prosecutionSide} prosecution");

            bool sameDeal = true;
            foreach (var pair in deal1) if (deal2[pair.Key] != pair.Value) sameDeal = false;
            Check("same seed -> same table", sameDeal, "reproducible");

            bool differentDeal = false;
            foreach (var pair in deal1) if (deal3[pair.Key] != pair.Value) differentDeal = true;
            Check("different seed -> different table", differentDeal, "reshuffled");

            // Order must not matter, or the deal is not reproducible across runs:
            // NGO reports connected clients in whatever order it likes.
            var reversed = RoleAssignment.Deal(4242, new ulong[] { 3, 2, 1, 0 });
            bool orderStable = true;
            foreach (var pair in deal1) if (reversed[pair.Key] != pair.Value) orderStable = false;
            Check("client order does not change the deal", orderStable, "sorted before shuffling");

            // Late join must never produce a second Defendant.
            var existing = new List<PlayerRole>(deal1.Values);
            var joiner = RoleAssignment.AssignLateJoiner(existing);
            Check("late joiner is never the Defendant", joiner != PlayerRole.Defendant, $"got {joiner}");
            Check("late joiner on a full table becomes Investigator",
                  joiner == PlayerRole.Investigator, $"got {joiner} (repeatable seat)");

            // A table missing its Prosecutor must fill that seat before adding another
            // Investigator, or the case has nobody to bring the charge.
            var missingProsecutor = new List<PlayerRole>
                { PlayerRole.Defendant, PlayerRole.DefenseAttorney, PlayerRole.Investigator };
            Check("late joiner fills an empty unique seat first",
                  RoleAssignment.AssignLateJoiner(missingProsecutor) == PlayerRole.Prosecutor,
                  "no Prosecutor -> joiner becomes Prosecutor");

            // Larger lobbies still deal exactly one Defendant.
            bool bigOk = true;
            for (int n = 2; n <= 8; n++)
            {
                var ids = new List<ulong>();
                for (ulong i = 0; i < (ulong)n; i++) ids.Add(i);
                var table = RoleAssignment.Deal(77, ids);
                int d = 0;
                foreach (var r in table.Values) if (r == PlayerRole.Defendant) d++;
                if (d != 1 || table.Count != n) bigOk = false;
            }
            Check("2-8 players: always exactly one Defendant", bigOk, "checked every size");

            var vacated = new List<PlayerRole> { PlayerRole.Prosecutor, PlayerRole.DefenseAttorney };
            Check("vacant Defendant is detected", RoleAssignment.VacantDefendant(vacated),
                  "trial system decides in-absentia vs redeal");

            // ---- briefings: the per-role secrecy contract ----
            sb.Append('\n');

            CompleteCaseTruth guilty = null;
            for (ulong seed = 1; seed < 80 && guilty == null; seed++)
            {
                var t = CaseGenerationService.Generate(seed);
                if (t.File.Guilty) guilty = t;
            }

            if (guilty == null) { Check("found a guilty case for briefing checks", false, "none"); }
            else
            {
                var defendantCard = BriefingFactory.Build(guilty, PlayerRole.Defendant);
                var attorneyCard = BriefingFactory.Build(guilty, PlayerRole.DefenseAttorney);
                var prosecutorCard = BriefingFactory.Build(guilty, PlayerRole.Prosecutor);
                var investigatorCard = BriefingFactory.Build(guilty, PlayerRole.Investigator);

                Check("defendant briefing: knows guilt", defendantCard.KnowsOwnGuilt &&
                      defendantCard.IsActuallyGuilty, $"seed {guilty.Seed}, defendant IS guilty");
                Check("defence attorney: guilt withheld",
                      !attorneyCard.KnowsOwnGuilt && !attorneyCard.IsActuallyGuilty, "flag false");
                Check("prosecutor: guilt withheld",
                      !prosecutorCard.KnowsOwnGuilt && !prosecutorCard.IsActuallyGuilty, "flag false");
                Check("investigator: guilt withheld",
                      !investigatorCard.KnowsOwnGuilt && !investigatorCard.IsActuallyGuilty, "flag false");

                // Hiding the perpetrator can ONLY be tested on an innocent case: when
                // the defendant is guilty they ARE the perpetrator, and their name is
                // public because they are the one on trial. Testing it on a guilty
                // case passes vacuously and proves nothing.
                CompleteCaseTruth framed = null;
                for (ulong seed = 1; seed < 80 && framed == null; seed++)
                {
                    var t = CaseGenerationService.Generate(seed);
                    if (!t.File.Guilty && !string.IsNullOrEmpty(t.File.Perpetrator) &&
                        t.File.Perpetrator != t.File.Defendant)
                        framed = t;
                }

                if (framed == null)
                {
                    Check("found an innocent case with a real perpetrator", false, "none in seeds 1-80");
                }
                else
                {
                    string perp = framed.File.Perpetrator;
                    var fAttorney = BriefingFactory.Build(framed, PlayerRole.DefenseAttorney);
                    var fProsecutor = BriefingFactory.Build(framed, PlayerRole.Prosecutor);
                    var fInvestigator = BriefingFactory.Build(framed, PlayerRole.Investigator);
                    var fDefendant = BriefingFactory.Build(framed, PlayerRole.Defendant);

                    Check("attorney card hides the real culprit",
                          !fAttorney.PrivateInformation.ToString().Contains(perp),
                          $"seed {framed.Seed}: '{perp}' did it, name absent");
                    Check("prosecutor card hides the real culprit",
                          !fProsecutor.PrivateInformation.ToString().Contains(perp),
                          $"'{perp}' absent");
                    Check("investigator card hides the real culprit",
                          !fInvestigator.PrivateInformation.ToString().Contains(perp),
                          $"'{perp}' absent");
                    Check("even the DEFENDANT is not told who did it",
                          !fDefendant.PrivateInformation.ToString().Contains(perp),
                          "they know they are innocent, not who framed them");
                }

                // Only the defendant gets a timeline, and only their own row.
                Check("only the defendant receives a timeline",
                      defendantCard.PrivateInformation.ToString().Contains("WHERE YOU WERE") &&
                      !attorneyCard.PrivateInformation.ToString().Contains("WHERE YOU WERE") &&
                      !prosecutorCard.PrivateInformation.ToString().Contains("WHERE YOU WERE"),
                      "occupancy stays on the host");

                Check("teams are correct",
                      defendantCard.Team == PlayerTeam.Defense &&
                      attorneyCard.Team == PlayerTeam.Defense &&
                      prosecutorCard.Team == PlayerTeam.Prosecution &&
                      investigatorCard.Team == PlayerTeam.Prosecution, "2 v 2");

                Check("defence attorney is given no facts",
                      !attorneyCard.PrivateInformation.ToString().Contains("OPENING FACT"),
                      "must ask their client");
                Check("prosecutor receives an opening fact",
                      prosecutorCard.PrivateInformation.ToString().Contains("YOUR OPENING FACT"),
                      "from the proof chain, labelled as a placeholder");

                Check("RoleBriefing is sendable, truth is not",
                      typeof(INetworkSerializable).IsAssignableFrom(typeof(RoleBriefing)) &&
                      !typeof(INetworkSerializable).IsAssignableFrom(typeof(CompleteCaseTruth)),
                      "only filtered cards can cross the wire");
            }

            sb.Append('\n').Append(pass ? "ALL CHECKS PASSED" : ">>> FAILURES ABOVE <<<");

            allPassed = pass;
            return sb.ToString();
        }

        /// <summary>Writes the audit plus a full debug dump to disk, for CI or review.</summary>
        public static void WriteReport(string path, ulong sampleSeed)
        {
            var sb = new StringBuilder(Run(out _));
            sb.Append("\n\n---- developer debug view, seed ").Append(sampleSeed).Append(" ----\n\n");
            sb.Append(CaseViewFactory.BuildDeveloperDebugView(CaseGenerationService.Generate(sampleSeed)));
            File.WriteAllText(path, sb.ToString());
        }
    }
}
