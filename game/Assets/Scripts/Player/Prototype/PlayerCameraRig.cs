using UnityEngine;
using UnityEngine.InputSystem;

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

        [Header("Camera mode")]
        [Tooltip("First or third person. Local only — never replicated.")]
        public CameraMode Mode = CameraMode.ThirdPerson;

        [Tooltip("Toggles between first and third person. C, not V — V is push-to-talk " +
                 "(VoiceCapture.PushToTalkKey), so binding the camera there flipped the " +
                 "view every time anyone spoke.")]
        public Key ToggleKey = Key.C;

        [Tooltip("Seconds to travel between the two modes. Small: the switch should " +
                 "read as immediate, just not as a jump cut.")]
        public float ModeBlendSeconds = 0.18f;

        [Header("First person")]
        [Tooltip("Eye height above the feet. MUST match PlayerInteractionDetector.EyeHeight " +
                 "(and the server's), or in first person you look from one height and " +
                 "reach from another, and the crosshair starts lying about what is in range.")]
        public float FirstPersonEyeHeight = 1.5f;

        [Tooltip("Vertical field of view in first person. Wider than third person, " +
                 "which is conventional and reduces the shut-in feeling.")]
        public float FirstPersonFov = 75f;

        [Tooltip("Look sensitivity in first person.")]
        public float FirstPersonSensitivity = 1f;

        [Tooltip("Near clip in first person. Small, so a wall you stand against does " +
                 "not slice open.")]
        public float FirstPersonNearClip = 0.04f;

        [Tooltip("Local-only offset applied to carried evidence in first person, so the " +
                 "folder sits low instead of filling the screen. Never seen by anyone else.")]
        public Vector3 FirstPersonCarryOffset = new Vector3(0f, -0.17f, 0.06f);

        [Header("Mouse look")]
        [Tooltip("Look sensitivity in THIRD person. Multiplies PlayerInputReader.MouseSensitivity.")]
        public float SensitivityMultiplier = 1f;

        [Tooltip("Vertical field of view in third person.")]
        public float ThirdPersonFov = 60f;

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

        private Camera _camera;
        private float _defaultNearClip;
        private float _modeBlend;              // 0 = third person, 1 = first
        private Transform _appliedTarget;      // so a respawn re-applies the mode

        /// <summary>Yaw in degrees. PlayerMovement uses our forward vector, not this, but it is handy for debugging.</summary>
        public float Yaw => _yaw;

        /// <summary>0 = fully third person, 1 = fully first. Debug readout.</summary>
        public float ModeBlend => _modeBlend;

        /// <summary>Where the camera is orbiting, in world space. Debug readout.</summary>
        public Vector3 Pivot => _smoothedPivot;

        /// <summary>How far back the camera actually is after collision. Debug readout.</summary>
        public float CurrentDistance => _currentDistance;

        /// <summary>True once the blend has committed to first person.</summary>
        public bool IsFirstPerson => Mode == CameraMode.FirstPerson;

        private void Start()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null) _defaultNearClip = _camera.nearClipPlane;

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

            ReadToggle();
            ApplyModeToTarget();

            // ONE PATH, NOT TWO. Both modes are the same orbit camera with different
            // numbers, blended by _modeBlend: first person is simply a zero-distance,
            // zero-shoulder orbit at eye height. At blend 0 every value below is
            // literally the third-person field it came from, so third person is
            // unchanged rather than merely similar.
            float target = Mode == CameraMode.FirstPerson ? 1f : 0f;
            _modeBlend = ModeBlendSeconds <= 0f
                ? target
                : Mathf.MoveTowards(_modeBlend, target, Time.deltaTime / ModeBlendSeconds);

            float pivotHeight = Mathf.Lerp(TargetHeight, FirstPersonEyeHeight, _modeBlend);
            float wantDistance = Mathf.Lerp(Distance, 0f, _modeBlend);
            float shoulder = Mathf.Lerp(ShoulderOffset, 0f, _modeBlend);

            ReadMouse();

            // Smooth the point we orbit, not the camera position. Smoothing the
            // camera directly makes it swing wide on fast turns.
            //
            // Smoothing goes to zero in first person: a lagging pivot is pleasant
            // framing when you are watching a character and motion sickness when you
            // are inside one.
            float followSmooth = Mathf.Lerp(FollowSmoothTime, 0f, _modeBlend);
            _smoothedPivot = Vector3.SmoothDamp(
                _smoothedPivot, GetPivot(pivotHeight), ref _followVelocity, followSmooth);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 wantedOffset = rotation * new Vector3(shoulder, 0f, -wantDistance);
            float wantedDistance = ResolveCollision(_smoothedPivot, wantedOffset, wantDistance);

            // Snap in instantly when something blocks the view, ease back out slowly.
            // Popping outward is far more noticeable than popping inward.
            _currentDistance = wantedDistance < _currentDistance
                ? wantedDistance
                : Mathf.Lerp(_currentDistance, wantedDistance,
                             1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, CollisionRecoverTime)));

            Vector3 finalOffset = rotation * new Vector3(shoulder, 0f, -_currentDistance);
            transform.position = _smoothedPivot + finalOffset;
            transform.rotation = rotation;

            if (_camera != null)
            {
                _camera.fieldOfView = Mathf.Lerp(ThirdPersonFov, FirstPersonFov, _modeBlend);
                // A tight near plane is what stops a wall you are pressed against
                // from slicing open once the camera is inside your own head.
                _camera.nearClipPlane = Mathf.Lerp(_defaultNearClip, FirstPersonNearClip, _modeBlend);
            }
        }

        /// <summary>
        /// V toggles the mode. Refused while the cursor is free, which is this
        /// project's existing signal for "the player is in a menu or typing" — the
        /// same gate PlayerCameraRig already uses to ignore mouse look. Refused with
        /// no Target too, so a keypress before the network spawns does nothing.
        /// </summary>
        private void ReadToggle()
        {
            if (Target == null) return;
            if (Input == null || !Input.CursorLocked) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!System.Enum.IsDefined(typeof(Key), ToggleKey)) return;

            if (keyboard[ToggleKey].wasPressedThisFrame)
                Mode = Mode == CameraMode.FirstPerson
                    ? CameraMode.ThirdPerson
                    : CameraMode.FirstPerson;
        }

        /// <summary>
        /// Pushes the consequences of the mode onto the player: which of your own
        /// renderers are hidden, whether your body turns to face the camera, and
        /// where a carried folder is drawn FOR YOU.
        ///
        /// All three are local presentation. None of them touch custody, movement
        /// physics or anything replicated — a remote observer sees an identical
        /// player whichever mode you are in.
        ///
        /// Re-applied when Target changes so a respawn does not come back with a
        /// hidden head or a folder stuck at a first-person offset.
        /// </summary>
        private void ApplyModeToTarget()
        {
            bool first = Mode == CameraMode.FirstPerson;
            bool targetChanged = _appliedTarget != Target;
            _appliedTarget = Target;

            var body = Target.GetComponent<PlayerLocalBody>();
            if (body != null) body.SetFirstPerson(first);

            var movement = Target.GetComponent<PlayerMovement>();
            if (movement != null) movement.FaceCameraYaw = first;

            // The socket already applies LocalViewOffset only for the local carrier;
            // zeroing it in third person is what keeps that offset first-person-only.
            var socket = Target.GetComponent<PlayerCarrySocket>();
            if (socket != null)
                socket.LocalViewOffset = first ? FirstPersonCarryOffset : Vector3.zero;

            if (targetChanged) _hasPivot = false;   // resnap onto the new body
        }

        private void ReadMouse()
        {
            if (Input == null || !Input.CursorLocked) return;

            // Pitch limits are deliberately shared across both modes: they are about
            // how far a neck bends, which does not change with the camera.
            float sensitivity = Mathf.Lerp(SensitivityMultiplier, FirstPersonSensitivity, _modeBlend);

            Vector2 look = Input.Look * sensitivity;
            _yaw += look.x;
            _pitch += InvertY ? look.y : -look.y;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        }

        private Vector3 GetPivot() => GetPivot(TargetHeight);

        private Vector3 GetPivot(float height) => Target.position + Vector3.up * height;

        /// <summary>
        /// Sphere-cast from the player's head out to where the camera wants to be.
        /// A sphere rather than a ray, so the camera stops before its edges clip a
        /// wall rather than when its centre point touches one.
        /// </summary>
        private float ResolveCollision(Vector3 pivot, Vector3 wantedOffset, float baseDistance)
        {
            // First person: there is no boom to collide. The camera sits at the eye
            // point, which the CharacterController already keeps out of geometry, so
            // the only real hazard is the near plane — handled by FirstPersonNearClip.
            // Sphere-casting a zero-length boom here would just clamp to MinDistance
            // and shove the view backwards out of the head.
            if (baseDistance < 0.01f) return 0f;

            float wanted = wantedOffset.magnitude;
            if (wanted < 0.001f) return 0f;

            Vector3 direction = wantedOffset / wanted;

            bool blocked = Physics.SphereCast(
                pivot, CollisionPadding, direction, out RaycastHit hit,
                wanted, CollisionLayers, QueryTriggerInteraction.Ignore);

            if (!blocked) return baseDistance;

            // hit.distance is measured along the offset direction, which includes
            // the shoulder nudge, so scale it back onto the pure distance axis.
            float scale = baseDistance / wanted;
            return Mathf.Max(Mathf.Min(MinDistance, baseDistance), hit.distance * scale);
        }
    }
}
