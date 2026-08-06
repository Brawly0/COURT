using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// WHY THIS EXISTS: it is the only script that knows a keyboard and mouse
    /// exist. Everything else (movement, camera) just reads the properties below.
    ///
    /// That separation is what makes this multiplayer-ready later: when the time
    /// comes, a remote player's inputs get written into these same fields from
    /// the network instead of from the local keyboard, and PlayerMovement never
    /// has to change. Same reason a gamepad or rebindable controls can be added
    /// here without touching anything else.
    ///
    /// Reads devices directly (Keyboard.current / Mouse.current) to match the
    /// convention already used by FirstPersonController in this project.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [Header("Mouse")]
        [Tooltip("Degrees of camera rotation per unit of mouse movement.")]
        public float MouseSensitivity = 0.12f;

        [Tooltip("Lock and hide the cursor on start. Escape toggles it.")]
        public bool LockCursorOnStart = true;

        /// <summary>WASD as a -1..1 vector. y = forward/back, x = strafe.</summary>
        public Vector2 Move { get; private set; }

        /// <summary>Mouse delta for this frame, already scaled by sensitivity.</summary>
        public Vector2 Look { get; private set; }

        /// <summary>True only on the single frame Space went down.</summary>
        public bool JumpPressedThisFrame { get; private set; }

        /// <summary>True while Shift is held.</summary>
        public bool SprintHeld { get; private set; }

        /// <summary>True while Left Ctrl is held. Without this, keyboard input is
        /// always full-tilt and there is no way to tell walking from running.</summary>
        public bool WalkHeld { get; private set; }

        /// <summary>False while the cursor is unlocked, so the camera stops following the mouse.</summary>
        public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        private void Start()
        {
            if (LockCursorOnStart) SetCursorLocked(true);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard == null)
            {
                // No keyboard (headless, or focus lost). Report "no input" rather
                // than leaving stale values that would make the player run forever.
                Move = Vector2.zero;
                Look = Vector2.zero;
                JumpPressedThisFrame = false;
                SprintHeld = false;
                WalkHeld = false;
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(!CursorLocked);

            Vector2 move = Vector2.zero;
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;

            // Clamp instead of normalize: holding W alone must still give 1.0,
            // but W+D diagonally must not give 1.41 ("diagonal speed boost").
            Move = Vector2.ClampMagnitude(move, 1f);

            SprintHeld = keyboard.leftShiftKey.isPressed;
            WalkHeld = keyboard.leftCtrlKey.isPressed;
            JumpPressedThisFrame = keyboard.spaceKey.wasPressedThisFrame;

            Look = (mouse != null && CursorLocked)
                ? mouse.delta.ReadValue() * MouseSensitivity
                : Vector2.zero;
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
