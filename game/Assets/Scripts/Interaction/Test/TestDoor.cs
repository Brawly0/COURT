using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Interaction.Test
{
    /// <summary>
    /// TEST OBJECT — instant toggle whose visible state is replicated.
    ///
    /// The open flag drives the transform on EVERY machine, not just the server's.
    /// Moving it server-side only is the classic mistake here: it looks right to the
    /// host and stays shut for everyone else.
    /// </summary>
    public class TestDoor : NetworkInteractable
    {
        [Tooltip("Local offset applied when open.")]
        public Vector3 OpenOffset = new Vector3(0f, 2.9f, 0f);

        [Tooltip("Seconds to slide between states.")]
        public float SlideSeconds = 0.35f;

        private readonly NetworkVariable<bool> _open = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Vector3 _closedPosition;
        private float _blend;

        public bool IsOpen => _open.Value;

        public override string PromptFor(ulong clientId) => _open.Value ? "Close Door" : "Open Door";

        private void Awake() => _closedPosition = transform.localPosition;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Snap to the current state on join, so a late joiner does not watch the
            // door slide open on its own.
            _blend = _open.Value ? 1f : 0f;
            ApplyBlend();
        }

        public override void ServerExecute(ulong clientId)
        {
            _open.Value = !_open.Value;
            Debug.Log($"[Interact] Door {(_open.Value ? "opened" : "closed")} by client {clientId}.");
        }

        private void Update()
        {
            float goal = _open.Value ? 1f : 0f;
            if (Mathf.Approximately(_blend, goal)) return;

            _blend = Mathf.MoveTowards(_blend, goal, Time.deltaTime / Mathf.Max(0.01f, SlideSeconds));
            ApplyBlend();
        }

        private void ApplyBlend() => transform.localPosition = _closedPosition + OpenOffset * _blend;
    }
}
