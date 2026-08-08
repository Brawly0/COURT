using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Interaction;
using CaseClosed.Game.Prototype;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// The folder you can actually pick up.
    ///
    /// WHAT IT REPLICATES: an EvidenceId, a custody state, a carrier id, and a
    /// position. That is the complete list. No title, no contents, no relevance, no
    /// case truth — a client that reads every field of this component learns only
    /// that "item E-003 is being carried by player 2", which is exactly what it can
    /// already see by looking across the room.
    ///
    /// The description lives on the server and is sent, once, to a player who has
    /// legitimately taken possession.
    ///
    /// CARRYING IS NOT PHYSICS. While carried the object is not simulated or
    /// re-parented across the network; every client simply draws it at the carrier's
    /// socket. The server owns one authoritative fact — who holds it — and the
    /// visuals follow from that. Networking a carried rigidbody would be more code
    /// and less certainty.
    /// </summary>
    public class PhysicalEvidence : NetworkInteractable
    {
        [Header("Carry")]
        [Tooltip("Fallback offset from the carrier's origin, used only if their rig has " +
                 "no PlayerCarrySocket. A rig with a socket ignores this entirely.")]
        public Vector3 CarryOffset = new Vector3(0f, 1.22f, 0.42f);

        /// <summary>
        /// Every spawned body on this machine. Lets any client answer "is player N
        /// carrying something" without a lookup through the server-side director,
        /// which does not exist on a client.
        /// </summary>
        private static readonly List<PhysicalEvidence> Active = new();

        /// <summary>
        /// The item this client believes the given player is holding, or null.
        /// Derived purely from replicated custody — no extra network traffic, and it
        /// cannot disagree with the folder it describes.
        /// </summary>
        public static PhysicalEvidence FindCarriedBy(ulong clientId)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var body = Active[i];
                if (body == null || !body.InUse) continue;
                if (body.Custody == EvidenceCustody.Carried && body.CarrierClientId == clientId)
                    return body;
            }
            return null;
        }

        private readonly NetworkVariable<FixedString64Bytes> _evidenceId = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<EvidenceCustody> _custody = new(
            EvidenceCustody.InContainer,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _carrier = new(
            EvidenceInstance.NoCarrier,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _worldPosition = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Renderer[] _renderers;
        private Collider _collider;

        public string EvidenceId => _evidenceId.Value.ToString();
        public EvidenceCustody Custody => _custody.Value;
        public ulong CarrierClientId => _carrier.Value;

        /// <summary>Unused pool entries have no id and stay invisible.</summary>
        public bool InUse => !string.IsNullOrEmpty(EvidenceId);

        /// <summary>
        /// One line per body in use, for the developer readout. Deliberately reports
        /// the RESOLVED socket rather than just the carrier id: "carrier 1, socket
        /// null" is the signature of a rig built before the socket existed, and is
        /// otherwise indistinguishable from a custody bug.
        /// </summary>
        public static List<string> DebugSnapshot()
        {
            var lines = new List<string>();
            for (int i = 0; i < Active.Count; i++)
            {
                var body = Active[i];
                if (body == null || !body.InUse) continue;

                string socket = body.Custody == EvidenceCustody.Carried
                    ? (body.FindCarrierSocket()?.Attachment.name ?? "NULL")
                    : "-";

                string carrier = body.Custody == EvidenceCustody.Carried
                    ? body.CarrierClientId.ToString()
                    : "-";

                lines.Add($"{body.EvidenceId}  custody={body.Custody}  carrier={carrier}\n" +
                          $"    socket={socket}  pos={body.transform.position}");
            }
            return lines;
        }

        /// <summary>Only a loose item on the floor can be picked up.</summary>
        public override bool IsAvailable => InUse && _custody.Value == EvidenceCustody.InWorld;

        public override string PromptFor(ulong clientId) => "Pick Up Evidence";

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _collider = GetComponent<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!Active.Contains(this)) Active.Add(this);

            _custody.OnValueChanged += (_, __) => ApplyVisibility();
            _evidenceId.OnValueChanged += (_, __) => ApplyVisibility();
            ApplyVisibility();
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Pickup runs through the ordinary interaction path, so distance, line of
        /// sight, player state and the exclusivity lock are all already enforced by
        /// the time this is called. Custody itself is decided by the director.
        /// </summary>
        public override void ServerExecute(ulong clientId)
        {
            EvidenceCustodyDirector.Instance?.ServerRequestPickup(this, clientId);
        }

        /// <summary>Server-side assignment when evidence is revealed. Pool entries are reused.</summary>
        public void ServerAssign(string evidenceId, Vector3 position)
        {
            if (!IsServer) return;

            _evidenceId.Value = evidenceId;
            _custody.Value = EvidenceCustody.InWorld;
            _carrier.Value = EvidenceInstance.NoCarrier;
            _worldPosition.Value = position;
            transform.position = position;
        }

        public void ServerSetCarried(ulong carrier)
        {
            if (!IsServer) return;
            _custody.Value = EvidenceCustody.Carried;
            _carrier.Value = carrier;
        }

        public void ServerSetInWorld(Vector3 position)
        {
            if (!IsServer) return;
            _custody.Value = EvidenceCustody.InWorld;
            _carrier.Value = EvidenceInstance.NoCarrier;
            _worldPosition.Value = position;
            transform.position = position;
        }

        /// <summary>
        /// Inside the machine: invisible and non-solid, but still assigned. Distinct
        /// from ServerRelease, which returns the body to the pool — this item still
        /// exists and is coming back out.
        /// </summary>
        public void ServerStow()
        {
            if (!IsServer) return;
            _custody.Value = EvidenceCustody.InLabMachine;
            _carrier.Value = EvidenceInstance.NoCarrier;
        }

        public void ServerRelease()
        {
            if (!IsServer) return;
            _evidenceId.Value = default;
            _custody.Value = EvidenceCustody.InContainer;
            _carrier.Value = EvidenceInstance.NoCarrier;
        }

        /// <summary>
        /// Runs on every machine. A carried item is drawn at its carrier's socket;
        /// a loose one sits where the server says. No client ever decides custody —
        /// it only draws the consequence.
        ///
        /// LATEUPDATE, NOT UPDATE, and this matters more than it looks. The socket
        /// hangs off an animated chest. Sampling it in Update reads the pose the
        /// Animator wrote LAST frame, so the folder trails the body by one frame and
        /// visibly swims during fast turns. LateUpdate runs after animation, so the
        /// folder sits exactly where the hands are.
        ///
        /// Snapped, not smoothed, for the same reason: the socket is already attached
        /// to the body, so any easing here is pure lag against a target that is not
        /// moving relative to the hands.
        /// </summary>
        private void LateUpdate()
        {
            if (!InUse) return;

            if (_custody.Value == EvidenceCustody.Carried)
            {
                var socket = FindCarrierSocket();
                if (socket == null) return;

                bool localView = IsLocalCarrier();
                socket.GetAttachPose(localView, out Vector3 position, out Quaternion rotation);

                transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                transform.position = _worldPosition.Value;
            }
        }

        /// <summary>
        /// True when this machine's own player is the carrier — the one case where a
        /// first-person view offset would apply. Always false in third person today.
        /// </summary>
        private bool IsLocalCarrier()
        {
            var manager = NetworkManager.Singleton;
            return manager != null && manager.IsClient && manager.LocalClientId == _carrier.Value;
        }

        /// <summary>
        /// Resolves the carrier's carry socket. Falls back to a plain offset from the
        /// player root, so a rig without a socket still holds the item somewhere
        /// sensible rather than dropping it at the world origin.
        /// </summary>
        private PlayerCarrySocket FindCarrierSocket()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return null;
            if (!manager.ConnectedClients.TryGetValue(_carrier.Value, out var client)) return null;

            var player = client.PlayerObject;
            if (player == null) return null;

            var socket = player.GetComponentInChildren<PlayerCarrySocket>();
            if (socket != null) return socket;

            // No socket on this rig. Synthesise one so the item is still held.
            if (_fallbackSocket == null)
            {
                _fallbackSocket = player.gameObject.AddComponent<PlayerCarrySocket>();
                _fallbackSocket.PositionOffset = CarryOffset;
                Debug.LogWarning("[Carry] Carrier has no PlayerCarrySocket — " +
                                 "falling back to a root offset. Rebuild the character prefab.");
            }
            return _fallbackSocket;
        }

        private PlayerCarrySocket _fallbackSocket;

        /// <summary>
        /// Unassigned pool entries, and items still filed away, are invisible and
        /// non-solid. A carried item stays visible — other players seeing what you
        /// are holding is the point.
        /// </summary>
        private void ApplyVisibility()
        {
            // Filed away, locked away, or inside the machine: all invisible, and none
            // of them pickable. Custody being single-valued is what lets one test
            // cover every "not out here" case.
            bool visible = InUse &&
                           _custody.Value != EvidenceCustody.InContainer &&
                           _custody.Value != EvidenceCustody.InLocker &&
                           _custody.Value != EvidenceCustody.InLabMachine;

            if (_renderers != null)
                foreach (var renderer in _renderers) if (renderer != null) renderer.enabled = visible;

            // Collider off while carried, so it cannot block its own carrier or be
            // targeted for a second pickup.
            if (_collider != null)
                _collider.enabled = visible && _custody.Value == EvidenceCustody.InWorld;
        }
    }
}
