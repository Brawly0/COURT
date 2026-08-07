using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// WHY THIS EXISTS: so you can see what the controller thinks is happening
    /// without opening the Inspector mid-play. When jumping feels wrong, this tells
    /// you whether it is a movement bug (Grounded flickering) or an animation bug
    /// (Movement State correct, Animation State stuck).
    ///
    /// Deliberately OnGUI — zero setup, no Canvas, no prefab wiring. It is a
    /// development tool, not shipping UI. Delete the component and nothing breaks.
    /// </summary>
    public class PlayerDebugHud : MonoBehaviour
    {
        [Tooltip("Leave empty and it finds the player automatically.")]
        public PlayerMovement Movement;

        [Tooltip("Leave empty and it uses the driver on the same object as Movement.")]
        public PlayerAnimatorDriver AnimatorDriver;

        [Tooltip("Turn the overlay off without removing the component.")]
        public bool Visible = true;

        // Renamed from the old KeyCode-typed "ToggleKey" on purpose: scenes saved
        // before the switch stored KeyCode.F1 (282), which is out of range for
        // InputSystem.Key and threw every frame. A new name forces a clean default.
        [Tooltip("Toggles the overlay at runtime.")]
        public Key HudToggleKey = Key.F1;

        private GUIStyle _style;
        private GUIStyle _boxStyle;

        private void Awake()
        {
            if (Movement == null) Movement = FindAnyObjectByType<PlayerMovement>();
            if (AnimatorDriver == null && Movement != null)
                AnimatorDriver = Movement.GetComponent<PlayerAnimatorDriver>();
        }

        private void Update()
        {
            // Read the device directly rather than going through PlayerInputReader:
            // a debug toggle is not gameplay input and should not live in the
            // controller's input contract. This project has the legacy Input
            // Manager disabled, so it must be the Input System device API.
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Indexing the keyboard with an out-of-range Key throws, so never pass
            // one through unchecked.
            if (HudToggleKey == Key.None || !System.Enum.IsDefined(typeof(Key), HudToggleKey)) return;

            if (keyboard[HudToggleKey].wasPressedThisFrame) Visible = !Visible;
        }

        private void OnGUI()
        {
            if (!Visible || Movement == null) return;

            EnsureStyles();

            GUI.Box(new Rect(12, 12, 260, 132), GUIContent.none, _boxStyle);
            GUILayout.BeginArea(new Rect(24, 22, 240, 116));

            GUILayout.Label($"Speed:            {Movement.CurrentSpeed:0.00} m/s", _style);
            GUILayout.Label($"Grounded:         {Movement.IsGrounded}", _style);
            GUILayout.Label($"Movement State:   {Movement.State}", _style);
            GUILayout.Label($"Animation State:  {(AnimatorDriver != null ? AnimatorDriver.CurrentAnimationState : "-")}", _style);
            GUILayout.Label($"Slope:            {Movement.GroundAngle:0}°{(Movement.OnSteepSlope ? "  (sliding)" : "")}", _style);

            GUILayout.EndArea();

            GUI.Label(new Rect(12, 150, 400, 20),
                "WASD move  ·  Shift sprint  ·  Ctrl walk  ·  Space jump  ·  Esc cursor  ·  F1 hide",
                _style);
        }

        private void EnsureStyles()
        {
            if (_style != null) return;

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = false
            };
            _style.normal.textColor = Color.white;

            // A flat dark panel so the text stays readable over a bright floor.
            var background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
            background.Apply();

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = background;
        }
    }
}
