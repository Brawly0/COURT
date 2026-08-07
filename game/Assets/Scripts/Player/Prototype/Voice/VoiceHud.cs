using System.Linq;
using UnityEngine;
using Unity.Netcode;
using CaseClosed.Game.Prototype.Net;

namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: voice is invisible, so without feedback you cannot tell
    /// "my mic is broken" from "nobody is talking" from "they are out of range".
    /// This shows all three.
    ///
    /// Local  : device name, mic level meter, transmitting light, mute state.
    /// Remote : a speaking marker over each player, with their distance and whether
    ///          they are inside voice range.
    ///
    /// OnGUI, like the other prototype HUDs — a dev tool, not shipping UI.
    /// </summary>
    public class VoiceHud : MonoBehaviour
    {
        [Tooltip("Toggles mute.")]
        public UnityEngine.InputSystem.Key MuteKey = UnityEngine.InputSystem.Key.M;

        [Tooltip("Cycles to the next microphone.")]
        public UnityEngine.InputSystem.Key NextDeviceKey = UnityEngine.InputSystem.Key.N;

        [Tooltip("Hides the overlay without removing the component.")]
        public bool Visible = true;

        private PlayerVoice _local;
        private GUIStyle _label;
        private Texture2D _barBackground, _barFill, _panel;

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (_local == null || !_local.IsSpawned) _local = FindLocalVoice();
            if (_local == null) return;

            if (keyboard[MuteKey].wasPressedThisFrame) _local.IsMuted = !_local.IsMuted;
            if (keyboard[NextDeviceKey].wasPressedThisFrame && _local.Capture != null)
                _local.Capture.SelectNextDevice();
        }

        private static PlayerVoice FindLocalVoice()
        {
            return Object.FindObjectsByType<PlayerVoice>().FirstOrDefault(v => v.IsOwner);
        }

        private void OnGUI()
        {
            if (!Visible) return;
            EnsureStyles();

            DrawLocalPanel();
            DrawRemoteSpeakers();
        }

        private void DrawLocalPanel()
        {
            float x = 12f, y = Screen.height - 116f, w = 280f;

            GUI.DrawTexture(new Rect(x, y, w, 104f), _panel);

            if (_local == null)
            {
                GUI.Label(new Rect(x + 12f, y + 10f, w - 24f, 22f), "VOICE  -  no local player yet", _label);
                return;
            }

            var capture = _local.Capture;
            bool hasMic = capture != null && capture.HasMicrophone;
            string device = capture != null && !string.IsNullOrEmpty(capture.ActiveDevice)
                ? capture.ActiveDevice : "none";
            if (device.Length > 26) device = device.Substring(0, 26) + "...";

            string status = _local.IsMuted ? "MUTED"
                          : !hasMic ? "NO MIC"
                          : _local.IsSpeaking ? "TRANSMITTING"
                          : "idle";

            GUI.color = _local.IsMuted ? new Color(1f, 0.45f, 0.4f)
                      : _local.IsSpeaking ? new Color(0.45f, 1f, 0.5f)
                      : Color.white;
            GUI.Label(new Rect(x + 12f, y + 8f, w - 24f, 22f), $"VOICE  -  {status}", _label);
            GUI.color = Color.white;

            GUI.Label(new Rect(x + 12f, y + 28f, w - 24f, 20f), $"mic: {device}", _label);

            // Level meter, with the sensitivity gate marked so you can see whether
            // your voice is actually clearing it.
            float level = capture != null ? Mathf.Clamp01(capture.InputLevel * 6f) : 0f;
            var meter = new Rect(x + 12f, y + 52f, w - 24f, 12f);
            GUI.DrawTexture(meter, _barBackground);
            GUI.DrawTexture(new Rect(meter.x, meter.y, meter.width * level, meter.height), _barFill);

            if (capture != null)
            {
                float gate = Mathf.Clamp01(capture.InputSensitivity * 6f);
                GUI.DrawTexture(new Rect(meter.x + meter.width * gate, meter.y - 2f, 2f, meter.height + 4f), _barBackground);
            }

            GUI.Label(new Rect(x + 12f, y + 70f, w - 24f, 20f),
                $"hold {(capture != null ? capture.PushToTalkKey.ToString() : "V")} to talk  ·  " +
                $"{MuteKey} mute  ·  {NextDeviceKey} device", _label);

            GUI.Label(new Rect(x + 12f, y + 86f, w - 24f, 20f),
                $"range: {_local.MaxVoiceDistance:0} m", _label);
        }

        /// <summary>
        /// A marker over anyone currently speaking, plus their distance. This is what
        /// makes the proximity test observable: walk away and watch the distance
        /// climb past the range figure as the voice fades out.
        /// </summary>
        private void DrawRemoteSpeakers()
        {
            var camera = Camera.main;
            if (camera == null || _local == null) return;

            foreach (var voice in Object.FindObjectsByType<PlayerVoice>())
            {
                if (voice.IsOwner) continue;

                float distance = Vector3.Distance(voice.transform.position, _local.transform.position);
                bool inRange = distance <= voice.MaxVoiceDistance;
                if (!voice.IsSpeaking && !inRange) continue;

                Vector3 head = voice.transform.position + Vector3.up * 2.1f;
                Vector3 screen = camera.WorldToScreenPoint(head);
                if (screen.z <= 0f) continue;

                float sx = screen.x - 70f;
                float sy = Screen.height - screen.y;

                GUI.color = voice.IsSpeaking
                    ? (inRange ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.7f, 0.3f))
                    : new Color(1f, 1f, 1f, 0.45f);

                // IsSpeaking now means "audio is reaching me", so a marker only appears
                // for players you can genuinely hear.
                string tag = voice.IsSpeaking ? "((( speaking )))" : "";

                GUI.Label(new Rect(sx, sy, 140f, 20f), $"{tag}", _label);

                float occlusion = voice.Occlusion;
                string blocked = occlusion > 0.02f ? $"  walls {occlusion * 100f:0}%" : "";
                GUI.Label(new Rect(sx, sy + 16f, 180f, 20f), $"{distance:0.0} m{blocked}", _label);
                GUI.color = Color.white;
            }
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperLeft };
            _label.normal.textColor = Color.white;

            _panel = Solid(new Color(0f, 0f, 0f, 0.66f));
            _barBackground = Solid(new Color(1f, 1f, 1f, 0.22f));
            _barFill = Solid(new Color(0.4f, 0.85f, 1f, 0.95f));
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
