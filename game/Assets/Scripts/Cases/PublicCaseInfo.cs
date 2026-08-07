using Unity.Collections;
using Unity.Netcode;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// What EVERYONE is allowed to know. Replicated to all clients, including late
    /// joiners, and safe to print on any screen.
    ///
    /// Contains only the briefing a court would read aloud: what was taken, who is
    /// in the building, how long you have. It carries no perpetrator, no occupancy
    /// matrix, no corrupted memories, no guilt.
    ///
    /// Fixed-size strings on purpose. NetworkVariable requires an unmanaged type, so
    /// `string` and `string[]` are not options — and that constraint is doing useful
    /// work here, because it means this struct cannot quietly grow a reference to
    /// the full CaseFile.
    /// </summary>
    public struct PublicCaseInfo : INetworkSerializable
    {
        public FixedString64Bytes Title;
        public FixedString128Bytes CrimeDescription;
        public FixedString512Bytes Briefing;

        /// <summary>Planned investigation length. Not yet enforced by any timer.</summary>
        public int InvestigationSeconds;

        /// <summary>Who is publicly known to have been present. Not who did it.</summary>
        public FixedList512Bytes<FixedString32Bytes> KnownCharacters;

        /// <summary>Shown in dev UI so you can confirm two machines hold the same case.</summary>
        public ulong Seed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Title);
            serializer.SerializeValue(ref CrimeDescription);
            serializer.SerializeValue(ref Briefing);
            serializer.SerializeValue(ref InvestigationSeconds);
            serializer.SerializeValue(ref Seed);

            // NGO has no serializer for FixedList, so write a count then the items.
            // Reading rebuilds the list from scratch rather than mutating in place.
            int count = KnownCharacters.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                KnownCharacters = default;
                for (int i = 0; i < count; i++)
                {
                    FixedString32Bytes name = default;
                    serializer.SerializeValue(ref name);
                    KnownCharacters.Add(name);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var name = KnownCharacters[i];
                    serializer.SerializeValue(ref name);
                }
            }
        }

        public static PublicCaseInfo Empty => new PublicCaseInfo
        {
            Title = "(no case)",
            CrimeDescription = default,
            Briefing = default,
            InvestigationSeconds = 0,
            KnownCharacters = default,
            Seed = 0,
        };
    }
}
