using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;

namespace CaseClosed.Game.Interaction
{
    /// <summary>
    /// The one contract every interactable in COURT implements — shelves, doors,
    /// evidence, terminals, the gavel. Subclasses override two methods and nothing
    /// else: what happens on the server, and what the prompt says.
    ///
    /// WHY A BASE CLASS RATHER THAN AN INTERFACE: the parts that must not vary —
    /// distance checks, the exclusivity lock, hold timing, release on disconnect —
    /// live here and cannot be forgotten by an implementer. An interface would let
    /// every future object re-invent locking, and one of them would get it wrong.
    ///
    /// IDENTITY IS NetworkObjectId. The client sends that number and nothing else;
    /// the server resolves it through NGO's own spawn table. A client cannot
    /// fabricate a target because it does not send an object, only a claim about one.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetworkInteractable : NetworkBehaviour
    {
        /// <summary>Sentinel for "nobody holds this". 0 is a real client id (the host).</summary>
        public const ulong NoOwner = ulong.MaxValue;

        [Header("Interaction")]
        [Tooltip("Shown next to the key prompt.")]
        public string Prompt = "Use";

        [Tooltip("Seconds the key must be held. 0 = instant.")]
        public float HoldDuration = 0f;

        [Tooltip("Furthest the player may be, in metres. Enforced on the server.")]
        public float MaxDistance = 3f;

        [Tooltip("Require an unobstructed line from the player's eyes to this object.")]
        public bool RequiresLineOfSight = true;

        [Header("Permissions")]
        [Tooltip("None = anyone may use it. Otherwise only this team.")]
        public PlayerTeam RequiredTeam = PlayerTeam.None;

        [Tooltip("Unassigned = any role. Otherwise only this role.")]
        public PlayerRole RequiredRole = PlayerRole.Unassigned;

        /// <summary>
        /// Who currently holds this object, replicated so every client's prompt can
        /// say "busy" without asking the server.
        /// </summary>
        protected readonly NetworkVariable<ulong> LockOwner = new(
            NoOwner, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsLocked => LockOwner.Value != NoOwner;
        public bool IsLockedByOther(ulong clientId) => IsLocked && LockOwner.Value != clientId;

        /// <summary>Who holds it, or NoOwner. Public so disconnect cleanup can sweep.</summary>
        public ulong LockedBy => LockOwner.Value;

        public bool IsHold => HoldDuration > 0.01f;

        /// <summary>Override for objects that go out of service (a door already open, a shelf emptied).</summary>
        public virtual bool IsAvailable => true;

        /// <summary>Prompt as this specific client should see it. Override for stateful text.</summary>
        public virtual string PromptFor(ulong clientId) => Prompt;

        // ------------------------------------------------------------------
        // server: the parts subclasses fill in
        // ------------------------------------------------------------------

        /// <summary>
        /// Extra server-side checks beyond the shared ones. Return Accepted to allow.
        /// Runs AFTER distance, line of sight, availability, lock and permission.
        /// </summary>
        public virtual InteractionOutcome ServerValidate(ulong clientId) => InteractionOutcome.Accepted;

        /// <summary>The actual effect. Server only. Called once per successful interaction.</summary>
        public abstract void ServerExecute(ulong clientId);

        /// <summary>Optional cleanup when a hold is abandoned. Server only.</summary>
        public virtual void ServerCancel(ulong clientId) { }

        // ------------------------------------------------------------------
        // locking
        // ------------------------------------------------------------------

        /// <summary>
        /// Claims the object. Server only.
        ///
        /// Instant interactions do not lock — two players pressing a button in the
        /// same frame is fine. Only holds are exclusive, because that is where the
        /// contest actually is: two people searching one shelf.
        /// </summary>
        public bool ServerTryLock(ulong clientId)
        {
            if (!IsServer) return false;
            if (IsLocked) return LockOwner.Value == clientId;

            LockOwner.Value = clientId;
            return true;
        }

        public void ServerReleaseLock(ulong clientId)
        {
            if (!IsServer) return;
            if (LockOwner.Value == clientId) LockOwner.Value = NoOwner;
        }

        /// <summary>Unconditional release, for disconnects and despawns.</summary>
        public void ServerForceRelease()
        {
            if (IsServer) LockOwner.Value = NoOwner;
        }

        /// <summary>Where the interaction ray should land. Override for large objects.</summary>
        public virtual Vector3 InteractionPoint =>
            TryGetComponent<Collider>(out var collider) ? collider.bounds.center : transform.position;

        public override void OnNetworkDespawn()
        {
            // A despawning object must not leave a client believing it holds a lock.
            if (IsServer) LockOwner.Value = NoOwner;
        }
    }
}
