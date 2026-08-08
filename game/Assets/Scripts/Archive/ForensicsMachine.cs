using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Interaction;

namespace CaseClosed.Game.Archive
{
    public enum MachineState : byte
    {
        Idle = 0,
        Processing = 1,
        Complete = 2,
    }

    /// <summary>
    /// The forensics processor. One item at a time, on the server's clock.
    ///
    /// WHAT IT REPLICATES: a state, a label, and when the run ends. That is the
    /// complete list. A client reading every field learns "the machine is busy with
    /// an unprocessed fingerprint card for another 40 seconds", which is exactly what
    /// anyone standing in the room can see. The RESULT is never replicated — it goes
    /// to one player, once, when they legitimately collect or review it.
    ///
    /// THE CLIENT NEVER NAMES THE EVIDENCE. It asks to use this machine; the server
    /// resolves what that player is carrying. And the client never reports elapsed
    /// time — the server records the end time and decides completion itself, so a
    /// modified client cannot finish a 90-second analysis early.
    /// </summary>
    public class ForensicsMachine : NetworkInteractable
    {
        [Header("Timing")]
        [Tooltip("Use the generator's real durations (90s / 120s). Off for development.")]
        public bool UseProductionTiming = false;

        [Tooltip("Multiplier applied when production timing is off. 0.055 turns 90s into ~5s. " +
                 "The production value is never overwritten — only scaled at run time.")]
        public float DevelopmentTimeScale = 0.055f;

        // ---- replicated, and deliberately harmless ----
        private readonly NetworkVariable<MachineState> _state = new(
            MachineState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>The REDACTED title. Never the forensic result.</summary>
        private readonly NetworkVariable<FixedString128Bytes> _label = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _endsAtServerTime = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _totalSeconds = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>What is inside. SERVER ONLY — never replicated, never sent.</summary>
        private string _loadedEvidenceId;

        public MachineState State => _state.Value;
        public string Label => _label.Value.ToString();
        public string ServerLoadedId => IsServer ? _loadedEvidenceId : null;

        /// <summary>Seconds left, for the display. Clamped so it never reads negative.</summary>
        public float SecondsRemaining => _state.Value == MachineState.Processing
            ? Mathf.Max(0f, _endsAtServerTime.Value - Time.time)
            : 0f;

        public float Progress01 => _totalSeconds.Value <= 0f ? 0f
            : Mathf.Clamp01(1f - SecondsRemaining / _totalSeconds.Value);

        /// <summary>Busy is still interactable — you may be collecting, not loading.</summary>
        public override bool IsAvailable => true;

        public override string PromptFor(ulong clientId)
        {
            switch (_state.Value)
            {
                case MachineState.Processing:
                    return $"Processing {Label}  {Mathf.CeilToInt(SecondsRemaining)}s";

                case MachineState.Complete:
                    return "Collect Processed Evidence";

                default:
                    var director = EvidenceCustodyDirector.Instance;
                    return director != null && director.LocalIsCarrying
                        ? "Load Evidence"
                        : "Forensics Machine";
            }
        }

        /// <summary>Loading takes a moment; collecting is immediate.</summary>
        public override float HoldDurationFor(ulong clientId) =>
            _state.Value == MachineState.Complete ? 0.2f : HoldDuration;

        public override InteractionOutcome ServerValidate(ulong clientId)
        {
            var director = EvidenceCustodyDirector.Instance;
            if (director == null) return InteractionOutcome.RejectedUnavailable;

            // Collecting a finished run.
            if (_state.Value == MachineState.Complete)
            {
                // Hands must be free — carrying two items is not representable.
                return director.CarriedBy(clientId) == null
                    ? InteractionOutcome.Accepted
                    : InteractionOutcome.RejectedUnavailable;
            }

            // A run in progress is nobody's to interrupt.
            if (_state.Value == MachineState.Processing) return InteractionOutcome.RejectedBusy;

            // Loading. The server reads what they hold; the client named nothing.
            var carried = director.CarriedBy(clientId);
            if (carried == null) return InteractionOutcome.RejectedUnavailable;

            // Compatibility is DERIVED from the generator's own FoundAt data via
            // ArchiveEvidenceIndex — never a hard-coded list of ids.
            if (!carried.RequiresProcessing) return InteractionOutcome.RejectedUnavailable;
            if (carried.Processing != EvidenceProcessingState.Unprocessed)
                return InteractionOutcome.RejectedUnavailable;

            return InteractionOutcome.Accepted;
        }

        public override void ServerExecute(ulong clientId)
        {
            if (!IsServer) return;

            if (_state.Value == MachineState.Complete) { ServerCollect(clientId); return; }
            if (_state.Value == MachineState.Processing) return;

            ServerLoad(clientId);
        }

        private void ServerLoad(ulong clientId)
        {
            var director = EvidenceCustodyDirector.Instance;
            var instance = director?.CarriedBy(clientId);
            if (instance == null) return;

            float production = instance.ProcessingSeconds;
            float effective = UseProductionTiming
                ? production
                : Mathf.Max(0.5f, production * DevelopmentTimeScale);

            if (!director.ServerLoadIntoMachine(clientId, instance, transform.position)) return;

            _loadedEvidenceId = instance.EvidenceId;
            _label.Value = Clip(director.RedactedTitleOf(instance.EvidenceId));
            _totalSeconds.Value = effective;
            _endsAtServerTime.Value = Time.time + effective;
            _state.Value = MachineState.Processing;

            Debug.Log($"[Lab] Loaded {_loadedEvidenceId} — {production}s production, " +
                      $"running {effective:F1}s{(UseProductionTiming ? "" : " (development timing)")}.");
        }

        private void ServerCollect(ulong clientId)
        {
            var director = EvidenceCustodyDirector.Instance;
            if (director == null || string.IsNullOrEmpty(_loadedEvidenceId)) return;

            if (!director.ServerCollectFromMachine(clientId, _loadedEvidenceId, transform.position)) return;

            _loadedEvidenceId = null;
            _label.Value = default;
            _state.Value = MachineState.Idle;
            _totalSeconds.Value = 0f;
            _endsAtServerTime.Value = 0f;
        }

        /// <summary>
        /// The server's clock and nobody else's. Runs regardless of whether the
        /// loading player is still in the room — walking away does not pause an
        /// analysis, which is what makes the machine a contested resource rather
        /// than a place you have to stand.
        /// </summary>
        private void Update()
        {
            if (!IsServer) return;
            if (_state.Value != MachineState.Processing) return;
            if (Time.time < _endsAtServerTime.Value) return;

            _state.Value = MachineState.Complete;
            EvidenceCustodyDirector.Instance?.ServerFinishProcessing(_loadedEvidenceId, transform.position);

            Debug.Log($"[Lab] Processing complete: {_loadedEvidenceId}. Result awaits collection.");
        }

        /// <summary>
        /// Empties the machine without granting anything. Called on case reset, so a
        /// previous case's sample cannot sit in the machine holding an id that has
        /// just been recycled — the stale-body bug, one milestone later.
        /// </summary>
        public void ServerResetMachine()
        {
            if (!IsServer) return;
            _loadedEvidenceId = null;
            _label.Value = default;
            _state.Value = MachineState.Idle;
            _totalSeconds.Value = 0f;
            _endsAtServerTime.Value = 0f;
            ServerForceRelease();
        }

        private static FixedString128Bytes Clip(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;
            return value.Length <= 40 ? value : value.Substring(0, 40);
        }
    }
}
