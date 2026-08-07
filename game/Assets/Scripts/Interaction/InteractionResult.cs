using Unity.Collections;
using Unity.Netcode;

namespace CaseClosed.Game.Interaction
{
    /// <summary>
    /// Why an interaction was accepted or refused.
    ///
    /// Deliberately coarse. The player is told "that is busy", never "that is busy
    /// because the defence attorney is searching it" — a refusal reason is a channel
    /// like any other, and this one would leak who is where. Detailed reasons go to
    /// the server log only.
    /// </summary>
    public enum InteractionOutcome : byte
    {
        Accepted = 0,
        Started = 1,          // hold interaction began
        Completed = 2,
        Cancelled = 3,

        RejectedUnknownTarget = 10,
        RejectedTooFar = 11,
        RejectedNoLineOfSight = 12,
        RejectedBusy = 13,
        RejectedUnavailable = 14,
        RejectedWrongRole = 15,
        RejectedPlayerState = 16,
        RejectedNotOwner = 17,
    }

    /// <summary>
    /// A server verdict, sent back to the one client that asked.
    ///
    /// Carries no case data and no other player's identity — only the object, the
    /// verdict, and a short line of text safe to put on screen.
    /// </summary>
    public struct InteractionResponse : INetworkSerializable
    {
        public ulong TargetId;
        public InteractionOutcome Outcome;
        public FixedString128Bytes Message;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TargetId);

            byte outcome = (byte)Outcome;
            serializer.SerializeValue(ref outcome);
            Outcome = (InteractionOutcome)outcome;

            serializer.SerializeValue(ref Message);
        }

        public static InteractionResponse Reject(ulong targetId, InteractionOutcome outcome, string message)
            => new InteractionResponse { TargetId = targetId, Outcome = outcome, Message = Clip(message) };

        public static InteractionResponse Ok(ulong targetId, InteractionOutcome outcome, string message)
            => new InteractionResponse { TargetId = targetId, Outcome = outcome, Message = Clip(message) };

        /// <summary>Byte-safe truncation; FixedString counts bytes, not characters.</summary>
        private static FixedString128Bytes Clip(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;
            if (System.Text.Encoding.UTF8.GetByteCount(value) <= 120) return value;

            var sb = new System.Text.StringBuilder();
            int used = 0;
            foreach (char ch in value)
            {
                int size = System.Text.Encoding.UTF8.GetByteCount(new[] { ch });
                if (used + size > 117) break;
                sb.Append(ch);
                used += size;
            }
            return sb.Append("...").ToString();
        }

        /// <summary>Player-facing text. Never mentions who else is involved.</summary>
        public static string DefaultMessage(InteractionOutcome outcome)
        {
            switch (outcome)
            {
                case InteractionOutcome.RejectedTooFar: return "Too far away.";
                case InteractionOutcome.RejectedNoLineOfSight: return "You cannot reach that.";
                case InteractionOutcome.RejectedBusy: return "Someone is already using that.";
                case InteractionOutcome.RejectedUnavailable: return "That cannot be used right now.";
                case InteractionOutcome.RejectedWrongRole: return "That is not yours to touch.";
                case InteractionOutcome.RejectedPlayerState: return "You cannot do that yet.";
                case InteractionOutcome.RejectedUnknownTarget: return "Nothing there.";
                // Reached when the server has no live body for the requester. The
                // wording avoids "player"/"client" so the audit's tripwire for
                // person-identifying refusals stays meaningful.
                case InteractionOutcome.RejectedNotOwner: return "You are not in the session.";
                case InteractionOutcome.Cancelled: return "Interrupted.";
                default: return "";
            }
        }

        public bool IsRejection => (byte)Outcome >= 10;
    }
}
