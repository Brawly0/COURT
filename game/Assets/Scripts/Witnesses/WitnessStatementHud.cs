using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Witnesses
{
    /// <summary>
    /// Shows the statement you just took, and lets you re-read the ones you already
    /// have.
    ///
    /// Everything drawn here arrived in a WitnessTestimony packet addressed to this
    /// client, so there is no path from this screen back to case truth — not because
    /// the drawing code is careful, but because the packet contains nothing else.
    ///
    /// Placeholder presentation.
    /// </summary>
    public class WitnessStatementHud : MonoBehaviour
    {
        [Tooltip("Closes the open statement.")]
        public Key CloseKey = Key.Escape;

        [Tooltip("Hold to list every statement you have taken.")]
        public Key NotebookKey = Key.B;

        [Tooltip("Toggles the HOST-ONLY witness inspector. Does nothing on a client.")]
        public Key DevKey = Key.F7;

        private WitnessTestimony? _open;
        private bool _dev;

        private GUIStyle _heading, _body, _small, _quote;
        private Texture2D _panel, _accent;

        private void Update()
        {
            var director = WitnessDirector.Instance;
            if (director != null)
            {
                director.StatementReceived -= OnStatement;
                director.StatementReceived += OnStatement;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (System.Enum.IsDefined(typeof(Key), CloseKey) && keyboard[CloseKey].wasPressedThisFrame)
                _open = null;

            if (System.Enum.IsDefined(typeof(Key), DevKey) && keyboard[DevKey].wasPressedThisFrame)
                _dev = !_dev;
        }

        private void OnDestroy()
        {
            var director = WitnessDirector.Instance;
            if (director != null) director.StatementReceived -= OnStatement;
        }

        private void OnStatement(WitnessTestimony testimony) => _open = testimony;

        private void OnGUI()
        {
            EnsureStyles();
            DrawStatement();
            DrawNotebook();
            DrawDev();
        }

        /// <summary>
        /// The statement itself. Prose only — there is deliberately no confidence
        /// meter, no "this line may be unreliable", no source marker. The player is
        /// meant to weigh it against the Archive and against other witnesses, which
        /// only works if the game refuses to grade it for them.
        /// </summary>
        private void DrawStatement()
        {
            if (_open == null) return;
            var t = _open.Value;

            float w = 520f, h = 240f;
            float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, 4f, h), _accent);

            GUI.Label(new Rect(x + 18f, y + 12f, w - 36f, 20f), "WITNESS STATEMENT", _small);
            GUI.Label(new Rect(x + 18f, y + 32f, w - 36f, 26f), t.DisplayName.ToString(), _heading);
            GUI.Label(new Rect(x + 18f, y + 66f, w - 36f, h - 110f),
                      "“" + t.Statement.ToString() + "”", _quote);

            if (GUI.Button(new Rect(x + w - 96f, y + h - 38f, 78f, 26f), "Close")) _open = null;
            GUI.Label(new Rect(x + 18f, y + h - 34f, 220f, 20f), $"[{CloseKey}] close", _small);
        }

        /// <summary>Everything you have taken. Never shrinks, and never fills in for you.</summary>
        private void DrawNotebook()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !System.Enum.IsDefined(typeof(Key), NotebookKey)) return;
            if (!keyboard[NotebookKey].isPressed) return;

            var known = WitnessDirector.KnownStatements;
            float w = 440f;
            float h = 46f + Mathf.Max(1, known.Count) * 74f;
            float x = Screen.width - w - 16f, y = 150f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 14f, y + 10f, w - 28f, h - 20f));
            GUILayout.Label($"STATEMENTS YOU HAVE TAKEN ({known.Count})", _heading);
            GUILayout.Space(4f);

            if (known.Count == 0)
            {
                GUILayout.Label("None yet. Interview someone in the Witness Lounge.", _small);
            }
            else
            {
                foreach (var entry in known.Values)
                {
                    GUILayout.Label(entry.DisplayName.ToString(), _body);
                    GUILayout.Label(entry.Statement.ToString(), _small);
                    GUILayout.Space(6f);
                }
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// HOST-ONLY inspector. DeveloperDump returns an empty string on a client,
        /// so a modified build pressing this key still sees nothing — the truth is
        /// not on that machine to print.
        /// </summary>
        private void DrawDev()
        {
            if (!_dev) return;

            var director = WitnessDirector.Instance;
            string dump = director != null ? director.DeveloperDump() : "";

            if (string.IsNullOrEmpty(dump))
            {
                GUI.Label(new Rect(16f, Screen.height - 40f, 520f, 24f),
                          "witness dev inspector: host only", _small);
                return;
            }

            float w = 700f, h = Screen.height - 200f;
            GUI.DrawTexture(new Rect(12f, 100f, w, h), _panel);
            GUI.Label(new Rect(24f, 108f, w - 24f, h - 16f), dump, _small);
        }

        private void EnsureStyles()
        {
            if (_body != null) return;

            _heading = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _body.normal.textColor = Color.white;

            _quote = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, fontStyle = FontStyle.Italic };
            _quote.normal.textColor = new Color(0.93f, 0.93f, 0.9f);

            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _small.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

            _panel = Solid(new Color(0.05f, 0.05f, 0.07f, 0.94f));
            _accent = Solid(new Color(1f, 0.85f, 0.45f));
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
