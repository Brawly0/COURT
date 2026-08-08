using System.Collections.Generic;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Archive
{
    /// <summary>Where a generated evidence item belongs in the building.</summary>
    public enum EvidenceHome
    {
        Archive,        // paper records: schedules, door logs, case files
        Lab,            // forensic samples awaiting processing
        SecurityOffice, // camera footage
        Impound,        // the physical object itself
        Unknown,
    }

    /// <summary>
    /// One generated evidence item, described in Unity's terms.
    ///
    /// A projection of CaseFile.Evidence — never a copy of the truth. It carries an
    /// id, a home, a title, and the record's own contents. It does NOT carry guilt,
    /// the perpetrator, the proof chain, or anything else the case knows.
    /// </summary>
    public sealed class IndexedEvidence
    {
        public string EvidenceId;      // "E-003", stable for a given seed
        public int SourceIndex;        // position in CaseFile.Evidence
        public string Title;           // EvidenceItem.Name
        public string Contents;        // EvidenceItem.GmContents - the record itself
        public string GeneratorHome;   // EvidenceItem.FoundAt, verbatim
        public EvidenceHome Home;

        /// <summary>Lab seconds parsed from FoundAt. 0 when the item needs no processing.</summary>
        public float ProcessingSeconds;

        /// <summary>
        /// What the item is called BEFORE the lab has finished with it.
        ///
        /// The generator puts the forensic answer in the item's own Name —
        /// "Fingerprint card: Greg the Janitor, Nadia" — so showing the real name to
        /// someone who has not processed it would hand over the result for free.
        /// This is a redaction of data the player has not earned, exactly like the
        /// existing knowledge gate; it invents nothing.
        /// </summary>
        public string RedactedTitle;

        public bool BelongsInArchive => Home == EvidenceHome.Archive;

        /// <summary>Only the generator's Lab-tray items carry a processing time.</summary>
        public bool RequiresProcessing => Home == EvidenceHome.Lab && ProcessingSeconds > 0f;

        /// <summary>What to call it, given whether the lab has finished.</summary>
        public string TitleFor(bool processed) =>
            !RequiresProcessing || processed ? Title : RedactedTitle;
    }

    /// <summary>
    /// WHY THIS EXISTS: the bridge between "what happened" and "where can players
    /// find out". The generator answers the first and must not be edited to answer
    /// the second, so the mapping lives here.
    ///
    /// THE GENERATOR ALREADY DECIDES PLACEMENT. Every EvidenceItem carries a FoundAt
    /// string — "Archives, row 3", "Lab tray (processing: 90s)", "Impound cage". So
    /// Unity reads that intent rather than inventing categories, which is what keeps
    /// fingerprints and stolen goats out of filing cabinets.
    ///
    /// TWO THINGS UNITY ADDS, both flagged as gaps rather than smuggled in:
    ///   - a stable EvidenceId, derived from the item's index. The generator builds
    ///     its evidence list in a fixed order for a given seed, so index 3 is always
    ///     the same item — good enough to key runtime state against, and it needs no
    ///     change to the engine.
    ///   - an EvidenceHome enum, parsed from FoundAt. Presentation only.
    /// </summary>
    public static class ArchiveEvidenceIndex
    {
        public static List<IndexedEvidence> Build(CaseFile file)
        {
            var result = new List<IndexedEvidence>();
            if (file?.Evidence == null) return result;

            for (int i = 0; i < file.Evidence.Count; i++)
            {
                var item = file.Evidence[i];
                if (item == null) continue;

                result.Add(new IndexedEvidence
                {
                    EvidenceId = MakeId(i),
                    SourceIndex = i,
                    Title = item.Name ?? "(unnamed)",
                    Contents = item.GmContents ?? "",
                    GeneratorHome = item.FoundAt ?? "",
                    Home = Classify(item.FoundAt),
                    ProcessingSeconds = ParseProcessingSeconds(item.FoundAt),
                    RedactedTitle = Redact(item.Name),
                });
            }

            return result;
        }

        /// <summary>Stable for a seed, because the generator emits evidence in a fixed order.</summary>
        public static string MakeId(int sourceIndex) => $"E-{sourceIndex:000}";

        /// <summary>
        /// Reads the generator's own FoundAt string. Deliberately conservative:
        /// anything unrecognised is Unknown and therefore NOT placed in the Archive.
        /// Guessing wrong here would put a blood sample in a filing cabinet.
        /// </summary>
        public static EvidenceHome Classify(string foundAt)
        {
            if (string.IsNullOrEmpty(foundAt)) return EvidenceHome.Unknown;
            string text = foundAt.ToLowerInvariant();

            if (text.Contains("archive")) return EvidenceHome.Archive;
            if (text.Contains("lab")) return EvidenceHome.Lab;
            if (text.Contains("security")) return EvidenceHome.SecurityOffice;
            if (text.Contains("impound")) return EvidenceHome.Impound;

            return EvidenceHome.Unknown;
        }

        /// <summary>
        /// Reads "Lab tray (processing: 90s)" and returns 90.
        ///
        /// THE ONE PLACE THIS PROSE IS PARSED. The generator stores the duration as
        /// human-readable text inside FoundAt rather than as a field, so somebody has
        /// to interpret it — but exactly once. Scattering "does this string contain
        /// 90s" through the lab, the HUD and the audit would mean three readers that
        /// can disagree, and a generator wording change would break them one at a time.
        ///
        /// Returns 0 when there is no processing clause, which is what makes
        /// "requires processing" a derived fact rather than a hard-coded id list.
        /// </summary>
        public static float ParseProcessingSeconds(string foundAt)
        {
            if (string.IsNullOrEmpty(foundAt)) return 0f;

            var match = System.Text.RegularExpressions.Regex.Match(
                foundAt, @"processing\s*:\s*(\d+(?:\.\d+)?)\s*s",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success) return 0f;
            return float.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float seconds)
                ? seconds : 0f;
        }

        /// <summary>
        /// Strips the forensic answer out of the item's name, keeping the kind.
        /// "Fingerprint card: Greg, Nadia" -> "Fingerprint card (unprocessed)".
        /// Names without a colon are already generic and pass through.
        /// </summary>
        public static string Redact(string name)
        {
            if (string.IsNullOrEmpty(name)) return "(unidentified sample)";

            int colon = name.IndexOf(':');
            string kind = colon > 0 ? name.Substring(0, colon).Trim() : name.Trim();
            return $"{kind} (unprocessed)";
        }

        /// <summary>Only the items the generator itself files in the Lab.</summary>
        public static List<IndexedEvidence> LabItems(CaseFile file)
        {
            var lab = new List<IndexedEvidence>();
            foreach (var item in Build(file)) if (item.RequiresProcessing) lab.Add(item);
            return lab;
        }

        /// <summary>Only the items the generator itself files in the Archive.</summary>
        public static List<IndexedEvidence> ArchiveItems(CaseFile file)
        {
            var all = Build(file);
            var archive = new List<IndexedEvidence>();
            foreach (var item in all) if (item.BelongsInArchive) archive.Add(item);
            return archive;
        }
    }
}
