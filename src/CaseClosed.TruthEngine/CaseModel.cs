using System.Collections.Generic;
using System.Text;

namespace CaseClosed.TruthEngine
{
    public enum Clarity { Lucid, Hazy, Fractured }
    public enum Stamp { Clear, Hazy }
    public enum Fidelity { Reliable, Corrupted }
    public enum CorruptionKind { TimeShift, DescriptorSwap, InferencePromotion, ProtectAgenda, SelfPreservation }

    /// <summary>One entry of Obs(X): what a character saw or heard (GDD 06 Stage 2).</summary>
    public sealed class Observation
    {
        public string Verb;      // "saw" | "heard"
        public string Subject;   // who/what
        public int Location;     // index into World.Locations
        public int Slot;         // index into World.Slots
        public bool Corrupted;   // believed, but false (they are never told)

        public Observation(string verb, string subject, int location, int slot)
        {
            Verb = verb; Subject = subject; Location = location; Slot = slot; Corrupted = false;
        }

        public Observation Copy() =>
            new Observation(Verb, Subject, Location, Slot) { Corrupted = Corrupted };
    }

    /// <summary>GM-side record of a lie/error and the truth that disproves it.</summary>
    public sealed class CorruptionEntry
    {
        public string Witness;
        public CorruptionKind Kind;
        public string TruthNote;   // what really happened
        public string CounterNote; // the discoverable fact that disproves it (detectability invariant)

        public CorruptionEntry(string witness, CorruptionKind kind, string truthNote, string counterNote)
        {
            Witness = witness; Kind = kind; TruthNote = truthNote; CounterNote = counterNote;
        }
    }

    /// <summary>A card in the defendant's hand (Clarity system, GDD 07).</summary>
    public sealed class MemoryFragment
    {
        public Stamp Stamp;
        public string Text;
        public Fidelity Fidelity;
        public string CorruptionCause; // null unless corrupted (Fractured only)

        public MemoryFragment(Stamp stamp, string text, Fidelity fidelity, string cause = null)
        {
            Stamp = stamp; Text = text; Fidelity = fidelity; CorruptionCause = cause;
        }
    }

    /// <summary>A discoverable physical/digital item placed in the courthouse.</summary>
    public sealed class EvidenceItem
    {
        public string Name;
        public string FoundAt;    // courthouse placement
        public string GmContents; // what it actually shows (GM eyes only)

        public EvidenceItem(string name, string foundAt, string gmContents)
        {
            Name = name; FoundAt = foundAt; GmContents = gmContents;
        }
    }

    /// <summary>The complete generated case. Host-memory only in the real game (GDD 12).</summary>
    public sealed class CaseFile
    {
        public ulong Seed;
        public string Title;
        public string CrimeObject;
        public string Defendant;
        public List<string> CastNames = new List<string>();
        public int[][] Occupancy;             // [castIndex][slot] -> location index
        public bool Guilty;
        public string Perpetrator;
        public int CrimeSlot;                 // t*
        public int CrimeLocation;             // l*
        public Clarity Clarity;
        public Dictionary<string, List<Observation>> Obs = new Dictionary<string, List<Observation>>();
        public List<CorruptionEntry> Ledger = new List<CorruptionEntry>();
        public string Protector;              // agenda liar covering the perp, or null
        public int PerpClaimedLocation = -1;  // self-preservation lie, or -1 (never the defendant)
        public List<string> Baggage = new List<string>();
        public List<MemoryFragment> Hand = new List<MemoryFragment>();
        public List<EvidenceItem> Evidence = new List<EvidenceItem>();
        public List<string> ProofFacts = new List<string>();  // the pooled-solvable chain
        public string SecretText;
        public int Rerolls;
        public int RerolledCorruptions;       // corruptions reverted for lacking a counter

        public int CastIndex(string name) => CastNames.IndexOf(name);

        /// <summary>Deterministic digest used by tests and replay checks.</summary>
        public string Digest()
        {
            var sb = new StringBuilder();
            sb.Append(Title).Append('|').Append(Guilty).Append('|').Append(Perpetrator)
              .Append('|').Append(CrimeSlot).Append('|').Append(CrimeLocation)
              .Append('|').Append(Clarity).Append('|').Append(Protector ?? "-")
              .Append('|').Append(PerpClaimedLocation);
            foreach (var row in Occupancy) foreach (var loc in row) sb.Append(loc);
            foreach (var e in Ledger) sb.Append('|').Append(e.Witness).Append(':').Append(e.Kind);
            foreach (var f in Hand) sb.Append('|').Append(f.Stamp).Append(':').Append(f.Fidelity);
            return sb.ToString();
        }
    }
}
