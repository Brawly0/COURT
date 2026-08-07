using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Prototype.Net
{
    /// <summary>
    /// WHY THIS EXISTS: position and rotation are replicated by ClientNetworkTransform,
    /// but that says nothing about what the body is DOING. Without this, remote
    /// players would glide around frozen in an idle pose.
    ///
    /// We do NOT send animations. We send four small numbers and let every machine
    /// run the same Animator Controller off them. Remote characters therefore blend
    /// Idle -> Walk -> Run -> Sprint using the identical state machine, for a few
    /// bytes a tick instead of a stream of animation state.
    ///
    /// Owner  : reads PlayerMovement, writes the NetworkVariables.
    /// Remote : reads the NetworkVariables, writes the Animator.
    ///
    /// Jump and Land are Animator *triggers*, so they cannot simply be a value that
    /// is polled — they are fired on the frame the replicated MovementState changes
    /// into them. That is the same rule PlayerAnimatorDriver uses locally, which is
    /// why both ends agree without any RPCs.
    /// </summary>
    [RequireComponent(typeof(PrototypeNetPlayer))]
    public class PlayerNetworkSync : NetworkBehaviour
    {
        [Tooltip("Leave empty and it finds the Animator on this object or a child.")]
        public Animator Animator;

        [Tooltip("Seconds of damping applied to Speed on remote copies, matching the local driver.")]
        public float RemoteSpeedDamping = 0.09f;

        // Owner writes, everyone reads. That permission pair is what makes this
        // owner-authoritative: no other client can rewrite your animation state.
        private readonly NetworkVariable<float> _speed = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<float> _verticalSpeed = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> _grounded = new(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<MovementState> _state = new(
            MovementState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// What actually arrived over the network. On a remote copy PlayerMovement is
        /// switched off and reports zero, so these are the only honest numbers — and
        /// the only way to tell "the animation is not syncing" from "nothing arrived".
        /// </summary>
        public float ReplicatedSpeed => _speed.Value;
        public bool ReplicatedGrounded => _grounded.Value;
        public MovementState ReplicatedState => _state.Value;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int VerticalHash = Animator.StringToHash("VerticalSpeed");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int LandHash = Animator.StringToHash("Land");

        private PlayerMovement _movement;
        private MovementState _previousRemoteState = MovementState.Idle;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (IsOwner) PublishLocalState();
            else ApplyRemoteState();
        }

        /// <summary>
        /// Owner side. Writing a NetworkVariable only costs bandwidth when the value
        /// actually changes, so assigning every frame is fine — NGO dedupes.
        /// </summary>
        private void PublishLocalState()
        {
            if (_movement == null) return;

            _speed.Value = _movement.CurrentSpeed;
            _verticalSpeed.Value = _movement.VerticalSpeed;
            _grounded.Value = _movement.IsGrounded;
            _state.Value = _movement.State;
        }

        /// <summary>
        /// Remote side. Feeds the same Animator parameters the local driver would,
        /// so one Animator Controller serves both cases.
        /// </summary>
        private void ApplyRemoteState()
        {
            if (Animator == null) return;

            Animator.SetFloat(SpeedHash, _speed.Value, RemoteSpeedDamping, Time.deltaTime);
            Animator.SetBool(GroundedHash, _grounded.Value);
            Animator.SetFloat(VerticalHash, _verticalSpeed.Value);

            MovementState current = _state.Value;
            if (current != _previousRemoteState)
            {
                if (current == MovementState.Jump) Animator.SetTrigger(JumpHash);
                else if (current == MovementState.Land) Animator.SetTrigger(LandHash);
                _previousRemoteState = current;
            }
        }
    }
}
