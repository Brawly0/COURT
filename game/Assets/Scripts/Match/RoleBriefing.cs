using Unity.Collections;
using Unity.Netcode;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// One player's briefing card. Sent to exactly one client, never broadcast.
    ///
    /// This is a TRANSPORT model, not a view of the truth. It holds only strings
    /// that have already been through the filter — there is no reference back to
    /// CompleteCaseTruth, so a mistake here cannot widen into a leak.
    ///
    /// Fixed-size strings because NGO needs a known size; the factory clips by BYTE
    /// budget, since the generator's prose contains multi-byte characters.
    /// </summary>
    public struct RoleBriefing : INetworkSerializable
    {
        public PlayerRole Role;
        public PlayerTeam Team;

        /// <summary>What this seat is trying to achieve.</summary>
        public FixedString512Bytes Objective;

        /// <summary>What this seat can do that others cannot.</summary>
        public FixedString512Bytes Ability;

        /// <summary>
        /// Role-specific private text. For the defendant this includes their own
        /// timeline; for everyone else it is guidance, never hidden truth.
        /// </summary>
        public FixedString4096Bytes PrivateInformation;

        /// <summary>True only for the defendant. Gates the flag below.</summary>
        public bool KnowsOwnGuilt;

        /// <summary>
        /// Meaningless unless KnowsOwnGuilt. Sent as false to everyone else
        /// regardless of the real answer, so the bytes carry no signal.
        /// </summary>
        public bool IsActuallyGuilty;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            byte role = (byte)Role;
            serializer.SerializeValue(ref role);
            Role = (PlayerRole)role;

            byte team = (byte)Team;
            serializer.SerializeValue(ref team);
            Team = (PlayerTeam)team;

            serializer.SerializeValue(ref Objective);
            serializer.SerializeValue(ref Ability);
            serializer.SerializeValue(ref PrivateInformation);
            serializer.SerializeValue(ref KnowsOwnGuilt);
            serializer.SerializeValue(ref IsActuallyGuilty);
        }

        public static RoleBriefing Empty => new RoleBriefing
        {
            Role = PlayerRole.Unassigned,
            Team = PlayerTeam.None,
            Objective = default,
            Ability = default,
            PrivateInformation = default,
            KnowsOwnGuilt = false,
            IsActuallyGuilty = false,
        };
    }
}
