using System.Collections.Generic;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Archive
{
    /// <summary>What one container is holding. Server-side only.</summary>
    public sealed class ContainerContents
    {
        public int ContainerIndex;

        /// <summary>Null when this container holds no real evidence.</summary>
        public string EvidenceId;

        /// <summary>Flavour text for a non-evidentiary result, or null for empty.</summary>
        public string JunkText;

        public bool HasEvidence => !string.IsNullOrEmpty(EvidenceId);
        public bool HasJunk => !string.IsNullOrEmpty(JunkText);
    }

    /// <summary>
    /// Decides which Archive container holds what.
    ///
    /// PLACEMENT IS NOT TRUTH. The generator says what happened; this says where a
    /// player can find a record of it. Nothing here ever writes back to the case —
    /// moving a door log to a different drawer must not change who took the goat.
    ///
    /// DETERMINISM: seeded from the case seed combined with a placement salt, on a
    /// stream separate from both the case generator and role dealing. Same seed plus
    /// same salt reproduces the same layout exactly, which is what makes a bug
    /// report reproducible; changing only the salt reshuffles the building without
    /// touching the case.
    /// </summary>
    public static class ArchivePlacement
    {
        /// <summary>
        /// Non-evidentiary filler. Every line is deliberately inert: no character
        /// names, no times, no locations, nothing that could be mistaken for a lead
        /// or accidentally implicate anyone. Junk must waste a player's time, never
        /// mislead them into a false accusation the case cannot support.
        /// </summary>
        public static readonly string[] Junk =
        {
            "Old parking citation. Unpaid.",
            "Expired building permit.",
            "Payroll record - unrelated department.",
            "Empty folder.",
            "Fire drill sign-off sheet.",
            "Stationery requisition, in triplicate.",
            "Photocopier service log.",
            "Minutes of a meeting about parking.",
            "Blank forms, still shrink-wrapped.",
            "Coffee fund ledger. Somebody owes a lot.",
        };

        /// <summary>
        /// Distributes the case's Archive-suitable evidence across containers, then
        /// fills some of the rest with junk and leaves the remainder empty.
        ///
        /// Every evidence item lands in exactly one container: the shuffled list is
        /// consumed head-first and never revisited, so a piece cannot be duplicated
        /// into two drawers.
        /// </summary>
        public static Dictionary<int, ContainerContents> Distribute(
            CaseFile file, int containerCount, ulong placementSalt, float junkFraction = 0.5f)
        {
            var result = new Dictionary<int, ContainerContents>();
            if (containerCount <= 0) return result;

            for (int i = 0; i < containerCount; i++)
                result[i] = new ContainerContents { ContainerIndex = i };

            if (file == null) return result;

            // A stream of its own: reshuffling the Archive must never disturb the
            // case or the role deal for the same seed.
            var rng = new Pcg32(file.Seed ^ placementSalt, sequence: 7717u);

            var slots = new List<int>();
            for (int i = 0; i < containerCount; i++) slots.Add(i);
            rng.Shuffle(slots);

            int cursor = 0;

            // 1. Real evidence first, one container each.
            var evidence = ArchiveEvidenceIndex.ArchiveItems(file);
            foreach (var item in evidence)
            {
                if (cursor >= slots.Count) break;      // more evidence than drawers
                result[slots[cursor]].EvidenceId = item.EvidenceId;
                cursor++;
            }

            // 2. Junk in some of what is left, so an empty drawer is not a reliable
            // signal that the good ones are elsewhere.
            int remaining = slots.Count - cursor;
            int junkCount = UnityEngine.Mathf.RoundToInt(remaining * junkFraction);

            for (int i = 0; i < junkCount && cursor < slots.Count; i++, cursor++)
                result[slots[cursor]].JunkText = Junk[rng.Next(Junk.Length)];

            // 3. Everything else stays empty.
            return result;
        }

        /// <summary>Developer summary. HOST ONLY — this is the whole answer key.</summary>
        public static string Describe(Dictionary<int, ContainerContents> placement)
        {
            var lines = new System.Text.StringBuilder();
            var keys = new List<int>(placement.Keys);
            keys.Sort();

            foreach (int key in keys)
            {
                var contents = placement[key];
                string what = contents.HasEvidence ? $"Evidence {contents.EvidenceId}"
                            : contents.HasJunk ? "Junk"
                            : "Empty";
                lines.Append($"  Container {key:00}  -> {what}\n");
            }
            return lines.ToString();
        }
    }
}
