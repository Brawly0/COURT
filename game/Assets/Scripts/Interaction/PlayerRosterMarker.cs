using UnityEngine;

namespace CaseClosed.Game.Interaction
{
    /// <summary>
    /// Marks a GameObject as a player body, so line-of-sight checks can skip it
    /// cheaply — standing behind someone should not stop you opening a door.
    ///
    /// In its own file deliberately. Unity's script-to-prefab binding breaks for a
    /// MonoBehaviour declared in a file named after a different class; this project
    /// has already been bitten by that once (see ClientNetworkTransform.cs) and it
    /// showed up as a "missing script" only in builds.
    /// </summary>
    public class PlayerRosterMarker : MonoBehaviour { }
}
