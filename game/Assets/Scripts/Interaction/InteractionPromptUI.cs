using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Interaction
{
    /// <summary>
    /// The "[E] Open Drawer" line, the hold bar, and the short refusal messages.
    ///
    /// Reads only what the local player can already see: the object it is looking at,
    /// and the server's answer to its own request. It never enumerates other players
    /// or asks the roster anything, so it cannot become a leak channel — a busy shelf
    /// says "someone is using that", never who.
    ///
    /// Placeholder styling. OnGUI, no Canvas, no dependencies.
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        [Tooltip("How long a rejection or completion message stays on screen.")]
        public float MessageSeconds = 2.2f;

        private PlayerInteractionDetector _detector;
        private string _message = "";
        private float _messageUntil;
        private bool _messageIsBad;

        /// <summary>Set the instant a key goes down, so the press is acknowledged locally.</summary>
        private float _pressFlashUntil;

        /// <summary>Set when the server answers, so the answer is acknowledged too.</summary>
        private float _resultFlashUntil;
        private bool _resultWasGood;

        private GUIStyle _prompt, _hint, _messageStyle;
        private Texture2D _panel, _barBack, _barFill, _dot;

        private void OnEnable()
        {
            var controller = InteractionNetworkController.Instance;
            if (controller != null) controller.ResponseReceived += OnResponse;
        }

        private void OnDisable()
        {
            var controller = InteractionNetworkController.Instance;
            if (controller != null) controller.ResponseReceived -= OnResponse;
        }

        private void Update()
        {
            // The controller spawns with the session, so subscribing may have to wait.
            var controller = InteractionNetworkController.Instance;
            if (controller != null)
            {
                controller.ResponseReceived -= OnResponse;
                controller.ResponseReceived += OnResponse;
            }

            var found = _detector;
            if (found == null || !found.isActiveAndEnabled) found = FindLocalDetector();

            if (!ReferenceEquals(found, _detector))
            {
                if (_detector != null) _detector.Pressed -= OnPressed;
                _detector = found;
                if (_detector != null) _detector.Pressed += OnPressed;
            }
        }

        private void OnPressed() => _pressFlashUntil = Time.time + 0.16f;

        private static PlayerInteractionDetector FindLocalDetector() =>
            Object.FindObjectsByType<PlayerInteractionDetector>(FindObjectsSortMode.None)
                  .FirstOrDefault(d => d.isActiveAndEnabled);

        private void OnResponse(InteractionResponse response)
        {
            // "Started" is not worth a message — the progress bar already says it.
            if (response.Outcome == InteractionOutcome.Started) return;

            // Flash the crosshair on the verdict: green for done, red for refused.
            // This is the "did that register?" signal, separate from reading the text.
            _resultFlashUntil = Time.time + 0.30f;
            _resultWasGood = !response.IsRejection;

            string text = response.Message.ToString();
            if (string.IsNullOrEmpty(text)) text = InteractionResponse.DefaultMessage(response.Outcome);
            if (string.IsNullOrEmpty(text)) return;

            _message = text;
            _messageIsBad = response.IsRejection;
            _messageUntil = Time.time + MessageSeconds;
        }

        private void OnGUI()
        {
            EnsureStyles();

            DrawCrosshair();
            DrawPrompt();
            DrawMessage();
        }

        /// <summary>
        /// The crosshair is the main "it registered" signal: it grows when something
        /// is targetable, punches out on a press, and flashes green or red on the
        /// server's answer. Pressing a key and seeing absolutely nothing move is what
        /// makes a system feel broken even when it is working.
        /// </summary>
        private void DrawCrosshair()
        {
            bool hot = _detector != null && _detector.Target != null;
            bool pressing = Time.time < _pressFlashUntil;
            bool result = Time.time < _resultFlashUntil;

            float size = hot ? 8f : 4f;
            if (pressing) size += 10f;              // punch on press
            if (result) size += 6f;

            Color color;
            if (result) color = _resultWasGood ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.4f);
            else if (pressing) color = Color.white;
            else if (hot) color = new Color(1f, 0.85f, 0.45f, 0.95f);
            else color = new Color(1f, 1f, 1f, 0.35f);

            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - size * 0.5f,
                                     Screen.height * 0.5f - size * 0.5f, size, size), _dot);
            GUI.color = previous;
        }

        private void DrawPrompt()
        {
            if (_detector == null) return;

            var target = _detector.Target;
            if (target == null || !target.IsSpawned) return;

            // Without a spawned controller the key genuinely does nothing, and
            // silence is indistinguishable from a bug. Say so.
            if (InteractionNetworkController.Instance == null)
            {
                float hw = 420f;
                GUI.Label(new Rect((Screen.width - hw) * 0.5f, Screen.height * 0.5f + 46f, hw, 24f),
                    "Interaction needs a session - press HOST or JOIN", _hint);
                return;
            }

            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            // Busy is shown instead of the prompt, so the player learns it before
            // pressing rather than by being refused.
            bool busy = target.IsLockedByOther(me);
            string text = busy
                ? "Someone is using that"
                : $"[{_detector.InteractKey}]  {target.PromptFor(me)}";

            float w = 380f, h = 34f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.5f + 44f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);

            var previous = GUI.color;
            if (busy) GUI.color = new Color(1f, 0.7f, 0.4f);
            GUI.Label(new Rect(x, y + 7f, w, 24f), text, _prompt);
            GUI.color = previous;

            DrawHoldBar(target, x, y + h + 4f, w);
        }

        /// <summary>
        /// Progress is a local estimate — the server owns the real clock. It exists so
        /// the hold feels responsive, not so it decides anything.
        /// </summary>
        private void DrawHoldBar(NetworkInteractable target, float x, float y, float w)
        {
            var controller = InteractionNetworkController.Instance;
            if (controller == null || !target.IsHold) return;
            if (controller.LocalHoldTarget != target.NetworkObjectId) return;

            float progress = Mathf.Clamp01(controller.LocalHoldProgress);
            if (progress <= 0f) return;

            var bar = new Rect(x + 40f, y, w - 80f, 8f);
            GUI.DrawTexture(bar, _barBack);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * progress, bar.height), _barFill);

            GUI.Label(new Rect(x, y + 10f, w, 18f), $"hold {_detector.InteractKey}...", _hint);
        }

        private void DrawMessage()
        {
            if (Time.time > _messageUntil || string.IsNullOrEmpty(_message)) return;

            float w = 420f, h = 28f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.5f + 110f;

            var previous = GUI.color;
            GUI.color = _messageIsBad ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 1f, 0.65f);
            GUI.Label(new Rect(x, y, w, h), _message, _messageStyle);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_prompt != null) return;

            _prompt = new GUIStyle(GUI.skin.label)
            { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _prompt.normal.textColor = Color.white;

            _hint = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            _hint.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

            _messageStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _messageStyle.normal.textColor = Color.white;

            _panel = Solid(new Color(0f, 0f, 0f, 0.62f));
            _barBack = Solid(new Color(1f, 1f, 1f, 0.22f));
            _barFill = Solid(new Color(1f, 0.85f, 0.45f, 0.95f));
            _dot = Solid(Color.white);
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
