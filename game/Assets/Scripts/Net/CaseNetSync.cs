using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Networked case coordination. The seed is the only case data on the
    /// wire — every client runs the same deterministic Truth Engine locally
    /// (same seed, same universe; Pcg32 guarantees it). Ground truth still
    /// only *matters* on the host: clients hold the CaseFile in memory, which
    /// is the accepted listen-server trust model at this phase (GDD 12 —
    /// knowledge-slice wire isolation arrives with the interview systems).
    /// Evidence collection replicates so an item leaves everyone's world.
    /// </summary>
    public class CaseNetSync : NetworkBehaviour
    {
        public static CaseNetSync Instance { get; private set; }

        private readonly NetworkVariable<ulong> _seed = new NetworkVariable<ulong>(0);

        private void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _seed.Value = CaseRuntime.Instance.Seed;

            Debug.Log($"[CaseNetSync] spawn server={IsServer} seed={_seed.Value}");
            if (_seed.Value != 0)
                CaseRuntime.Instance.GenerateNow(_seed.Value);
            _seed.OnValueChanged += (_, v) =>
            {
                Debug.Log($"[CaseNetSync] seed received: {v}");
                CaseRuntime.Instance.GenerateNow(v);
            };
        }

        public void RequestCollect(int index, string itemName)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                CaseRuntime.Instance.ApplyCollect(index, itemName);   // offline
                return;
            }
            CollectServerRpc(index, itemName);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CollectServerRpc(int index, string itemName)
        {
            // server-validated: the item must still exist server-side
            Debug.Log($"[CaseNetSync] CollectServerRpc({index})");
            if (GameObject.Find($"Evidence_{index}") == null) return;
            CollectClientRpc(index, itemName);
        }

        [ClientRpc]
        private void CollectClientRpc(int index, string itemName)
        {
            Debug.Log($"[CaseNetSync] CollectClientRpc({index})");
            CaseRuntime.Instance.ApplyCollect(index, itemName);
        }
    }
}
