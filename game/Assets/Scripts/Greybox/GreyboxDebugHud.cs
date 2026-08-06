using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using CaseClosed.Game.Prototype;

namespace CaseClosed.Game.Greybox
{
    /// <summary>
    /// WHY THIS EXISTS: a greybox is a set of claims about distance and pacing, and
    /// you cannot evaluate those by looking at the scene view. This measures them
    /// while you play.
    ///
    /// Shows current room, coordinates, speed, and — the important one — how long
    /// each journey between rooms actually took. Timings are recorded automatically
    /// whenever you cross from one named room into another, so you get real numbers
    /// from normal play rather than from a stopwatch.
    ///
    /// Corridors are marked transitional and are skipped, so a leg reads
    /// "Atrium -> Archive" rather than being chopped into three by the hallway.
    /// </summary>
    public class GreyboxDebugHud : MonoBehaviour
    {
        [Tooltip("Toggles the overlay.")]
        public Key ToggleKey = Key.F3;

        [Tooltip("Clears the recorded travel times.")]
        public Key ClearKey = Key.F4;

        public bool Visible = true;

        [Tooltip("How many recent journeys to keep on screen.")]
        public int HistoryLength = 6;

        private readonly List<string> _history = new();
        private RoomVolume[] _rooms;
        private PlayerMovement _player;

        private string _currentRoom = "-";
        private string _lastNamedRoom = "-";   // last non-corridor room we were in
        private float _legStartTime;

        private GUIStyle _label;
        private Texture2D _panel;

        private void Start() => _rooms = FindObjectsByType<RoomVolume>(FindObjectsInactive.Exclude);

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[ToggleKey].wasPressedThisFrame) Visible = !Visible;
                if (keyboard[ClearKey].wasPressedThisFrame) _history.Clear();
            }

            // The local player only exists once a session spawns one.
            if (_player == null || !_player.isActiveAndEnabled)
            {
                _player = FindObjectsByType<PlayerMovement>()
                    .FirstOrDefault(m => m.enabled && m.GetComponent<CharacterController>() != null
                                         && m.GetComponent<CharacterController>().enabled);
                if (_player == null) return;
            }

            if (_rooms == null || _rooms.Length == 0)
                _rooms = FindObjectsByType<RoomVolume>(FindObjectsInactive.Exclude);

            UpdateRoom(_player.transform.position);
        }

        private void UpdateRoom(Vector3 position)
        {
            var volume = _rooms.FirstOrDefault(r => r != null && r.Contains(position));
            string room = volume != null ? volume.RoomName : "outside";
            if (room == _currentRoom) return;

            _currentRoom = room;

            // Only time journeys between real rooms; hallways are part of the trip.
            if (volume == null || volume.IsTransitional) return;

            if (_lastNamedRoom != "-" && _lastNamedRoom != room)
            {
                float seconds = Time.time - _legStartTime;
                _history.Insert(0, $"{_lastNamedRoom} -> {room}   {seconds:0.0}s");
                if (_history.Count > HistoryLength) _history.RemoveAt(_history.Count - 1);
            }

            _lastNamedRoom = room;
            _legStartTime = Time.time;
        }

        private void OnGUI()
        {
            if (!Visible) return;
            EnsureStyles();

            float w = 300f, h = 150f + HistoryLength * 16f;
            float x = Screen.width - w - 12f, y = Screen.height - h - 12f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 12f, y + 10f, w - 24f, h - 20f));

            GUILayout.Label("GREYBOX DEBUG   (F3 hide · F4 clear)", _label);
            GUILayout.Space(4f);

            if (_player == null)
            {
                GUILayout.Label("no local player yet", _label);
                GUILayout.EndArea();
                return;
            }

            Vector3 p = _player.transform.position;
            GUILayout.Label($"room      {_currentRoom}", _label);
            GUILayout.Label($"position  {p.x:0.0}, {p.y:0.0}, {p.z:0.0}", _label);
            GUILayout.Label($"speed     {_player.CurrentSpeed:0.00} m/s   ({_player.State})", _label);
            GUILayout.Label($"grounded  {_player.IsGrounded}", _label);

            GUILayout.Space(6f);
            GUILayout.Label("TRAVEL TIMES", _label);
            if (_history.Count == 0) GUILayout.Label("  walk between rooms to record", _label);
            foreach (string entry in _history) GUILayout.Label("  " + entry, _label);

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _label.normal.textColor = Color.white;

            _panel = new Texture2D(1, 1);
            _panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.68f));
            _panel.Apply();
        }
    }
}
