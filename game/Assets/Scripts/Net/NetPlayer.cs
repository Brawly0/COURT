using Unity.Netcode;
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
        // vertical look angle, replicated so remote puppets tilt their heads
        // the way their owner is actually looking
        private readonly NetworkVariable<float> _lookPitch = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private FirstPersonController _fpc;
        private CharacterAnimator _anim;

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                if (_fpc == null) _fpc = GetComponent<FirstPersonController>();
                if (_fpc != null && Mathf.Abs(_lookPitch.Value - _fpc.Pitch) > 0.5f)
                    _lookPitch.Value = _fpc.Pitch;
            }
            else
            {
                if (_anim == null) _anim = GetComponentInChildren<CharacterAnimator>();
                if (_anim != null) _anim.LookPitch = _lookPitch.Value;
            }
        }

        public override void OnNetworkSpawn()
        {
            var cnt = GetComponent<ClientNetworkTransform>();
            Debug.Log($"[NetPlayer] spawn id={OwnerClientId} owner={IsOwner} ownerAuth={cnt != null && cnt.IsOwnerAuthoritative}");

            if (IsOwner)
            {
                // spawn at the scene's SpawnPoint (works in any building), staggered per client
                var sp = GameObject.Find("SpawnPoint");
                var basePos = sp != null ? sp.transform.position : new Vector3(12f, 0.1f, 0f);
                transform.position = basePos + new Vector3(0f, 0f, ((int)OwnerClientId % 5) * 1.4f - 2.8f);
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

}
