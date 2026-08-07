using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// What is KNOWN about a piece of evidence. Nothing to do with who is holding it.
    ///
    /// Knowledge is monotonic: once a player has read a document, dropping the folder
    /// does not erase their memory of it. That is why this is separate from custody —
    /// collapsing the two would mean handing over a file made you forget it.
    /// </summary>
    public enum EvidenceKnowledge : byte
    {
        Undiscovered = 0,
        Found = 1,

        // Reserved, NOT implemented:
        // Processed = 2, Registered = 3, Admissible = 4,
    }

    /// <summary>
    /// Where the PHYSICAL item is. Nothing to do with who understands it.
    ///
    /// Exactly one of these is true at any moment, which is what makes duplication
    /// impossible: an item cannot be in a drawer and in a hand.
    /// </summary>
    public enum EvidenceCustody : byte
    {
        InContainer = 0,   // still filed, not yet revealed
        InWorld = 1,       // lying somewhere, free to pick up
        Carried = 2,       // in a specific player's hands

        // Reserved, NOT implemented:
        // Registered = 3, Destroyed = 4,
    }

    /// <summary>
    /// Server-side record of one evidence item. Host memory only — deliberately NOT
    /// INetworkSerializable, so it cannot be placed in an RPC.
    ///
    /// Carries the two dimensions independently:
    ///   Knowledge — who has read it (a set; several people may know the same thing)
    ///   Custody   — where the paper is (single-valued; only one place at a time)
    /// </summary>
    public sealed class EvidenceInstance
    {
        public string EvidenceId;
        public IndexedEvidence Source;

        // ---- knowledge ----
        public EvidenceKnowledge Knowledge = EvidenceKnowledge.Undiscovered;

        /// <summary>Everyone who has legitimately read this. Never shrinks.</summary>
        public readonly HashSet<ulong> KnownBy = new();

        public ulong FirstFoundByClientId;
        public PlayerTeam FirstFoundByTeam;
        public float FoundAtTime = -1f;
        public int FoundInContainer = -1;

        // ---- custody ----
        public EvidenceCustody Custody = EvidenceCustody.InContainer;
        public ulong CarrierClientId = NoCarrier;
        public UnityEngine.Vector3 WorldPosition;

        public const ulong NoCarrier = ulong.MaxValue;

        public bool IsFound => Knowledge != EvidenceKnowledge.Undiscovered;
        public bool IsCarried => Custody == EvidenceCustody.Carried;
        public bool IsOnTheFloor => Custody == EvidenceCustody.InWorld;

        public bool IsKnownBy(ulong clientId) => KnownBy.Contains(clientId);

        /// <summary>
        /// First discovery. One-way; returns false if already found, which is the
        /// guard against a replayed search paying out twice.
        /// </summary>
        public bool TryMarkFound(ulong clientId, PlayerTeam team, int containerIndex, float time)
        {
            if (Knowledge != EvidenceKnowledge.Undiscovered) return false;

            Knowledge = EvidenceKnowledge.Found;
            FirstFoundByClientId = clientId;
            FirstFoundByTeam = team;
            FoundInContainer = containerIndex;
            FoundAtTime = time;

            KnownBy.Add(clientId);
            return true;
        }

        /// <summary>
        /// Adds a reader. Called when someone legitimately takes possession — the
        /// prototype rule is that holding a document lets you read it.
        /// </summary>
        public void GrantKnowledge(ulong clientId) => KnownBy.Add(clientId);

        // ---- custody transitions, all server-side ----

        public bool TryPickUp(ulong clientId)
        {
            // Only a loose item can be picked up. Still filed, or already in
            // somebody's hands, and the answer is no.
            if (Custody != EvidenceCustody.InWorld) return false;

            Custody = EvidenceCustody.Carried;
            CarrierClientId = clientId;
            GrantKnowledge(clientId);
            return true;
        }

        /// <summary>Only the actual carrier may drop it.</summary>
        public bool TryDrop(ulong clientId, UnityEngine.Vector3 position)
        {
            if (Custody != EvidenceCustody.Carried) return false;
            if (CarrierClientId != clientId) return false;

            Custody = EvidenceCustody.InWorld;
            CarrierClientId = NoCarrier;
            WorldPosition = position;
            return true;
        }

        /// <summary>Discovery reveals the item into the world at the container.</summary>
        public void PlaceInWorld(UnityEngine.Vector3 position)
        {
            Custody = EvidenceCustody.InWorld;
            CarrierClientId = NoCarrier;
            WorldPosition = position;
        }

        /// <summary>
        /// A carrier vanished. The item falls where they last stood rather than
        /// disappearing with them — evidence must never leave the building because
        /// somebody's wifi died.
        /// </summary>
        public void ForceDrop(UnityEngine.Vector3 position)
        {
            Custody = EvidenceCustody.InWorld;
            CarrierClientId = NoCarrier;
            WorldPosition = position;
        }
    }

    /// <summary>
    /// What a player is told about a piece of evidence they have legitimately
    /// discovered or taken possession of. Sent to exactly one client.
    ///
    /// The record and nothing else: no perpetrator, no guilt, no proof-chain
    /// position, no hint as to whether this item matters.
    /// </summary>
    public struct EvidenceDiscovery : INetworkSerializable
    {
        public FixedString64Bytes EvidenceId;
        public FixedString128Bytes Title;
        public FixedString64Bytes Kind;
        public FixedString512Bytes Description;
        public int ContainerIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref EvidenceId);
            serializer.SerializeValue(ref Title);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Description);
            serializer.SerializeValue(ref ContainerIndex);
        }
    }
}
