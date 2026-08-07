using System.Text;
using CaseClosed.TruthEngine;
using CaseClosed.Game.Cases.Roles;
using Unity.Collections;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// THE FILTER. Turns the complete truth into the narrower things other people
    /// are allowed to see.
    ///
    /// Every leak of hidden information would have to pass through this file, which
    /// is why it is small, pure, and separate from anything networked. Reviewing
    /// case secrecy means reviewing this one class — not hunting for stray RPCs
    /// across the project.
    ///
    /// Rule of thumb for anything added here: if a normal player could not have
    /// learned it by walking into the courthouse, it does not belong in a view.
    /// </summary>
    public static class CaseViewFactory
    {
        /// <summary>Planned investigation length (GDD 04). No timer enforces this yet.</summary>
        public const int InvestigationSeconds = 15 * 60;

        /// <summary>
        /// The briefing everyone gets. Deliberately says WHAT was taken and WHO was
        /// around, never WHO DID IT or WHEN precisely — the crime slot is the thing
        /// players reconstruct.
        /// </summary>
        public static PublicCaseInfo BuildPublicInfo(CompleteCaseTruth truth)
        {
            if (truth == null) return PublicCaseInfo.Empty;

            var file = truth.File;

            var info = new PublicCaseInfo
            {
                Title = Clip64(file.Title),
                CrimeDescription = Clip128($"Someone took {file.CrimeObject}."),
                InvestigationSeconds = InvestigationSeconds,
                Seed = truth.Seed,
                KnownCharacters = default,
            };

            var briefing = new StringBuilder();
            briefing.Append("The defendant, ").Append(file.Defendant)
                    .Append(", stands accused of taking ").Append(file.CrimeObject).Append(". ");
            briefing.Append("Everyone below was in the building. Establish where each of them was, ")
                    .Append("and when. Testimony is not evidence.");
            info.Briefing = Clip512(briefing.ToString());

            // Cast names are public: these people are visibly present. Their
            // POSITIONS over time are the secret, and those are not here.
            foreach (var name in file.CastNames)
            {
                if (info.KnownCharacters.Length >= 12) break;
                info.KnownCharacters.Add(Clip32(name));
            }

            return info;
        }

        /// <summary>
        /// One player's private view.
        ///
        /// The only asymmetry that exists right now: the defendant is told whether
        /// they actually did it. Everyone else receives IsActuallyGuilty = false
        /// regardless of the truth, so the field carries no information for them —
        /// inspecting the packet reveals nothing either way.
        /// </summary>
        public static PlayerCaseView BuildPlayerView(CompleteCaseTruth truth, PlayerRole role)
        {
            if (truth == null) return PlayerCaseView.Empty;

            var file = truth.File;
            var view = new PlayerCaseView
            {
                Role = role,
                KnowsOwnGuilt = role == PlayerRole.Defendant,
                // Never read the real flag unless this player is the defendant.
                IsActuallyGuilty = role == PlayerRole.Defendant && file.Guilty,
                PermittedFacts = default,
            };

            if (role == PlayerRole.Defendant)
            {
                var text = new StringBuilder();
                text.Append("You are ").Append(file.Defendant).Append(", the defendant. ");
                text.Append(file.Guilty
                    ? "You did take it. Whether you admit that is your choice."
                    : "You did not take it. Proving that is another matter.");
                text.Append(" Your memory of the day is ").Append(file.Clarity).Append('.');
                view.PrivateBriefing = Clip512(text.ToString());

                // The Baggage Rule: the defendant always looks guilty three innocent
                // ways, and they know which three.
                foreach (var item in file.Baggage)
                {
                    if (view.PermittedFacts.Length >= 3) break;
                    view.PermittedFacts.Add(Clip128(item));
                }
            }
            else if (role == PlayerRole.Prosecutor || role == PlayerRole.Investigator)
            {
                // docs/MAP_DESIGN.md §1: "Prosecution gets 1 forensic fact."
                //
                // From the defendant's BAGGAGE, never the proof chain. Proof facts
                // implicate the real perpetrator, so one of those would name the
                // culprit — see BriefingFactory for the audit failure that caught it.
                view.PrivateBriefing = Clip512(
                    $"You prosecute. The state says {file.Defendant} took {file.CrimeObject}. " +
                    "Forensics have given you one fact. Build the rest before the bell.");

                bool perpIsDefendant = file.Perpetrator == file.Defendant;
                foreach (var item in file.Baggage)
                {
                    if (string.IsNullOrEmpty(item)) continue;
                    if (!perpIsDefendant && !string.IsNullOrEmpty(file.Perpetrator) &&
                        item.Contains(file.Perpetrator)) continue;

                    view.PermittedFacts.Add(Clip128(item));
                    break;
                }
            }
            else if (role == PlayerRole.DefenseAttorney)
            {
                // "Defense gets nothing and has to ask their client." The empty
                // fact list is the mechanic, not an oversight: it is what forces
                // them to go and talk to the Defendant, who may lie to them.
                view.PrivateBriefing = Clip512(
                    $"You defend {file.Defendant}. You have been given no evidence. " +
                    "Your client knows what happened. Whether they tell you is up to them.");
            }
            else
            {
                view.PrivateBriefing = Clip512(
                    "You have no seat at this table yet. Wait for the host to deal a case.");
            }

            return view;
        }

        /// <summary>
        /// EVERYTHING, formatted for a human. Development only — CaseDebugPanel
        /// gates this behind UNITY_EDITOR / DEVELOPMENT_BUILD and a host check, and
        /// it is never sent anywhere.
        /// </summary>
        public static string BuildDeveloperDebugView(CompleteCaseTruth truth)
        {
            if (truth == null) return "(no case)";

            var file = truth.File;
            var sb = new StringBuilder();

            sb.Append("SEED             ").Append(truth.Seed).Append('\n');
            sb.Append("DIGEST           ").Append(Shorten(truth.Digest(), 46)).Append('\n');
            sb.Append("ATTEMPTS         ").Append(truth.GenerationAttempts)
              .Append("   (").Append(truth.GenerationMilliseconds.ToString("0.0")).Append(" ms)\n");
            sb.Append("SOLVER           ").Append(truth.IsSolvable ? "SOLVABLE" : "*** UNSOLVABLE ***")
              .Append("   depth ").Append(truth.InferenceDepth).Append('\n');
            sb.Append("REVERTED CORRUPT ").Append(file.RerolledCorruptions).Append('\n');
            sb.Append('\n');

            sb.Append("TITLE            ").Append(file.Title).Append('\n');
            sb.Append("CRIME            ").Append(file.CrimeObject).Append('\n');
            sb.Append("WHEN             ").Append(SlotName(file.CrimeSlot)).Append('\n');
            sb.Append("WHERE            ").Append(LocationName(file.CrimeLocation)).Append('\n');
            sb.Append("PERPETRATOR      ").Append(file.Perpetrator ?? "(nobody - staged)").Append('\n');
            sb.Append("DEFENDANT        ").Append(file.Defendant).Append('\n');
            sb.Append("DEFENDANT GUILTY ").Append(file.Guilty ? "YES" : "NO").Append('\n');
            sb.Append("CLARITY          ").Append(file.Clarity).Append('\n');
            sb.Append("PROTECTOR        ").Append(file.Protector ?? "-").Append('\n');
            sb.Append("PERP CLAIMS      ").Append(file.PerpClaimedLocation >= 0
                ? LocationName(file.PerpClaimedLocation) : "-").Append('\n');
            sb.Append('\n');

            sb.Append("OCCUPANCY (who was where, per slot)\n     ");
            foreach (var slot in World.Slots) sb.Append(slot.PadRight(14));
            sb.Append('\n');
            for (int i = 0; i < file.CastNames.Count && i < file.Occupancy.Length; i++)
            {
                sb.Append("  ").Append(file.CastNames[i].PadRight(22));
                foreach (int loc in file.Occupancy[i]) sb.Append(LocationName(loc).PadRight(14));
                sb.Append('\n');
            }
            sb.Append('\n');

            sb.Append("PROOF CHAIN (").Append(file.ProofFacts.Count).Append(")\n");
            foreach (var fact in file.ProofFacts) sb.Append("  - ").Append(fact).Append('\n');
            sb.Append('\n');

            sb.Append("EVIDENCE (").Append(file.Evidence.Count).Append(")\n");
            foreach (var item in file.Evidence)
                sb.Append("  - ").Append(item.Name).Append("  @ ").Append(item.FoundAt)
                  .Append("\n      ").Append(item.GmContents).Append('\n');
            sb.Append('\n');

            sb.Append("CORRUPTED MEMORIES (").Append(file.Ledger.Count).Append(")\n");
            foreach (var entry in file.Ledger)
                sb.Append("  - ").Append(entry.Witness).Append("  [").Append(entry.Kind).Append("]\n")
                  .Append("      lie:     ").Append(entry.TruthNote).Append('\n')
                  .Append("      counter: ").Append(entry.CounterNote).Append('\n');
            sb.Append('\n');

            sb.Append("WITNESS OBSERVATIONS\n");
            foreach (var pair in file.Obs)
            {
                sb.Append("  ").Append(pair.Key).Append('\n');
                foreach (var o in pair.Value)
                    sb.Append("      ").Append(o.Corrupted ? "[FALSE] " : "        ")
                      .Append(o.Verb).Append(' ').Append(o.Subject)
                      .Append(" @ ").Append(LocationName(o.Location))
                      .Append(' ').Append(SlotName(o.Slot)).Append('\n');
            }
            sb.Append('\n');

            sb.Append("DEFENDANT BAGGAGE (innocent but incriminating)\n");
            foreach (var item in file.Baggage) sb.Append("  - ").Append(item).Append('\n');
            sb.Append('\n');

            sb.Append("DEFENDANT HAND (Clarity fragments)\n");
            foreach (var fragment in file.Hand)
                sb.Append("  - [").Append(fragment.Stamp).Append('/').Append(fragment.Fidelity).Append("] ")
                  .Append(fragment.Text).Append('\n');

            if (!string.IsNullOrEmpty(file.SecretText))
                sb.Append("\nSECRET\n  ").Append(file.SecretText).Append('\n');

            return sb.ToString();
        }

        // ---- helpers -------------------------------------------------------
        // FixedString silently truncates on overflow, so clip explicitly and
        // visibly rather than discovering a half-sentence in the UI later.

        private static string LocationName(int index) =>
            index >= 0 && index < World.Locations.Length ? World.Locations[index] : "?";

        private static string SlotName(int index) =>
            index >= 0 && index < World.Slots.Length ? World.Slots[index] : "?";

        /// <summary>
        /// Truncate to a BYTE budget, not a character count.
        ///
        /// FixedStringNBytes is sized in bytes and throws on overflow. The generator's
        /// prose contains em-dashes, which are 3 UTF-8 bytes each, so a 124-character
        /// string can be 130 bytes and blow a FixedString128. Counting characters here
        /// was a real crash, found by the audit.
        ///
        /// The budgets below leave headroom for the type's own length prefix.
        /// </summary>
        private static string Shorten(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            var sb = new StringBuilder();
            int used = 0;
            foreach (char ch in value)
            {
                int size = Encoding.UTF8.GetByteCount(new[] { ch });
                if (used + size > maxBytes - 3) break;   // room for the ellipsis
                sb.Append(ch);
                used += size;
            }
            return sb.Append("...").ToString();
        }

        private static FixedString32Bytes Clip32(string value) => Shorten(value, 28);
        private static FixedString64Bytes Clip64(string value) => Shorten(value, 58);
        private static FixedString128Bytes Clip128(string value) => Shorten(value, 120);
        private static FixedString512Bytes Clip512(string value) => Shorten(value, 500);
    }
}
