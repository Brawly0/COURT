using UnityEngine;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// WHY THIS EXISTS: a standard third-person orbit camera, kept completely
    /// separate from the character. The camera does not move the player and the
    /// player does not move the camera — PlayerMovement only ever *reads* this
    /// transform's facing to work out which way "forward" is.
    ///
    /// That one-way relationship matters later: in multiplayer every client has
    /// its own camera, but only one body per player. Nothing here should ever end
    /// up on a networked object.
    ///
    /// Put this on the Camera itself and point Target at the player.
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The player to orbit. Usually the character root.")]
        public Transform Target;

        [Tooltip("Look at a point above the feet — roughly the head — not the floor.")]
        public float TargetHeight = 1.5f;

        [Header("Distance & framing")]
        [Tooltip("How far behind the player the camera sits, when nothing is in the way.")]
        public float Distance = 4.5f;

        [Tooltip("Shoulder offset. Positive nudges the camera right, for an over-the-shoulder feel.")]
        public float ShoulderOffset = 0.6f;

        [Header("Mouse look")]
        [Tooltip("Extra multiplier on top of PlayerInputReader.MouseSensitivity.")]
        public float SensitivityMultiplier = 1f;

        [Tooltip("How far down you can look. Negative is downward.")]
        public float MinPitch = -35f;

        [Tooltip("How far up you can look.")]
        public float MaxPitch = 70f;

        [Tooltip("Invert vertical mouse look.")]
        public bool InvertY = false;

        [Header("Smoothing")]
        [Tooltip("Seconds for the camera to catch up to the player. 0 = rigid, higher = floatier.")]
        public float FollowSmoothTime = 0.06f;

        [Tooltip("Seconds for the camera to ease back out after a wall stops blocking it.")]
        public float CollisionRecoverTime = 0.25f;

        [Header("Collision")]
        [Tooltip("What the camera refuses to pass through.")]
        public LayerMask CollisionLayers = ~0;

        [Tooltip("How far off a wall the camera stops. Stops the near plane clipping through.")]
        public float CollisionPadding = 0.25f;

        [Tooltip("Never pull closer than this, even in a tight corner.")]
        public float MinDistance = 0.8f;

        [Header("Input")]
        [Tooltip("Where mouse movement comes from. Leave empty and it finds the one on Target.")]
        public PlayerInputReader Input;

        private float _yaw;
        private float _pitch = 12f;
        private float _currentDistance;
        private Vector3 _followVelocity;
        private Vector3 _smoothedPivot;
        private bool _hasPivot;

        /// <summary>Yaw in degrees. PlayerMovement uses our forward vector, not this, but it is handy for debugging.</summary>
        public float Yaw => _yaw;

        private void Start()
        {
            if (Target != null && Input == null)
                Input = Target.GetComponent<PlayerInputReader>();

            _yaw = Target != null ? Target.eulerAngles.y : transform.eulerAngles.y;
            _currentDistance = Distance;

            // Target is null until a session spawns the local player, so the pivot
            // cannot be computed yet. LateUpdate snaps it the first frame we have one.
            if (Target != null)
            {
                _smoothedPivot = GetPivot();
                _hasPivot = true;
            }
        }

        /// <summary>
        /// LateUpdate, not Update: the player has already moved by now, so the
        /// camera lands on the final position and never lags a frame behind.
        /// </summary>
        private void LateUpdate()
        {
            if (Target == null) return;

            // First frame with a target (it was spawned by the network): snap to it
            // rather than smoothing in from wherever the camera happened to sit.
            if (!_hasPivot)
            {
                _smoothedPivot = GetPivot();
                _yaw = Target.eulerAngles.y;
                _hasPivot = true;
            }

            ReadMouse();

            // Smooth the point we orbit, not the camera position. Smoothing the
            // camera directly makes it swing wide on fast turns.
            _smoothedPivot = Vector3.SmoothDamp(
                _smoothedPivot, GetPivot(), ref _followVelocity, FollowSmoothTime);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 wantedOffset = rotation * new Vector3(ShoulderOffset, 0f, -Distance);
            float wantedDistance = ResolveCollision(_smoothedPivot, wantedOffset);

            // Snap in instantly when something blocks the view, ease back out slowly.
            // Popping outward is far more noticeable than popping inward.
            _currentDistance = wantedDistance < _currentDistance
                ? wantedDistance
                : Mathf.Lerp(_currentDistance, wantedDistance,
                             1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, CollisionRecoverTime)));

            Vector3 finalOffset = rotation * new Vector3(ShoulderOffset, 0f, -_currentDistance);
            transform.position = _smoothedPivot + finalOffset;
            transform.rotation = rotation;
        }

        private void ReadMouse()
        {
            if (Input == null || !Input.CursorLocked) return;

            Vector2 look = Input.Look * SensitivityMultiplier;
            _yaw += look.x;
            _pitch += InvertY ? look.y : -look.y;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        }

        private Vector3 GetPivot() => Target.position + Vector3.up * TargetHeight;

        /// <summary>
        /// Sphere-cast from the player's head out to where the camera wants to be.
        /// A sphere rather than a ray, so the camera stops before its edges clip a
        /// wall rather than when its centre point touches one.
        /// </summary>
        private float ResolveCollision(Vector3 pivot, Vector3 wantedOffset)
        {
            float wanted = wantedOffset.magnitude;
            if (wanted < 0.001f) return MinDistance;

            Vector3 direction = wantedOffset / wanted;

            bool blocked = Physics.SphereCast(
                pivot, CollisionPadding, direction, out RaycastHit hit,
                wanted, CollisionLayers, QueryTriggerInteraction.Ignore);

            if (!blocked) return Distance;

            // hit.distance is measured along the offset direction, which includes
            // the shoulder nudge, so scale it back onto the pure distance axis.
            float scale = Distance / wanted;
            return Mathf.Max(MinDistance, hit.distance * scale);
        }
    }
}
