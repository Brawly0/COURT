using System.Collections.Generic;

namespace CaseClosed.Game.Witnesses
{
    /// <summary>
    /// The server's record of one witness. HOST MEMORY ONLY.
    ///
    /// Deliberately NOT INetworkSerializable and deliberately not a
    /// NetworkBehaviour field: this holds a name that indexes straight into the
    /// generated CaseFile, so putting it on the wire would put the case on the wire.
    /// The same structural protection CompleteCaseTruth uses.
    ///
    /// The NPC in the lounge (see WitnessNpc) replicates only a display name and a
    /// busy flag. Everything that could be reasoned back to the truth lives here.
    /// </summary>
    public sealed class WitnessRuntime
    {
        /// <summary>Generated identity — a name from CaseFile.CastNames. Stable per seed.</summary>
        public string CharacterId;

        /// <summary>Row into CaseFile.Occupancy and friends. Server-side lookup key.</summary>
        public int CastIndex;

        /// <summary>Which pooled NPC in the lounge is presenting this witness.</summary>
        public int NpcIndex = -1;

        /// <summary>
        /// Who has legitimately interviewed this witness. Per-player, and only ever
        /// grows — the same shape as evidence knowledge, and for the same reason:
        /// several players may know the same statement, and nobody forgets it.
        ///
        /// Teammates are NOT added automatically. If Player A wants Player B to know
        /// what Nadia said, they have to say it out loud.
        /// </summary>
        public readonly HashSet<ulong> InterviewedBy = new();

        /// <summary>
        /// Rendered once, on first completion, then reused. Rebuilding it per
        /// interview would risk two players hearing different words from the same
        /// witness — the statement is a fact about the witness, not about the asker.
        /// </summary>
        public WitnessTestimony? CachedStatement;

        public bool IsKnownBy(ulong clientId) => InterviewedBy.Contains(clientId);

        /// <summary>Returns false if this player already knew it, so callers can tell
        /// a first interview from a review without duplicating the record.</summary>
        public bool GrantKnowledge(ulong clientId) => InterviewedBy.Add(clientId);
    }
}
