using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Builds the player's PSX body at runtime (prefabs can't hold runtime-built
    /// primitives cleanly, and each client wants its own seeded look).
    /// The owner's own head is hidden from their camera so it doesn't fill the
    /// screen, while remote players see the full puppet.
    /// </summary>
    public class PlayerBodySpawner : MonoBehaviour
    {
        public int LookSeed = -1;   // -1 = derive from owner id / instance

        private void Start()
        {
            int seed = LookSeed >= 0 ? LookSeed : Mathf.Abs((int)(GetEntityId().GetHashCode())) % 9973;
            var net = GetComponent<NetPlayer>();
            if (net != null && net.IsSpawned) seed = 700 + (int)net.OwnerClientId * 31;

            var rig = CharacterBuilder.Build(transform, seed, true);
            var anim = gameObject.AddComponent<CharacterAnimator>();
            anim.Init(rig, seed);
            _headRenderers = rig.HeadPivot.GetComponentsInChildren<Renderer>();

            // OFFLINE mode: NetPlayer exists but is never network-spawned, and an
            // unspawned NetworkBehaviour reports IsOwner=false - which left the
            // head visible and parked the camera inside the back of the skull.
            // FirstPersonController re-applies visibility whenever the view toggles.
            bool isLocal = net == null || !net.IsSpawned || net.IsOwner;
            if (isLocal)
            {
                var fpc = GetComponent<FirstPersonController>();
                SetHeadVisible(fpc == null || fpc.ThirdPerson);
            }
        }

        private Renderer[] _headRenderers;
        public bool HeadReady => _headRenderers != null;

        /// <summary>Third person shows the local head; first person shadow-only.</summary>
        public void SetHeadVisible(bool visible)
        {
            if (_headRenderers == null) return;
            foreach (var r in _headRenderers)
                if (r != null)
                    r.shadowCastingMode = visible
                        ? UnityEngine.Rendering.ShadowCastingMode.On
                        : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }
}
