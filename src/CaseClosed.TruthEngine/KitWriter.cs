using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CaseClosed.TruthEngine
{
    /// <summary>
    /// Renders a CaseFile as the Gate 0.5 paper-playtest kit (GDD 14 Phase 0.5):
    /// a GM truth sheet plus per-team handout files, and a full console dump.
    /// Also the reference for how the Unity build will surface each slice.
    /// </summary>
    public static class KitWriter
    {
        private static string Loc(int i) => World.Locations[i];
        private static string Slot(int i) => World.Slots[i];

        // ---------------- statements (opening statements per witness) ----------------
        public static List<string> OpeningStatement(CaseFile c, string witness)
        {
            int wi = c.CastIndex(witness);
            string ClaimedLoc(int t) =>
                witness == c.Perpetrator && c.PerpClaimedLocation >= 0 && t == c.CrimeSlot
                    ? Loc(c.PerpClaimedLocation)
                    : Loc(c.Occupancy[wi][t]);

            var saw = c.Obs[witness].Where(o => o.Verb == "saw").ToList();
            // crime-relevant sightings always survive the cap (proof chain must exist on paper)
            saw = saw.OrderBy(o => !(o.Slot == c.CrimeSlot &&
                                     (o.Location == c.CrimeLocation || o.Subject == c.Perpetrator)))
                     .ToList();

            var lines = new List<string>
            {
                $"I was in the {ClaimedLoc(0)} at {Slot(0)}.",
                $"I was in the {ClaimedLoc(c.CrimeSlot)} at {Slot(c.CrimeSlot)}.",
            };
            lines.AddRange(saw.Select(o => $"I saw {o.Subject} in the {Loc(o.Location)} at {Slot(o.Slot)}."));
            lines.AddRange(c.Obs[witness].Where(o => o.Verb == "heard")
                 .Select(o => $"I heard {o.Subject} near the {Loc(o.Location)} at {Slot(o.Slot)}."));
            return lines.Distinct().Take(5).ToList();
        }

        // ---------------- GM sheet ----------------
        public static string GmSheet(CaseFile c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# GM TRUTH SHEET — {c.Title} (seed {c.Seed})");
            sb.AppendLine();
            sb.AppendLine($"Charge: theft of {c.CrimeObject}. Defendant: {c.Defendant}.");
            sb.AppendLine($"**GUILTY: {(c.Guilty ? "YES — " + c.Perpetrator : "NO — true culprit: " + c.Perpetrator)}**");
            sb.AppendLine($"Crime: {Loc(c.CrimeLocation)} at {Slot(c.CrimeSlot)} | Clarity tier: {c.Clarity}");
            sb.AppendLine();

            sb.AppendLine("## Occupancy matrix (ground truth — no player ever sees this)");
            sb.AppendLine();
            sb.Append("| Actor |").Append(string.Join(" | ", World.Slots)).AppendLine(" |");
            sb.Append("|---|").Append(string.Join("|", World.Slots.Select(_ => "---"))).AppendLine("|");
            for (int i = 0; i < c.CastNames.Count; i++)
            {
                string name = c.CastNames[i] + (i == 0 ? " (DEF)" : "");
                sb.Append("| ").Append(name).Append(" |");
                for (int t = 0; t < World.Slots.Length; t++)
                    sb.Append(' ').Append(Loc(c.Occupancy[i][t]).Split(' ')[0]).Append(" |");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("## Corruption ledger (what really happened + how it is caught)");
            foreach (var e in c.Ledger)
                sb.AppendLine($"- **{e.Witness}** [{e.Kind}]: {e.TruthNote}\n  - counter: {e.CounterNote}");
            if (c.Ledger.Count == 0) sb.AppendLine("- none");

            sb.AppendLine();
            sb.AppendLine("## Defendant hand fidelity");
            foreach (var f in c.Hand)
                sb.AppendLine($"- [{f.Stamp.ToString().ToUpper()}] {f.Text} -> {f.Fidelity}" +
                              (f.CorruptionCause != null ? $" ({f.CorruptionCause})" : ""));

            sb.AppendLine();
            sb.AppendLine("## The proof chain (pooled-solvable facts)");
            foreach (var p in c.ProofFacts) sb.AppendLine($"- {p}");

            sb.AppendLine();
            sb.AppendLine("## Evidence contents (GM eyes only)");
            foreach (var e in c.Evidence)
                sb.AppendLine($"- **{e.Name}** ({e.FoundAt}): {e.GmContents}");

            sb.AppendLine();
            sb.AppendLine("## Witness opening statements (read aloud on first interview)");
            foreach (var w in c.CastNames.Skip(1))
            {
                sb.AppendLine($"### {w}");
                foreach (var line in OpeningStatement(c, w)) sb.AppendLine($"- {line}");
            }

            sb.AppendLine();
            sb.AppendLine("## GM rules");
            sb.AppendLine("- The handout is each witness's OPENING statement only. They know their whole");
            sb.AppendLine("  matrix row + everything in Obs — answer follow-up questions from this sheet,");
            sb.AppendLine("  in character, keeping every corrupted memory corrupted. They believe it.");
            sb.AppendLine("- Baggage facts surface when players ask the right questions or fetch the evidence.");
            sb.AppendLine();
            sb.AppendLine("## Baggage (the defendant looks guilty regardless — the Baggage Rule)");
            foreach (var b in c.Baggage) sb.AppendLine($"- {b}");
            return sb.ToString();
        }

        // ---------------- team handouts ----------------
        public static string PublicBrief(CaseFile c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# CASE FILE — {c.Title}");
            sb.AppendLine();
            sb.AppendLine($"Charge: theft of {c.CrimeObject}.");
            sb.AppendLine($"Defendant: **{c.Defendant}**.");
            sb.AppendLine($"Police summary: {c.CrimeObject} went missing during the event. " +
                          $"{c.Defendant} was arrested after their prints were found on it. They deny everything.");
            sb.AppendLine();
            sb.AppendLine("## People present that day (interviewable)");
            foreach (var w in c.CastNames.Skip(1)) sb.AppendLine($"- {w}");
            sb.AppendLine();
            sb.AppendLine("## Evidence known to exist in the courthouse");
            foreach (var e in c.Evidence) sb.AppendLine($"- {e.Name} — {e.FoundAt}");
            sb.AppendLine();
            sb.AppendLine("Investigation: 15:00. Then trial: 8:00. The judge only counts what you registered.");
            return sb.ToString();
        }

        public static string DefenseBriefing(CaseFile c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# DEFENSE BRIEFING — {c.Title}");
            sb.AppendLine();
            sb.AppendLine("Your client spoke to you in the holding cells. This is everything they gave you.");
            sb.AppendLine("Stamps are how certain the memory FEELS. Certainty is not truth.");
            sb.AppendLine();
            foreach (var f in c.Hand)
                sb.AppendLine($"- [{f.Stamp.ToString().ToUpper()}] {f.Text}");
            sb.AppendLine();
            sb.AppendLine("The Secret is real leverage and real cost. Using it is your call — and theirs.");
            return sb.ToString();
        }

        public static string ProsecutionBriefing(CaseFile c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# PROSECUTION BRIEFING — {c.Title}");
            sb.AppendLine();
            sb.AppendLine("The police file gives you two leads. The file is usually right. Usually.");
            sb.AppendLine();
            sb.AppendLine($"1. Fingerprints were recovered from {c.CrimeObject} — the card is at the Lab.");
            sb.AppendLine($"2. The {Loc(c.CrimeLocation)} door log is in Archives, row 5.");
            return sb.ToString();
        }

        public static void WriteKit(CaseFile c, string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "gm-sheet.md"), GmSheet(c));
            File.WriteAllText(Path.Combine(dir, "public-brief.md"), PublicBrief(c));
            File.WriteAllText(Path.Combine(dir, "defense-briefing.md"), DefenseBriefing(c));
            File.WriteAllText(Path.Combine(dir, "prosecution-briefing.md"), ProsecutionBriefing(c));
        }

        // ---------------- console dump ----------------
        public static string RenderConsole(CaseFile c)
        {
            var sb = new StringBuilder();
            string bar = new string('=', 66);
            sb.AppendLine(bar);
            sb.AppendLine($"  CASE FILE: {c.Title}   (seed {c.Seed})");
            sb.AppendLine($"  Charge: theft of {c.CrimeObject}. Defendant: {c.Defendant}.");
            sb.AppendLine(bar);
            sb.AppendLine(GmSheet(c));
            sb.AppendLine(bar);
            sb.AppendLine("  (Team handouts: use `kit <seed> <dir>` to write separate files.)");
            sb.AppendLine(bar);
            return sb.ToString();
        }

        public static string ScanLine(CaseFile c)
        {
            return $"seed {c.Seed,4} | {c.Title,-22} | {(c.Guilty ? "GUILTY  " : "innocent")} | {c.Clarity,-9}" +
                   $" | perp-lie {(c.PerpClaimedLocation >= 0 ? "Y" : "n")} | protector {(c.Protector != null ? "Y" : "n")}" +
                   $" | proof {c.ProofFacts.Count} | rerolls {c.Rerolls}";
        }
    }
}
