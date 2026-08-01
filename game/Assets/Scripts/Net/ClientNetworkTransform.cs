using Unity.Netcode.Components;

namespace CaseClosed.Game
{
    /// <summary>
    /// Owner-authoritative NetworkTransform (standard NGO pattern).
    /// Lives in its own file: Unity's script-to-prefab binding breaks for
    /// MonoBehaviours defined in a file named after a different class —
    /// that produced a "missing script" on Player.prefab in builds.
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        public bool IsOwnerAuthoritative => !OnIsServerAuthoritative();
        protected override bool OnIsServerAuthoritative() => false;
    }
}
