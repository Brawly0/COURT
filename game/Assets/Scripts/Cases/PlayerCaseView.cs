using Unity.Collections;
using Unity.Netcode;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// What ONE player is allowed to know, sent only to that player.
    ///
    /// The rule this type exists to enforce: the defendant learns whether they are
    /// actually guilty; nobody else does. In COURT the defendant lying is the
    /// player's choice, so they must know the truth — and if that fact reached the
    /// whole lobby the game would be over before it started.
    ///
    /// Delivered by a targeted ClientRpc rather than a NetworkVariable, because a
    /// NetworkVariable is readable by every client by design. One player, one
    /// message, no shared surface to sniff.
    /// </summary>
    public struct PlayerCaseView : INetworkSerializable
    {
        public PlayerRole Role;

        /// <summary>Briefing only this player sees.</summary>
        public FixedString512Bytes PrivateBriefing;

        /// <summary>True only for the defendant. Gates the field below.</summary>
        public bool KnowsOwnGuilt;

        /// <summary>
        /// MEANINGLESS unless KnowsOwnGuilt is true. The host sends `false` to
        /// everyone else regardless of the real answer, so the packet itself
        /// carries no signal — an investigator who inspects this sees the same
        /// bytes whether the defendant is guilty or not.
        /// </summary>
        public bool IsActuallyGuilty;

        /// <summary>Facts this player has been cleared to hold at the start.</summary>
        public FixedList512Bytes<FixedString128Bytes> PermittedFacts;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            byte role = (byte)Role;
            serializer.SerializeValue(ref role);
            Role = (PlayerRole)role;

            serializer.SerializeValue(ref PrivateBriefing);
            serializer.SerializeValue(ref KnowsOwnGuilt);
            serializer.SerializeValue(ref IsActuallyGuilty);

            // NGO has no serializer for FixedList: count, then items.
            int count = PermittedFacts.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                PermittedFacts = default;
                for (int i = 0; i < count; i++)
                {
                    FixedString128Bytes fact = default;
                    serializer.SerializeValue(ref fact);
                    PermittedFacts.Add(fact);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var fact = PermittedFacts[i];
                    serializer.SerializeValue(ref fact);
                }
            }
        }

        public static PlayerCaseView Empty => new PlayerCaseView
        {
            Role = PlayerRole.Unassigned,
            PrivateBriefing = default,
            KnowsOwnGuilt = false,
            IsActuallyGuilty = false,
            PermittedFacts = default,
        };
    }
}
