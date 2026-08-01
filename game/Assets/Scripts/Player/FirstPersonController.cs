using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game
{
    /// <summary>
    /// Graybox first-person controller. Walk/sprint speeds are the GDD 04
    /// walk-time targets made physical — do not change them casually; the
    /// map is calibrated against them.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public float WalkSpeed = 3.5f;
        public float SprintSpeed = 6.0f;
        public float MouseSensitivity = 0.12f;
        public float Gravity = -20f;

        [Header("Stamina (GDD 04)")]
        public float Stamina = 100f;
        public float SprintDrainPerSec = 15f;
        public float RegenPerSec = 10f;

        private CharacterController _cc;
        private Transform _cam;
        private float _pitch;
        private float _fallSpeed;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cam = GetComponentInChildren<Camera>().transform;
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

            // look
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * MouseSensitivity;
                transform.Rotate(0f, delta.x, 0f);
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
                _cam.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            // move
            Vector3 input = Vector3.zero;
            if (kb.wKey.isPressed) input += Vector3.forward;
            if (kb.sKey.isPressed) input += Vector3.back;
            if (kb.aKey.isPressed) input += Vector3.left;
            if (kb.dKey.isPressed) input += Vector3.right;
            input = Vector3.ClampMagnitude(input, 1f);

            bool wantsSprint = kb.leftShiftKey.isPressed && input.sqrMagnitude > 0.01f;
            bool sprinting = wantsSprint && Stamina > 0f;
            Stamina = Mathf.Clamp(
                Stamina + (sprinting ? -SprintDrainPerSec : RegenPerSec) * Time.deltaTime,
                0f, 100f);

            float speed = sprinting ? SprintSpeed : WalkSpeed;
            Vector3 move = transform.TransformDirection(input) * speed;

            _fallSpeed = _cc.isGrounded ? -1f : _fallSpeed + Gravity * Time.deltaTime;
            move.y = _fallSpeed;
            _cc.Move(move * Time.deltaTime);
        }
    }
}
