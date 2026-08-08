using Unity.Collections;
using Unity.Netcode;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// What ONE player personally learned, handed back to them when the
    /// investigation closes.
    ///
    /// WHY THIS EXISTS AT ALL: players spend fifteen minutes gathering fragments and
    /// then walk into a trial. Without a moment to re-read their own notes, the
    /// courtroom becomes a memory test rather than an argument, and the quiet player
    /// who found the best document forgets they have it.
    ///
    /// WHAT IT CANNOT CONTAIN: anything the player did not legitimately earn. It is
    /// assembled server-side from the SAME per-player ledgers the game already keeps
    /// — evidence knowledge, witness interviews, registrations — so it cannot show
    /// more than those ledgers already granted. It is not a second source of truth;
    /// it is a read of the first one, addressed to the player it belongs to.
    /// </summary>
    public struct CaseNotes : INetworkSerializable
    {
        public FixedString512Bytes EvidenceLearned;
        public FixedString512Bytes WitnessesInterviewed;
        public FixedString512Bytes EvidenceRegistered;
        public int EvidenceCount;
        public int WitnessCount;
        public int RegisteredCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref EvidenceLearned);
            serializer.SerializeValue(ref WitnessesInterviewed);
            serializer.SerializeValue(ref EvidenceRegistered);
            serializer.SerializeValue(ref EvidenceCount);
            serializer.SerializeValue(ref WitnessCount);
            serializer.SerializeValue(ref RegisteredCount);
        }

        public static CaseNotes Empty => new()
        {
            EvidenceLearned = "Nothing recorded.",
            WitnessesInterviewed = "Nobody interviewed.",
            EvidenceRegistered = "Nothing registered.",
        };
    }
}
