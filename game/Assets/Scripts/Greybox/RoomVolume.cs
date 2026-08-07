using UnityEngine;

namespace CaseClosed.Game.Greybox
{
    /// <summary>
    /// WHY THIS EXISTS: marks a named region of the building so tooling can answer
    /// "which room is this player in". That single fact drives the debug readout and
    /// the travel timer, and later it is what evidence/zone systems will hang off.
    ///
    /// Deliberately NOT a trigger collider. Triggers depend on collision layers, the
    /// CharacterController's quirks about firing enter/exit, and objects being awake.
    /// A plain bounds test against a position is one line, never misses an event, and
    /// works for remote players whose controllers are switched off.
    /// </summary>
    public class RoomVolume : MonoBehaviour
    {
        [Tooltip("Shown in the debug HUD and used by the travel timer.")]
        public string RoomName = "Room";

        [Tooltip("Extents of the region, centred on this transform.")]
        public Vector3 Size = new Vector3(20f, 8f, 20f);

        [Tooltip("Corridors count as their own 'room' but are not worth timing legs between.")]
        public bool IsTransitional = false;

        public Bounds WorldBounds => new Bounds(transform.position, Size);

        public bool Contains(Vector3 point) => WorldBounds.Contains(point);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsTransitional
                ? new Color(0.5f, 0.7f, 1f, 0.25f)
                : new Color(0.4f, 1f, 0.5f, 0.25f);
            Gizmos.DrawCube(transform.position, Size);
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(transform.position, Size);
        }
    }
}
