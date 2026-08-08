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
        InLocker = 3,      // handed in at the Evidence Locker. Terminal.
        InLabMachine = 4,  // loaded into the forensics machine, mid-analysis

        // Reserved, NOT implemented:
        // Destroyed = 5,
    }

    /// <summary>
    /// Whether the lab has finished with this item. The FOURTH dimension.
    ///
    /// WHY NOT EvidenceLegalState.Processed. Registration answers "is this in the
    /// court record"; processing answers "has the lab finished with it". They are
    /// independent questions with independent answers: a catering schedule registers
    /// without ever being processed, and a fingerprint card can finish processing
    /// and never be registered. Folding them together would make "processed but not
    /// yet registered" — the normal state of a useful forensic item — impossible to
    /// express. Same reasoning that gave custody InLocker rather than Registered.
    ///
    /// NotRequired is a first-class value, not a null: most evidence is paper, and
    /// "this item has no lab step" is a real answer rather than a missing one.
    /// </summary>
    public enum EvidenceProcessingState : byte
    {
        NotRequired = 0,   // paper. There is nothing for the lab to do.
        Unprocessed = 1,   // forensic, still redacted
        Processing = 2,    // in the machine, server clock running
        Processed = 3,     // result available to whoever collects or reviews it
    }

    /// <summary>
    /// Whether the evidence has been formally entered into the record. The THIRD
    /// dimension, and deliberately not folded into either of the other two.
    ///
    /// WHY NOT REUSE CUSTODY. "Registered" was originally reserved as a custody
    /// value, but custody answers WHERE THE PAPER IS and registration is a legal
    /// act, not a location. Overloading it would make two different questions share
    /// one field: an item in the locker but not yet registered, or registered and
    /// later moved, would both become unrepresentable. Custody gained
    /// <see cref="EvidenceCustody.InLocker"/> — a place — and this enum owns the
    /// legal question on its own.
    ///
    /// WHY NOT REUSE KNOWLEDGE. Knowledge is per-player and only ever grows.
    /// Registration is a single fact about the item, true for everyone at once.
    ///
    /// Later this grows to Processed / Admissible / Struck. Only the two below are
    /// implemented.
    /// </summary>
    public enum EvidenceLegalState : byte
    {
        Unregistered = 0,
        Registered = 1,

        // Reserved, NOT implemented:
        // Processed = 2, Admissible = 3, Struck = 4,
    }

    /// <summary>What happened to a piece of evidence. Chain-of-custody vocabulary.</summary>
    public enum CustodyEventType : byte
    {
        Discovered = 0,
        PickedUp = 1,
        Dropped = 2,
        DroppedOnDisconnect = 3,
        Registered = 4,
        LoadedIntoLab = 5,
        ProcessingComplete = 6,
        CollectedFromLab = 7,
    }

    /// <summary>
    /// One line in an evidence item's history. SERVER-OWNED: this type is not
    /// INetworkSerializable and lives only on <see cref="EvidenceInstance"/>, which
    /// is host memory. Admissibility, tampering and the end-of-match reveal will all
    /// need to ask "who touched this, when" — recording it now costs nothing and
    /// cannot be reconstructed later.
    /// </summary>
    public sealed class CustodyEvent
    {
        public CustodyEventType Type;
        public ulong ClientId = EvidenceInstance.NoCarrier;
        public PlayerTeam Team = PlayerTeam.None;
        public float Time;
        public UnityEngine.Vector3 Location;

        public override string ToString() =>
            $"{Time,7:F1}s  {Type,-20} client={(ClientId == EvidenceInstance.NoCarrier ? "-" : ClientId.ToString())}" +
            $"  team={Team}  at {Location}";
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

        // ---- forensic processing ----
        public EvidenceProcessingState Processing = EvidenceProcessingState.NotRequired;

        /// <summary>Seconds the lab needs, from the generator's FoundAt prose. 0 when not required.</summary>
        public float ProcessingSeconds;

        /// <summary>Everyone who has legitimately read the forensic result. Never shrinks.</summary>
        public readonly HashSet<ulong> ResultKnownBy = new();

        public bool RequiresProcessing => Processing != EvidenceProcessingState.NotRequired;
        public bool IsProcessed => Processing == EvidenceProcessingState.Processed;
        public bool IsInMachine => Custody == EvidenceCustody.InLabMachine;

        // ---- legal status ----
        public EvidenceLegalState LegalState = EvidenceLegalState.Unregistered;
        public ulong RegisteredByClientId = NoCarrier;
        public PlayerTeam RegisteredByTeam = PlayerTeam.None;
        public float RegisteredAtTime = -1f;

        /// <summary>
        /// Everything that has happened to this item, oldest first. Server-only.
        /// </summary>
        public readonly List<CustodyEvent> History = new();

        public const ulong NoCarrier = ulong.MaxValue;

        public bool IsFound => Knowledge != EvidenceKnowledge.Undiscovered;
        public bool IsCarried => Custody == EvidenceCustody.Carried;
        public bool IsOnTheFloor => Custody == EvidenceCustody.InWorld;
        public bool IsRegistered => LegalState == EvidenceLegalState.Registered;

        /// <summary>
        /// Registration is terminal in this prototype: once in the locker the item
        /// never returns to a hand or a floor. Every transition that would move it
        /// checks this.
        /// </summary>
        public bool IsLockedAway => Custody == EvidenceCustody.InLocker;

        /// <summary>
        /// The item is inside something — the locker or the lab machine — and cannot
        /// be materialised into the world by a rebuild, a disconnect or a test
        /// fixture. Without this, the same document could exist in the machine AND
        /// on the floor, which is precisely the duplication custody exists to
        /// prevent.
        /// </summary>
        public bool IsEnclosed =>
            Custody == EvidenceCustody.InLocker || Custody == EvidenceCustody.InLabMachine;

        public bool IsKnownBy(ulong clientId) => KnownBy.Contains(clientId);

        /// <summary>
        /// Into the machine. Leaves the carrier's hands in the same step, so a
        /// document is never both held and being analysed.
        /// </summary>
        public bool TryLoadIntoMachine(ulong clientId)
        {
            if (Custody != EvidenceCustody.Carried) return false;
            if (CarrierClientId != clientId) return false;
            if (Processing != EvidenceProcessingState.Unprocessed) return false;   // paper, or already done

            Custody = EvidenceCustody.InLabMachine;
            CarrierClientId = NoCarrier;
            Processing = EvidenceProcessingState.Processing;
            return true;
        }

        /// <summary>SERVER ONLY. The lab clock elapsed.</summary>
        public bool TryFinishProcessing()
        {
            if (Processing != EvidenceProcessingState.Processing) return false;
            Processing = EvidenceProcessingState.Processed;
            return true;
        }

        /// <summary>
        /// Back out of the machine into a pair of hands. Refused while the analysis
        /// is still running — the item is physically inside a working machine.
        /// </summary>
        public bool TryCollectFromMachine(ulong clientId)
        {
            if (Custody != EvidenceCustody.InLabMachine) return false;
            if (Processing == EvidenceProcessingState.Processing) return false;

            Custody = EvidenceCustody.Carried;
            CarrierClientId = clientId;
            GrantKnowledge(clientId);
            return true;
        }

        /// <summary>Reading the forensic result. Per-player, and only ever grows.</summary>
        public bool GrantResultKnowledge(ulong clientId) =>
            IsProcessed && ResultKnownBy.Add(clientId);

        public bool KnowsResult(ulong clientId) => ResultKnownBy.Contains(clientId);

        /// <summary>Appends to the history. The only way events are added.</summary>
        public void Record(CustodyEventType type, ulong clientId, PlayerTeam team,
                           float time, UnityEngine.Vector3 location)
        {
            History.Add(new CustodyEvent
            {
                Type = type,
                ClientId = clientId,
                Team = team,
                Time = time,
                Location = location,
            });
        }

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
            Record(CustodyEventType.Discovered, clientId, team, time, WorldPosition);
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
            // Only a loose item can be picked up. Still filed, already in somebody's
            // hands, or handed in at the locker, and the answer is no. The InWorld
            // test alone already excludes InLocker — this is not a second guard, it
            // is the same one, which is the point of custody being single-valued.
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

        /// <summary>
        /// Discovery reveals the item into the world at the container.
        ///
        /// Refuses once the item is in the locker. Without this a placement rebuild
        /// or a test fixture could quietly resurrect registered evidence back onto
        /// the floor, and the same document would exist twice in the record.
        /// </summary>
        public bool PlaceInWorld(UnityEngine.Vector3 position)
        {
            if (IsEnclosed) return false;

            Custody = EvidenceCustody.InWorld;
            CarrierClientId = NoCarrier;
            WorldPosition = position;
            return true;
        }

        /// <summary>
        /// The registration transition. SERVER ONLY, and the single place legal
        /// state can change.
        ///
        /// Takes the item out of the carrier's hands and into the locker in the same
        /// step, because those two facts must never disagree: a registered document
        /// still in someone's pocket is exactly the ambiguity this milestone exists
        /// to avoid.
        ///
        /// Knowledge is untouched. Everyone who read it still remembers it.
        /// </summary>
        public bool TryRegister(ulong clientId, PlayerTeam team, float time)
        {
            if (LegalState != EvidenceLegalState.Unregistered) return false;  // already done
            if (Custody != EvidenceCustody.Carried) return false;             // must be in hand
            if (CarrierClientId != clientId) return false;                    // and in YOUR hand
            if (!IsFound) return false;                                       // cannot register a rumour

            LegalState = EvidenceLegalState.Registered;
            RegisteredByClientId = clientId;
            RegisteredByTeam = team;
            RegisteredAtTime = time;

            Custody = EvidenceCustody.InLocker;
            CarrierClientId = NoCarrier;

            Record(CustodyEventType.Registered, clientId, team, time, WorldPosition);
            return true;
        }

        /// <summary>
        /// A carrier vanished. The item falls where they last stood rather than
        /// disappearing with them — evidence must never leave the building because
        /// somebody's wifi died.
        /// </summary>
        public bool ForceDrop(UnityEngine.Vector3 position)
        {
            if (IsEnclosed) return false;   // registered evidence never returns to the floor

            Custody = EvidenceCustody.InWorld;
            CarrierClientId = NoCarrier;
            WorldPosition = position;
            return true;
        }
    }

    /// <summary>
    /// What a player is told about a piece of evidence they have legitimately
    /// discovered or taken possession of. Sent to exactly one client.
    ///
    /// The record and nothing else: no perpetrator, no guilt, no proof-chain
    /// position, no hint as to whether this item matters.
    /// </summary>
    /// <summary>
    /// The PUBLIC fact that a registration happened. Broadcast to everyone.
    ///
    /// WHAT IS DELIBERATELY ABSENT: title, kind, description, container, relevance.
    /// "Item E-003 was registered by the Prosecution" tells the defence that the
    /// other side has something, which is fair and is half the tension of the game.
    /// "Catering schedule: places staff by slot" would hand them a document they
    /// never found, and there is no way to un-send it.
    ///
    /// The contents travel separately, in <see cref="EvidenceDiscovery"/>, to
    /// players who earned them. Keeping the two structs apart is what makes the
    /// leak impossible rather than merely avoided — there is no field here to put
    /// the contents in.
    /// </summary>
    public struct EvidenceRegistrationNotice : INetworkSerializable
    {
        public FixedString64Bytes EvidenceId;
        public byte Team;            // PlayerTeam of the registrant
        public float MatchTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref EvidenceId);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref MatchTime);
        }
    }

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
