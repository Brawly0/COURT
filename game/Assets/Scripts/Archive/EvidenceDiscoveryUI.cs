using System.Collections.Generic;
using UnityEngine;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// The "EVIDENCE FOUND" card, and the "nothing useful" line.
    ///
    /// Shows only what arrived in the discovery packet, which is already filtered.
    /// It has no access to the case, so it cannot leak developer metadata — there is
    /// no perpetrator, misleading flag or proof-chain position within reach of this
    /// class even if someone tried to print one.
    ///
    /// The empty result matters as much as the find: a four-second search that ends
    /// in silence reads as a broken game.
    /// </summary>
    public class EvidenceDiscoveryUI : MonoBehaviour
    {
        [Tooltip("Seconds the discovery card stays up.")]
        public float CardSeconds = 7f;

        [Tooltip("Seconds the 'nothing found' line stays up.")]
        public float EmptySeconds = 2.5f;

        [Tooltip("Hold to review what you have found so far.")]
        public UnityEngine.InputSystem.Key HistoryKey = UnityEngine.InputSystem.Key.Tab;

        private EvidenceDiscovery _card;
        private bool _hasCard;
        private float _cardUntil;

        private string _emptyText = "";
        private float _emptyUntil;

        /// <summary>This player's own finds. Local only — never another player's.</summary>
        private readonly List<EvidenceDiscovery> _history = new();

        private GUIStyle _title, _body, _small, _heading;
        private Texture2D _panel, _accent;

        private void Update()
        {
            var director = ArchiveDirector.Instance;
            if (director == null) return;

            // Re-subscription is cheap and survives the director spawning late.
            director.EvidenceDiscovered -= OnDiscovered;
            director.EvidenceDiscovered += OnDiscovered;
            director.SearchCameUpEmpty -= OnEmpty;
            director.SearchCameUpEmpty += OnEmpty;
        }

        private void OnDestroy()
        {
            var director = ArchiveDirector.Instance;
            if (director == null) return;
            director.EvidenceDiscovered -= OnDiscovered;
            director.SearchCameUpEmpty -= OnEmpty;
        }

        private void OnDiscovered(EvidenceDiscovery discovery)
        {
            _card = discovery;
            _hasCard = true;
            _cardUntil = Time.time + CardSeconds;
            _history.Add(discovery);
        }

        private void OnEmpty(string message)
        {
            _emptyText = string.IsNullOrEmpty(message) ? "Nothing useful found." : message;
            _emptyUntil = Time.time + EmptySeconds;
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (_hasCard && Time.time < _cardUntil) DrawCard();
            else _hasCard = false;

            if (Time.time < _emptyUntil) DrawEmpty();

            DrawHistory();
        }

        private void DrawCard()
        {
            float w = 460f, h = 190f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.22f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, w, 3f), _accent);

            GUILayout.BeginArea(new Rect(x + 20f, y + 14f, w - 40f, h - 28f));

            GUILayout.Label("EVIDENCE FOUND", _heading);
            GUILayout.Space(4f);
            GUILayout.Label(_card.Title.ToString(), _title);
            GUILayout.Label(_card.Kind.ToString(), _small);
            GUILayout.Space(6f);
            GUILayout.Label(_card.Description.ToString(), _body);

            GUILayout.EndArea();
        }

        private void DrawEmpty()
        {
            float w = 420f, h = 56f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.30f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 16f, y + 10f, w - 32f, h - 20f));
            GUILayout.Label("SEARCH COMPLETE", _small);
            GUILayout.Label(_emptyText, _body);
            GUILayout.EndArea();
        }

        /// <summary>Hold Tab for your own finds. Yours only — this is not a team feed.</summary>
        private void DrawHistory()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !System.Enum.IsDefined(typeof(UnityEngine.InputSystem.Key), HistoryKey)) return;
            if (!keyboard[HistoryKey].isPressed || _history.Count == 0) return;

            float w = 420f, h = 40f + _history.Count * 34f;
            float x = 16f, y = 160f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 14f, y + 10f, w - 28f, h - 20f));
            GUILayout.Label($"YOUR DISCOVERIES ({_history.Count})", _heading);

            foreach (var entry in _history)
            {
                GUILayout.Label(entry.Title.ToString(), _small);
                GUILayout.Label("   " + entry.Description.ToString(), _body);
            }

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_body != null) return;

            _heading = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
            _title.normal.textColor = Color.white;

            _body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _body.normal.textColor = new Color(0.9f, 0.9f, 0.88f);

            _small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _small.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

            _panel = Solid(new Color(0.05f, 0.05f, 0.07f, 0.95f));
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
