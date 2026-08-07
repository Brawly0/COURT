using System.Collections.Generic;
using UnityEngine;
using CaseClosed.Game.Interaction;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// The desk at the Evidence Locker where a carried document is entered into the
    /// record.
    ///
    /// WHAT THE CLIENT MAY SAY: "I want to use this terminal." That is all. It never
    /// names an evidence id — the server looks up what that player is actually
    /// holding. A client asking to register E-003 while carrying nothing, or while
    /// carrying something else, is not rejected so much as unable to ask the
    /// question: there is no field for it.
    ///
    /// Everything shared — distance, line of sight, player state, the exclusivity
    /// lock, the hold clock — comes from NetworkInteractable and is re-checked every
    /// frame of the hold. This class only adds the evidence-specific rules.
    /// </summary>
    public class RegistrationTerminal : NetworkInteractable
    {
        /// <summary>
        /// The item each holding client started with, so we can tell "still holding
        /// the same document" from "swapped it for a different one mid-hold".
        /// Server-side only.
        /// </summary>
        private readonly Dictionary<ulong, string> _holdStartedWith = new();

        /// <summary>
        /// Shown to this client. The prompt reads the LOCAL carry state, which the
        /// client already legitimately knows, so it can say something useful before
        /// the server is ever asked.
        /// </summary>
        public override string PromptFor(ulong clientId)
        {
            var director = EvidenceCustodyDirector.Instance;
            return director != null && director.LocalIsCarrying
                ? "Register Evidence"
                : "Nothing To Register";
        }

        /// <summary>
        /// Evidence-specific validation. Runs AFTER the shared checks, and runs
        /// again on every frame of the hold — which is what turns "walked away" and
        /// "dropped it" into cancellations without the client's cooperation.
        /// </summary>
        public override InteractionOutcome ServerValidate(ulong clientId)
        {
            var director = EvidenceCustodyDirector.Instance;
            if (director == null) return InteractionOutcome.RejectedUnavailable;

            var carried = director.CarriedBy(clientId);

            // Nothing in hand. Covers "registering evidence lying on the floor" and
            // "registering another player's evidence" in one test: CarriedBy only
            // ever returns what THIS client holds.
            if (carried == null)
            {
                _holdStartedWith.Remove(clientId);
                return InteractionOutcome.RejectedUnavailable;
            }

            // A fabricated id cannot reach here — this instance came from the
            // server's own dictionary, not from anything a client sent.
            if (!carried.IsFound) return InteractionOutcome.RejectedUnavailable;
            if (carried.IsRegistered) return InteractionOutcome.RejectedUnavailable;
            if (carried.CarrierClientId != clientId) return InteractionOutcome.RejectedNotOwner;

            // Custody must not change during the hold. Swapping documents mid-hold
            // would otherwise let a player start the clock on one item and finish it
            // on another.
            if (_holdStartedWith.TryGetValue(clientId, out string startedWith))
            {
                if (startedWith != carried.EvidenceId)
                {
                    _holdStartedWith.Remove(clientId);
                    return InteractionOutcome.RejectedUnavailable;
                }
            }
            else
            {
                _holdStartedWith[clientId] = carried.EvidenceId;
            }

            return InteractionOutcome.Accepted;
        }

        /// <summary>
        /// The hold completed with every check still passing. The director owns the
        /// transition itself; this class never touches custody or legal state.
        /// </summary>
        public override void ServerExecute(ulong clientId)
        {
            _holdStartedWith.Remove(clientId);
            EvidenceCustodyDirector.Instance?.ServerRegisterCarried(clientId, transform.position);
        }

        /// <summary>
        /// Abandoned: released the key, walked out of range, lost sight, dropped it,
        /// or disconnected. Nothing to undo — registration only ever happens in
        /// ServerExecute, so an interrupted hold leaves the evidence exactly as it
        /// was, still carried and still Unregistered.
        /// </summary>
        public override void ServerCancel(ulong clientId) => _holdStartedWith.Remove(clientId);
    }
}
