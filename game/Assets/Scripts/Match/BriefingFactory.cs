using System.Text;
using Unity.Collections;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// Builds one player's briefing card from the hidden truth.
    ///
    /// This is the narrow gate every private fact passes through. Server-side only —
    /// it takes CompleteCaseTruth, which never leaves the host, and returns a
    /// RoleBriefing, which is all a client ever sees.
    ///
    /// RULE OBSERVED THROUGHOUT: where the generator does not produce something a
    /// role is supposed to receive, the text says so explicitly rather than Unity
    /// inventing a fact. An invented "forensic fact" would be indistinguishable from
    /// a real one to a player, and would corrupt a case the engine guarantees is
    /// solvable. Missing outputs are listed in the milestone report instead.
    /// </summary>
    public static class BriefingFactory
    {
        public static RoleBriefing Build(CompleteCaseTruth truth, PlayerRole role)
        {
            if (truth == null) return RoleBriefing.Empty;

            var file = truth.File;
            var team = RoleInfo.TeamOf(role);

            var briefing = new RoleBriefing
            {
                Role = role,
                Team = team,
                Objective = Clip512(RoleInfo.Objective(role)),
                Ability = Clip512(RoleInfo.Ability(role)),
                KnowsOwnGuilt = role == PlayerRole.Defendant,
                // Never touch the real flag unless this player is the defendant.
                IsActuallyGuilty = role == PlayerRole.Defendant && file.Guilty,
            };

            briefing.PrivateInformation = Clip4096(PrivateTextFor(file, role));
            return briefing;
        }

        private static string PrivateTextFor(CaseFile file, PlayerRole role)
        {
            var text = new StringBuilder();

            switch (role)
            {
                case PlayerRole.Defendant:
                    text.Append("You are ").Append(file.Defendant).Append(".\n\n");
                    text.Append(file.Guilty
                        ? "YOU DID TAKE IT. Whether you ever admit that is entirely your choice.\n\n"
                        : "YOU DID NOT TAKE IT. Proving that is another matter.\n\n");

                    text.Append("Your memory of the day is ").Append(file.Clarity).Append(".\n\n");
                    AppendOwnTimeline(text, file);
                    AppendBaggage(text, file);
                    break;

                case PlayerRole.DefenseAttorney:
                    text.Append("You represent ").Append(file.Defendant).Append(".\n\n");
                    text.Append("You have been given NO evidence. That is not an oversight — ")
                        .Append("your client is your only source, and they may lie to you.\n\n");
                    text.Append("You do not know whether they did it. Nobody has told you, ")
                        .Append("and the case file on your desk would not say.");
                    break;

                case PlayerRole.Prosecutor:
                    text.Append("The state charges ").Append(file.Defendant)
                        .Append(" with taking ").Append(file.CrimeObject).Append(".\n\n");
                    AppendOpeningForensicFact(text, file);
                    text.Append("\nYou do not know whether they actually did it. ")
                        .Append("Your job is to prove it, which is not the same thing.");
                    break;

                case PlayerRole.Investigator:
                    text.Append("You work for the prosecution.\n\n");
                    text.Append("Nothing has been handed to you. Everything the prosecutor ")
                        .Append("takes to trial, you have to find first.\n\n");
                    text.Append("You do not know whether the defendant did it.");
                    break;

                default:
                    text.Append("You have not been dealt a seat yet.");
                    break;
            }

            return text.ToString();
        }

        /// <summary>
        /// The defendant's own movements — the one player entitled to their own row
        /// of the occupancy matrix. Everyone else's rows stay on the host.
        /// </summary>
        private static void AppendOwnTimeline(StringBuilder text, CaseFile file)
        {
            int row = file.CastIndex(file.Defendant);
            if (row < 0 || file.Occupancy == null || row >= file.Occupancy.Length)
            {
                text.Append("WHERE YOU WERE: unavailable.\n\n");
                return;
            }

            text.Append("WHERE YOU WERE:\n");
            var slots = file.Occupancy[row];
            for (int slot = 0; slot < slots.Length && slot < World.Slots.Length; slot++)
                text.Append("  ").Append(World.Slots[slot]).Append("  -  ")
                    .Append(World.Locations[slots[slot]]).Append('\n');
            text.Append('\n');
        }

        /// <summary>
        /// The Baggage Rule (GDD): the defendant always looks guilty three innocent
        /// ways, and is the only one who knows which three.
        /// </summary>
        private static void AppendBaggage(StringBuilder text, CaseFile file)
        {
            if (file.Baggage == null || file.Baggage.Count == 0) return;

            text.Append("WHAT MAKES YOU LOOK GUILTY:\n");
            foreach (var item in file.Baggage) text.Append("  - ").Append(item).Append('\n');
        }

        /// <summary>
        /// docs/MAP_DESIGN.md §1: "Prosecution gets 1 forensic fact."
        ///
        /// Sourced from the defendant's BAGGAGE, not from the proof chain.
        ///
        /// The proof chain is the set of facts that implicate the real perpetrator.
        /// Handing the prosecutor one of those names the culprit outright — in an
        /// innocent case it tells them their own defendant did not do it and who
        /// did, which ends the game at the briefing screen. An audit check caught
        /// exactly that.
        ///
        /// Baggage is the right source: true, discoverable, about the DEFENDANT, and
        /// incriminating without being conclusive — it is why the state charged them.
        /// The perpetrator's name is still filtered out defensively, because a
        /// content guarantee nobody enforces is only a hope.
        /// </summary>
        private static void AppendOpeningForensicFact(StringBuilder text, CaseFile file)
        {
            string chosen = null;

            if (file.Baggage != null)
            {
                foreach (var item in file.Baggage)
                {
                    if (string.IsNullOrEmpty(item)) continue;
                    if (NamesThePerpetrator(item, file)) continue;
                    chosen = item;
                    break;
                }
            }

            if (chosen == null)
            {
                text.Append("YOUR OPENING FACT: none available that does not give the game away.\n");
                return;
            }

            text.Append("YOUR OPENING FACT:\n  ").Append(chosen).Append('\n');
            text.Append("  (placeholder: drawn from the defendant's baggage — the generator ")
                .Append("does not yet tag facts as forensic.)\n");
        }

        /// <summary>
        /// True if the text names the real culprit. When the defendant IS the
        /// culprit their name is public — they are the one on trial — so that case
        /// is not treated as a leak.
        /// </summary>
        private static bool NamesThePerpetrator(string text, CaseFile file)
        {
            if (string.IsNullOrEmpty(file.Perpetrator)) return false;
            if (file.Perpetrator == file.Defendant) return false;
            return text.Contains(file.Perpetrator);
        }

        // Byte budgets, not character counts: the generator's prose contains
        // em-dashes, which are 3 UTF-8 bytes each.
        private static string Shorten(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            var sb = new StringBuilder();
            int used = 0;
            foreach (char ch in value)
            {
                int size = Encoding.UTF8.GetByteCount(new[] { ch });
                if (used + size > maxBytes - 3) break;
                sb.Append(ch);
                used += size;
            }
            return sb.Append("...").ToString();
        }

        private static FixedString512Bytes Clip512(string value) => Shorten(value, 500);
        private static FixedString4096Bytes Clip4096(string value) => Shorten(value, 4000);
    }
}
