using System;
using UnityEngine;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// The host's vault. Holds the one CompleteCaseTruth for the match and hands it
    /// to nothing that would put it on the wire.
    ///
    /// Plain MonoBehaviour, NOT a NetworkBehaviour — that is deliberate. A
    /// NetworkBehaviour invites NetworkVariables, and a NetworkVariable of anything
    /// derived from the truth is a leak waiting to happen. Replication lives next
    /// door in CaseNetworkController, which can only see the filtered views.
    ///
    /// On a client this object exists but Truth stays null: clients genuinely do not
    /// have the data, rather than having it and being asked not to look.
    /// </summary>
    public class ActiveCaseManager : MonoBehaviour
    {
        public static ActiveCaseManager Instance { get; private set; }

        /// <summary>HOST ONLY. Null on clients, always.</summary>
        public CompleteCaseTruth Truth { get; private set; }

        public bool HasCase => Truth != null;

        /// <summary>Raised on the host after a new truth is stored.</summary>
        public event Action<CompleteCaseTruth> CaseStored;

        /// <summary>Raised on the host when the case is discarded.</summary>
        public event Action CaseCleared;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Store a freshly generated case. Callers must already have checked they
        /// are the server; this class does not know about networking and cannot
        /// check for them.
        /// </summary>
        public void Store(CompleteCaseTruth truth)
        {
            if (truth == null)
            {
                Debug.LogError("[Case] Refusing to store a null truth.");
                return;
            }

            Truth = truth;
            Debug.Log($"[Case] Host stored truth for seed {truth.Seed} " +
                      $"(\"{truth.File.Title}\", perpetrator hidden).");
            CaseStored?.Invoke(truth);
        }

        /// <summary>
        /// Drop the case. Note this is NOT called when a client disconnects — the
        /// host's case outlives any individual player leaving.
        /// </summary>
        public void Clear()
        {
            if (Truth == null) return;
            Truth = null;
            Debug.Log("[Case] Host cleared the active case.");
            CaseCleared?.Invoke();
        }
    }
}
