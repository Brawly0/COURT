using UnityEngine;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// WHERE A CARRIED OBJECT IS DRAWN. One component, one question answered:
    /// "if this player is holding something, where does it go?"
    ///
    /// The socket itself is a joint parented under Torso, so it inherits the
    /// animated chest and the folder moves with the body for free. Nothing here
    /// decides *whether* the player is carrying — that is custody, and custody
    /// belongs to the server. This only answers where.
    ///
    /// FIRST PERSON, LATER: <see cref="LocalViewOffset"/> is applied only for the
    /// local player's own body. It is zero today because the game is third-person
    /// and the folder is meant to be seen. When a first-person mode arrives, a
    /// chest-height folder would fill the screen, and this is the hook that pushes
    /// it down and forward out of the near plane — without touching custody,
    /// networking, or what every other client sees.
    /// </summary>
    public class PlayerCarrySocket : MonoBehaviour
    {
        [Header("Attachment")]
        [Tooltip("The joint a carried object is drawn at. Usually a child of Torso.")]
        public Transform Socket;

        [Tooltip("Fine positional adjustment, in the socket's local space.")]
        public Vector3 PositionOffset = Vector3.zero;

        [Tooltip("Fine rotational adjustment, in degrees, in the socket's local space.")]
        public Vector3 RotationOffset = Vector3.zero;

        [Header("First person (reserved)")]
        [Tooltip("Extra offset applied ONLY on the local player's own body, to keep a " +
                 "held object out of the camera. Unused while the game is third-person.")]
        public Vector3 LocalViewOffset = Vector3.zero;

        /// <summary>Falls back to the player root, so a rig without a socket still works.</summary>
        public Transform Attachment => Socket != null ? Socket : transform;

        /// <summary>
        /// The world pose a carried object should sit at this frame.
        ///
        /// Read in LateUpdate, never Update — the socket hangs off an animated
        /// chest, so sampling it before the Animator has run gives last frame's
        /// pose and the folder visibly trails the body.
        /// </summary>
        public void GetAttachPose(bool localView, out Vector3 position, out Quaternion rotation)
        {
            Transform attach = Attachment;

            Vector3 local = PositionOffset;
            if (localView) local += LocalViewOffset;

            position = attach.TransformPoint(local);
            rotation = attach.rotation * Quaternion.Euler(RotationOffset);
        }

        private void OnDrawGizmosSelected()
        {
            Transform attach = Attachment;
            if (attach == null) return;

            Gizmos.color = new Color(1f, 0.85f, 0.45f);
            Gizmos.matrix = Matrix4x4.TRS(
                attach.TransformPoint(PositionOffset),
                attach.rotation * Quaternion.Euler(RotationOffset),
                Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.30f, 0.04f, 0.22f));
        }
    }
}
