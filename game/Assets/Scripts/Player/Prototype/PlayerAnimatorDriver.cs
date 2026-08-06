using UnityEngine;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// WHY THIS EXISTS: the bridge between physics and the Animator, and the only
    /// script that knows the Animator's parameter names.
    ///
    /// It is deliberately one-way — it reads PlayerMovement and writes to the
    /// Animator. Animation never pushes the character around (no root motion), so
    /// a missing or broken animation can never break movement. That also means
    /// swapping these placeholder clips for real ones later touches nothing else.
    ///
    /// Every value written here comes from measured velocity, so the state machine
    /// follows what the body is actually doing rather than what a button did.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Leave empty and it finds the Animator on this object or a child.")]
        public Animator Animator;

        [Header("Smoothing")]
        [Tooltip("Seconds of damping on the Speed parameter, so the blend doesn't jitter.")]
        public float SpeedDamping = 0.09f;

        // Hashes instead of strings: resolved once, cheaper every frame, and a typo
        // becomes a single obvious failure instead of a silent no-op scattered around.
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int VerticalHash = Animator.StringToHash("VerticalSpeed");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int LandHash = Animator.StringToHash("Land");

        private PlayerMovement _movement;
        private MovementState _previousState;

        /// <summary>Name of the state the Animator is actually in. Read by the debug HUD.</summary>
        public string CurrentAnimationState { get; private set; } = "-";

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (Animator == null || _movement == null) return;

            // Damped so the walk/run/sprint blend eases instead of snapping.
            Animator.SetFloat(SpeedHash, _movement.CurrentSpeed, SpeedDamping, Time.deltaTime);
            Animator.SetBool(GroundedHash, _movement.IsGrounded);
            Animator.SetFloat(VerticalHash, _movement.VerticalSpeed);

            // Triggers fire on the transition into a state, not while in it.
            MovementState state = _movement.State;
            if (state != _previousState)
            {
                if (state == MovementState.Jump) Animator.SetTrigger(JumpHash);
                else if (state == MovementState.Land) Animator.SetTrigger(LandHash);
                _previousState = state;
            }

            CacheAnimationStateName();
        }

        /// <summary>
        /// Reads back what the Animator actually chose, so the HUD can show the
        /// real animation state rather than what we assume it should be. That gap
        /// is exactly where animation bugs hide.
        /// </summary>
        private void CacheAnimationStateName()
        {
            var info = Animator.GetCurrentAnimatorStateInfo(0);

            if (info.IsName("Locomotion")) CurrentAnimationState = "Locomotion";
            else if (info.IsName("Jump")) CurrentAnimationState = "Jump";
            else if (info.IsName("Fall")) CurrentAnimationState = "Fall";
            else if (info.IsName("Land")) CurrentAnimationState = "Land";
            else CurrentAnimationState = "(transition)";
        }
    }
}
