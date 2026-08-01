using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>Marks a room's spawn point. Placed by GrayboxBuilder; looked up by CaseRuntime.</summary>
    public class ZoneAnchor : MonoBehaviour
    {
        public string ZoneName;
    }
}
