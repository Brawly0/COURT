using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Owner gate: remote copies of a player are visible capsules only —
    /// no input, no camera, no listener. Movement replicates via
    /// ClientNetworkTransform (owner-authoritative for the graybox spine;
    /// interactions stay server-validated per GDD 12).
    /// </summary>
    public class NetPlayer : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            var cnt = GetComponent<ClientNetworkTransform>();
            Debug.Log($"[NetPlayer] spawn id={OwnerClientId} owner={IsOwner} ownerAuth={cnt != null && cnt.IsOwnerAuthoritative}");

            if (IsOwner)
            {
                // hall spawn, staggered so players don't share a skeleton
                transform.position = new Vector3(6f, 0.1f, ((int)OwnerClientId % 5) * 1.2f - 2.4f);
                transform.rotation = Quaternion.Euler(0f, -90f, 0f);
                return;
            }
            var fpc = GetComponent<FirstPersonController>();
            if (fpc != null) fpc.enabled = false;
            var interactor = GetComponent<Interactor>();
            if (interactor != null) interactor.enabled = false;
            // CRITICAL: a live CharacterController stomps external transform writes,
            // freezing remote copies at spawn. Remote = pure puppet, no CC.
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) cam.gameObject.SetActive(false);
        }
    }

    /// <summary>Owner-authoritative NetworkTransform (standard NGO pattern).</summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        public bool IsOwnerAuthoritative => !OnIsServerAuthoritative();
        protected override bool OnIsServerAuthoritative() => false;
    }
}
