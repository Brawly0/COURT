using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Interaction;

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
        [Tooltip("Local offset from the carrier's origin where the item is drawn.")]
        public Vector3 CarryOffset = new Vector3(0.35f, 1.15f, 0.45f);

        [Tooltip("How quickly the item settles into a carrier's hands.")]
        public float FollowSharpness = 18f;

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
            _custody.OnValueChanged += (_, __) => ApplyVisibility();
            _evidenceId.OnValueChanged += (_, __) => ApplyVisibility();
            ApplyVisibility();
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
        /// </summary>
        private void Update()
        {
            if (!InUse) return;

            if (_custody.Value == EvidenceCustody.Carried)
            {
                var carrier = FindCarrierTransform();
                if (carrier == null) return;

                Vector3 target = carrier.TransformPoint(CarryOffset);
                transform.position = Vector3.Lerp(transform.position, target,
                    1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
                transform.rotation = Quaternion.Slerp(transform.rotation, carrier.rotation,
                    1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            }
            else
            {
                transform.position = _worldPosition.Value;
            }
        }

        private Transform FindCarrierTransform()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return null;
            if (!manager.ConnectedClients.TryGetValue(_carrier.Value, out var client)) return null;
            return client.PlayerObject != null ? client.PlayerObject.transform : null;
        }

        /// <summary>
        /// Unassigned pool entries, and items still filed away, are invisible and
        /// non-solid. A carried item stays visible — other players seeing what you
        /// are holding is the point.
        /// </summary>
        private void ApplyVisibility()
        {
            bool visible = InUse && _custody.Value != EvidenceCustody.InContainer;

            if (_renderers != null)
                foreach (var renderer in _renderers) if (renderer != null) renderer.enabled = visible;

            // Collider off while carried, so it cannot block its own carrier or be
            // targeted for a second pickup.
            if (_collider != null)
                _collider.enabled = visible && _custody.Value == EvidenceCustody.InWorld;
        }
    }
}
