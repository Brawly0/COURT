using UnityEngine;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// WHY THIS EXISTS: it turns "what the player asked for" into "where the body
    /// actually ends up". It owns speed, gravity, jumping, ground contact and
    /// which way the character faces — and nothing else. No cameras, no keyboards,
    /// no animation, no evidence/trial/case logic.
    ///
    /// It reads a PlayerInputReader and a camera Transform. It never reads a key
    /// directly, which is what will let a networked version feed it inputs later.
    ///
    /// Everything downstream (animation, the debug HUD) reads the read-only
    /// properties at the bottom — State, CurrentSpeed, IsGrounded. Those are
    /// derived from real velocity, never from a timer.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Speeds (metres per second)")]
        public float WalkSpeed = 1.9f;
        public float RunSpeed = 4.3f;
        public float SprintSpeed = 7.0f;

        [Header("Feel")]
        [Tooltip("How hard we push toward the target speed. Higher = snappier starts.")]
        public float Acceleration = 16f;
        [Tooltip("How hard we brake when input stops. Higher = less skating.")]
        public float Deceleration = 20f;
        [Tooltip("Seconds to swing around to face the movement direction. Lower = twitchier.")]
        public float TurnSmoothTime = 0.07f;
        [Tooltip("Fraction of control kept while airborne. 1 = full air control.")]
        [Range(0f, 1f)] public float AirControl = 0.55f;

        [Header("Jump & gravity")]
        public float JumpHeight = 1.4f;
        [Tooltip("Deliberately stronger than real gravity (-9.81). Floaty jumps feel bad.")]
        public float Gravity = -25f;
        [Tooltip("Extra gravity once falling, so the arc peaks softly and drops fast.")]
        public float FallGravityMultiplier = 1.4f;
        [Tooltip("Jump still works this long after walking off an edge. Classic platformer forgiveness.")]
        public float CoyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires on touchdown.")]
        public float JumpBufferTime = 0.12f;

        [Header("Ground detection")]
        [Tooltip("What counts as ground. Leave as Everything for the prototype.")]
        public LayerMask GroundLayers = ~0;
        [Tooltip("How far below the feet we look for ground.")]
        public float GroundProbeDistance = 0.35f;

        [Header("Slopes")]
        [Tooltip("Pushes the body onto the ground so running downhill doesn't turn into bunny-hopping.")]
        public float SlopeStickForce = 8f;
        [Tooltip("How fast we slide back down a slope that is too steep to climb.")]
        public float SteepSlideSpeed = 6f;

        [Header("Landing")]
        [Tooltip("How long the Land state is held before returning to locomotion.")]
        public float LandDuration = 0.15f;

        [Header("References")]
        [Tooltip("Movement is relative to this. Leave empty and it finds the main camera.")]
        public Transform CameraTransform;

        private CharacterController _controller;
        private PlayerInputReader _input;

        private Vector3 _planarVelocity;      // world-space XZ movement
        private float _verticalVelocity;      // Y, handled separately from steering
        private float _turnVelocity;          // scratch value for SmoothDampAngle

        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _landTimer;
        private bool _wasGroundedLastFrame;

        private Vector3 _groundNormal = Vector3.up;
        private float _groundAngle;

        // ---- read-only state for the animator and the debug HUD ----

        /// <summary>Horizontal speed in m/s. Ignores falling.</summary>
        public float CurrentSpeed => _planarVelocity.magnitude;

        /// <summary>Vertical speed in m/s. Positive = rising.</summary>
        public float VerticalSpeed => _verticalVelocity;

        public bool IsGrounded { get; private set; }

        public MovementState State { get; private set; } = MovementState.Idle;

        /// <summary>Angle of the ground under the feet, in degrees. 0 = flat.</summary>
        public float GroundAngle => _groundAngle;

        /// <summary>True when the ground is too steep to stand on and we are sliding.</summary>
        public bool OnSteepSlope => IsGrounded && _groundAngle > _controller.slopeLimit;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();

            if (CameraTransform == null && Camera.main != null)
                CameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            ProbeGround();
            UpdateTimers();
            ApplyGravityAndJump();
            ApplySteering();

            // One Move call per frame with everything combined. Calling Move twice
            // makes the controller fight itself on slopes.
            Vector3 motion = _planarVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            UpdateState();
            _wasGroundedLastFrame = IsGrounded;
        }

        /// <summary>
        /// CharacterController.isGrounded alone is famously flickery, so we also
        /// sphere-cast down from the middle of the capsule. That gives us a solid
        /// grounded flag AND the surface normal, which is what slope handling needs.
        /// </summary>
        private void ProbeGround()
        {
            float radius = _controller.radius;
            Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);

            bool hitSomething = Physics.SphereCast(
                origin, radius, Vector3.down, out RaycastHit hit,
                GroundProbeDistance + 0.05f, GroundLayers, QueryTriggerInteraction.Ignore);

            if (hitSomething)
            {
                _groundNormal = hit.normal;
                _groundAngle = Vector3.Angle(hit.normal, Vector3.up);
            }
            else
            {
                _groundNormal = Vector3.up;
                _groundAngle = 0f;
            }

            // Rising through the air must never count as grounded, or a jump would
            // be cancelled the instant it starts.
            IsGrounded = (hitSomething || _controller.isGrounded) && _verticalVelocity <= 0.1f;
        }

        private void UpdateTimers()
        {
            _coyoteTimer = IsGrounded ? CoyoteTime : _coyoteTimer - Time.deltaTime;

            _jumpBufferTimer = (_input != null && _input.JumpPressedThisFrame)
                ? JumpBufferTime
                : _jumpBufferTimer - Time.deltaTime;

            if (_landTimer > 0f) _landTimer -= Time.deltaTime;

            // Just touched down after time in the air -> play the landing beat.
            if (IsGrounded && !_wasGroundedLastFrame) _landTimer = LandDuration;
        }

        private void ApplyGravityAndJump()
        {
            if (IsGrounded && _verticalVelocity < 0f)
            {
                // Hold the body against the floor. Without this you skip down ramps.
                _verticalVelocity = -SlopeStickForce;
            }

            bool canJump = _coyoteTimer > 0f && _jumpBufferTimer > 0f;
            if (canJump)
            {
                // v = sqrt(2 * g * h) -> reaching exactly JumpHeight, whatever gravity is set to.
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(Gravity) * JumpHeight);
                _coyoteTimer = 0f;
                _jumpBufferTimer = 0f;
                _landTimer = 0f;
            }

            float g = _verticalVelocity < 0f ? Gravity * FallGravityMultiplier : Gravity;
            _verticalVelocity += g * Time.deltaTime;

            // Terminal velocity, so a long fall can't tunnel through the floor.
            _verticalVelocity = Mathf.Max(_verticalVelocity, -60f);
        }

        private void ApplySteering()
        {
            Vector2 rawInput = _input != null ? _input.Move : Vector2.zero;

            // Camera-relative: "forward" means "away from the camera", flattened so
            // looking at the ground doesn't drive the character into it.
            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (CameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(CameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(CameraTransform.right, Vector3.up).normalized;
            }
            Vector3 wishDirection = (forward * rawInput.y + right * rawInput.x);
            if (wishDirection.sqrMagnitude > 1f) wishDirection.Normalize();

            float targetSpeed = SelectTargetSpeed(rawInput);
            Vector3 targetVelocity = wishDirection * targetSpeed;

            // Accelerate when asking for movement, decelerate when not. Two separate
            // rates is what makes it feel snappy to start and still slide a little to a stop.
            float rate = wishDirection.sqrMagnitude > 0.001f ? Acceleration : Deceleration;
            if (!IsGrounded) rate *= AirControl;

            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity, targetVelocity, rate * Time.deltaTime);

            if (OnSteepSlope)
            {
                // Too steep to stand on: push down-slope instead of letting the
                // player climb it.
                Vector3 downSlope = Vector3.ProjectOnPlane(Vector3.down, _groundNormal).normalized;
                _planarVelocity += downSlope * SteepSlideSpeed * Time.deltaTime * 4f;
            }

            FaceMovementDirection();
        }

        private float SelectTargetSpeed(Vector2 rawInput)
        {
            if (rawInput.sqrMagnitude < 0.001f) return 0f;
            if (_input != null && _input.WalkHeld) return WalkSpeed;
            if (_input != null && _input.SprintHeld) return SprintSpeed;
            return RunSpeed;
        }

        /// <summary>
        /// Turn the whole body to face where it is going. Smoothed rather than
        /// snapped, because an instant 180 reads as a glitch at gameplay distance.
        /// </summary>
        private void FaceMovementDirection()
        {
            Vector3 flat = new Vector3(_planarVelocity.x, 0f, _planarVelocity.z);
            if (flat.sqrMagnitude < 0.01f) return;

            float targetYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float yaw = Mathf.SmoothDampAngle(
                transform.eulerAngles.y, targetYaw, ref _turnVelocity, TurnSmoothTime);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// The single source of truth for "what am I doing". Driven by measured
        /// speed and ground contact — never by how long a button was held.
        /// </summary>
        private void UpdateState()
        {
            if (!IsGrounded)
            {
                State = _verticalVelocity > 0.1f ? MovementState.Jump : MovementState.Fall;
                return;
            }

            if (_landTimer > 0f)
            {
                State = MovementState.Land;
                return;
            }

            float speed = CurrentSpeed;
            if (speed < 0.15f) State = MovementState.Idle;
            else if (speed < (WalkSpeed + RunSpeed) * 0.5f) State = MovementState.Walk;
            else if (speed < (RunSpeed + SprintSpeed) * 0.5f) State = MovementState.Run;
            else State = MovementState.Sprint;
        }

        private void OnDrawGizmosSelected()
        {
            var cc = GetComponent<CharacterController>();
            if (cc == null) return;

            Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.red;
            Vector3 origin = transform.position + Vector3.up * (cc.radius + 0.05f);
            Gizmos.DrawWireSphere(origin + Vector3.down * GroundProbeDistance, cc.radius);
        }
    }
}
