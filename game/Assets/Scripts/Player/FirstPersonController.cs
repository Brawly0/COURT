using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game
{
    /// <summary>
    /// First-person movement for the courthouse. Walk/sprint/crouch speeds are
    /// the GDD 04 walk-time targets made physical - do not retune casually, the
    /// map is calibrated against them.
    /// Feel: acceleration ramp (no ice-skating), head bob tied to stride,
    /// sprint FOV kick, lean into turns, landing dip, and a stamina system whose
    /// exhaustion is audible to other players (GDD 04: wheezing leaks position).
    /// The visible body is a PSX puppet driven by CharacterAnimator.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Speeds (GDD 04)")]
        public float WalkSpeed = 3.5f;
        public float SprintSpeed = 6.0f;
        public float CrouchSpeed = 1.8f;
        public float Acceleration = 14f;
        public float MouseSensitivity = 0.12f;
        public float Gravity = -20f;

        [Header("Stamina")]
        public float Stamina = 100f;
        public float SprintDrainPerSec = 15f;
        public float RegenPerSec = 10f;
        public float ExhaustedThreshold = 15f;

        [Header("Feel")]
        public float BobAmount = 0.045f;
        public float BobSpeed = 9.5f;
        public float LeanAmount = 1.8f;
        public float SprintFovKick = 6f;

        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public float SpeedMetresPerSec { get; private set; }
        /// <summary>0-1 exhaustion, used by the HUD and (later) proximity voice wheeze.</summary>
        public float Exhaustion => 1f - Mathf.Clamp01(Stamina / 100f);

        private CharacterController _cc;
        private Camera _cam;
        private Transform _camT;
        private CharacterAnimator _anim;
        private float _pitch, _bobPhase, _lean, _baseFov, _camBaseY;
        private float _fallSpeed;
        private bool _wasGrounded = true;
        private Vector3 _velocity;
        private const float StandHeight = 1.8f, CrouchHeight = 1.15f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cam = GetComponentInChildren<Camera>();
            _camT = _cam != null ? _cam.transform : transform;
            _baseFov = _cam != null ? _cam.fieldOfView : 70f;
            _camBaseY = _camT.localPosition.y;
            _anim = GetComponentInChildren<CharacterAnimator>();
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;

            // ---------------- look ----------------
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * MouseSensitivity;
                transform.Rotate(0f, delta.x, 0f);
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
                // lean into the turn - subtle, sells the weight
                _lean = Mathf.Lerp(_lean, Mathf.Clamp(-delta.x * 0.5f, -LeanAmount, LeanAmount), Time.deltaTime * 6f);
            }
            else _lean = Mathf.Lerp(_lean, 0f, Time.deltaTime * 6f);

            // ---------------- input ----------------
            Vector3 input = Vector3.zero;
            if (kb.wKey.isPressed) input += Vector3.forward;
            if (kb.sKey.isPressed) input += Vector3.back;
            if (kb.aKey.isPressed) input += Vector3.left;
            if (kb.dKey.isPressed) input += Vector3.right;
            input = Vector3.ClampMagnitude(input, 1f);
            bool moving = input.sqrMagnitude > 0.01f;

            // ---------------- crouch (blocked if something is overhead) ----------------
            bool wantCrouch = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
            if (IsCrouching && !wantCrouch &&
                Physics.SphereCast(transform.position + Vector3.up * CrouchHeight, _cc.radius * 0.9f,
                                   Vector3.up, out _, StandHeight - CrouchHeight))
                wantCrouch = true;   // ceiling above: stay down
            IsCrouching = wantCrouch;

            float targetH = IsCrouching ? CrouchHeight : StandHeight;
            _cc.height = Mathf.Lerp(_cc.height, targetH, Time.deltaTime * 10f);
            _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);

            // ---------------- stamina & sprint ----------------
            bool wantsSprint = kb.leftShiftKey.isPressed && moving && !IsCrouching;
            IsSprinting = wantsSprint && Stamina > 1f;
            Stamina = Mathf.Clamp(
                Stamina + (IsSprinting ? -SprintDrainPerSec : RegenPerSec) * Time.deltaTime, 0f, 100f);

            float target = IsCrouching ? CrouchSpeed : IsSprinting ? SprintSpeed : WalkSpeed;
            if (Stamina < ExhaustedThreshold) target *= 0.8f;   // winded: you slow down

            // ---------------- acceleration (no instant stops) ----------------
            Vector3 wish = transform.TransformDirection(input) * target;
            Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
            flat = Vector3.MoveTowards(flat, wish, Acceleration * Time.deltaTime);
            _velocity.x = flat.x; _velocity.z = flat.z;
            SpeedMetresPerSec = flat.magnitude;

            // ---------------- gravity & landing ----------------
            if (_cc.isGrounded)
            {
                if (!_wasGrounded) _landDip = 0.09f;    // little knee bend on touchdown
                _fallSpeed = -2f;
            }
            else _fallSpeed += Gravity * Time.deltaTime;
            _wasGrounded = _cc.isGrounded;
            _velocity.y = _fallSpeed;

            _cc.Move(_velocity * Time.deltaTime);

            UpdateCamera(moving);
            if (_anim != null)
            {
                _anim.SetSpeed(SpeedMetresPerSec);
                _anim.Stress = Exhaustion * 0.6f;       // winded reads as rattled
            }
        }

        private float _landDip;

        private void UpdateCamera(bool moving)
        {
            float dt = Time.deltaTime;

            // head bob follows the stride, so footsteps and view sync up
            float strideRate = BobSpeed * Mathf.Clamp01(SpeedMetresPerSec / WalkSpeed);
            if (moving && _cc.isGrounded) _bobPhase += dt * strideRate;
            else _bobPhase = Mathf.Lerp(_bobPhase, 0f, dt * 6f);

            float amp = BobAmount * (IsSprinting ? 1.5f : 1f) * Mathf.Clamp01(SpeedMetresPerSec / WalkSpeed);
            float bobY = Mathf.Abs(Mathf.Sin(_bobPhase)) * amp;
            float bobX = Mathf.Sin(_bobPhase * 0.5f) * amp * 0.6f;

            _landDip = Mathf.Lerp(_landDip, 0f, dt * 7f);
            float camY = (IsCrouching ? CrouchHeight - 0.2f : _camBaseY) + bobY - _landDip;

            _camT.localPosition = Vector3.Lerp(_camT.localPosition, new Vector3(bobX, camY, 0f), dt * 12f);
            _camT.localRotation = Quaternion.Euler(_pitch, 0f, _lean + Mathf.Sin(_bobPhase * 0.5f) * amp * 12f);

            if (_cam != null)
            {
                float wantFov = _baseFov + (IsSprinting ? SprintFovKick : 0f);
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, wantFov, dt * 6f);
            }
        }
    }
}
