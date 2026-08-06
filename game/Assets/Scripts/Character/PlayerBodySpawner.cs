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

            bool isLocal = net == null || net.IsOwner;
            if (isLocal)
            {
                // hide own head/hair from first person, keep the body visible when looking down
                foreach (var r in rig.HeadPivot.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }
    }
}
