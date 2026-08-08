using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Witnesses
{
    /// <summary>
    /// What a player is told after interviewing a witness. Sent to exactly one client.
    ///
    /// WHAT IS DELIBERATELY ABSENT, and why each one matters:
    ///   Corrupted flag   — the witness believes it; telling the player which lines
    ///                      are false hands them the solution and destroys the game.
    ///   Occupancy        — the true movement matrix. Interviewing everyone would
    ///                      otherwise reconstruct the whole case.
    ///   Ledger / CorruptionEntry — GM notes containing the literal truth and the
    ///                      fact that disproves it.
    ///   Protector / PerpClaimedLocation — the agenda itself. Its EFFECT is already
    ///                      in what the witness says; the label never travels.
    ///   Perpetrator, Guilty, ProofFacts, SecretText, Hand, Baggage — case truth.
    ///
    /// There is no field here to put any of them in, which is what makes the leak
    /// impossible rather than merely avoided.
    /// </summary>
    public struct WitnessTestimony : INetworkSerializable
    {
        public FixedString64Bytes WitnessId;
        public FixedString64Bytes DisplayName;

        /// <summary>
        /// 4096, not 512. A witness with a busy morning produces up to ~16 lines and
        /// measured 788 bytes at worst across 60 generated cases — 13% of statements
        /// were being silently clipped, so witnesses appeared to forget things in
        /// proportion to how much they had seen. Fixed-size network strings are byte
        /// budgets, not character budgets.
        /// </summary>
        public FixedString4096Bytes Statement;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WitnessId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref Statement);
        }
    }

    /// <summary>
    /// Turns a witness's BELIEVED observations into something they could say aloud.
    ///
    /// THIS IS A PRESENTATION LAYER, NOT A SECOND TRUTH SYSTEM. The generator has
    /// already done every piece of reasoning:
    ///   • corruption is applied IN PLACE — TimeShift rewrote Observation.Slot,
    ///     DescriptorSwap rewrote Observation.Subject
    ///   • withholding is applied by DELETION — ProtectAgenda removed the sighting
    ///     of the perpetrator, SelfPreservation removed the perp's own sightings
    ///
    /// So a corrupted witness reports the wrong time confidently, and a protector
    /// simply never mentions what they deleted. Nothing here decides what is true.
    /// This only writes sentences.
    /// </summary>
    public static class TestimonyWriter
    {
        /// <summary>
        /// Byte budget, not character budget. Sized well above the measured worst
        /// case (788 bytes) so a long statement is never quietly shortened; the
        /// clamp exists only as a backstop against a future cast or world table
        /// growing beyond anything seen today.
        /// </summary>
        private const int StatementBudget = 4000;

        public static WitnessTestimony Build(CaseFile file, string witnessName)
        {
            return new WitnessTestimony
            {
                WitnessId = Clip(witnessName, 58),
                DisplayName = Clip(witnessName, 58),
                Statement = ClipLong(Compose(file, witnessName)),
            };
        }

        /// <summary>
        /// Their own whereabouts, then what they claim to have seen or heard.
        ///
        /// WHEREABOUTS: an honest witness reports where they actually were, so this
        /// reads Occupancy for their own row at the crime slot ONLY — one cell, not
        /// the matrix. A lying perpetrator instead reports PerpClaimedLocation.
        ///
        /// That single field is the one thing the generator leaves unspoken: it
        /// deletes the perpetrator's real sightings but never authors the positive
        /// alibi to replace them. Rendering it here is formatting an existing value,
        /// not inventing a lie.
        /// </summary>
        private static string Compose(CaseFile file, string witnessName)
        {
            var lines = new List<string>();
            int castIndex = file.CastIndex(witnessName);

            if (castIndex >= 0 && file.CrimeSlot >= 0 && file.CrimeSlot < World.Slots.Length)
            {
                bool liesAboutSelf = witnessName == file.Perpetrator && file.PerpClaimedLocation >= 0;

                int claimed = liesAboutSelf
                    ? file.PerpClaimedLocation
                    : file.Occupancy[castIndex][file.CrimeSlot];

                lines.Add($"I was in the {World.Locations[claimed]} around {World.Slots[file.CrimeSlot]}.");
            }

            if (file.Obs != null && file.Obs.TryGetValue(witnessName, out var observations))
            {
                foreach (var o in observations)
                {
                    if (o == null) continue;
                    if (o.Location < 0 || o.Location >= World.Locations.Length) continue;
                    if (o.Slot < 0 || o.Slot >= World.Slots.Length) continue;

                    // NOTE: o.Corrupted is never read here. A corrupted line is
                    // written exactly like an honest one, because that is precisely
                    // what the witness believes.
                    lines.Add(o.Verb == "heard"
                        ? $"I heard {o.Subject} from the {World.Locations[o.Location]} around {World.Slots[o.Slot]}."
                        : $"I saw {o.Subject} in the {World.Locations[o.Location]} around {World.Slots[o.Slot]}.");
                }
            }

            if (lines.Count == 0)
                return "I was around, but I really did not notice anything.";

            var sb = new StringBuilder();
            foreach (var line in lines) sb.Append(line).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        private static FixedString64Bytes Clip(string value, int maxBytes) => Shorten(value, maxBytes);
        private static FixedString4096Bytes ClipLong(string value) => Shorten(value, StatementBudget);

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
    }
}
