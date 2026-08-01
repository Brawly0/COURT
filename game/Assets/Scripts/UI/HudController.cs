using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Graybox HUD in the mockups' spirit: TIME REMAINING, EVIDENCE FOUND,
    /// stamina, interact prompt, event log. OnGUI is deliberate — zero setup,
    /// replaced by the real monospace UI in the art phase (GDD 10).
    /// </summary>
    public class HudController : MonoBehaviour
    {
        private FirstPersonController _player;
        private Interactor _interactor;
        private GUIStyle _label, _big, _log;

        private void Start()
        {
            _player = FindFirstObjectByType<FirstPersonController>();
            _interactor = FindFirstObjectByType<Interactor>();
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _log = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.85f, 0.85f, 0.8f) } };
        }

        private void OnGUI()
        {
            EnsureStyles();
            var rt = CaseRuntime.Instance;
            if (rt == null || rt.Case == null) return;
            if (_player == null) _player = FindFirstObjectByType<FirstPersonController>();
            if (_interactor == null) _interactor = FindFirstObjectByType<Interactor>();

            // top-left: clock + evidence
            int m = Mathf.FloorToInt(rt.TimeRemaining / 60f);
            int s = Mathf.FloorToInt(rt.TimeRemaining % 60f);
            GUI.Label(new Rect(20, 15, 400, 30), "TIME REMAINING:", _label);
            GUI.Label(new Rect(20, 35, 400, 40), $"{m:00}:{s:00}", _big);
            GUI.Label(new Rect(20, 75, 400, 30), $"EVIDENCE FOUND: {rt.EvidenceFound} / {rt.EvidenceTotal}", _label);

            // top-right: case identity
            GUI.Label(new Rect(Screen.width - 420, 15, 400, 30), rt.Case.Title + $"   (seed {rt.Case.Seed})",
                new GUIStyle(_label) { alignment = TextAnchor.UpperRight });

            // bottom-left: stamina
            if (_player != null)
            {
                GUI.Label(new Rect(20, Screen.height - 60, 200, 25), "STAMINA", _label);
                GUI.Box(new Rect(20, Screen.height - 35, 200, 14), GUIContent.none);
                GUI.Box(new Rect(20, Screen.height - 35, 200f * _player.Stamina / 100f, 14), GUIContent.none);
            }

            // center: interact prompt + hold progress
            if (_interactor != null && _interactor.CurrentPrompt != null)
            {
                var c = new Rect(Screen.width / 2f - 200, Screen.height / 2f + 40, 400, 30);
                GUI.Label(c, "[HOLD E]  " + _interactor.CurrentPrompt,
                    new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
                if (_interactor.HoldProgress > 0f)
                    GUI.Box(new Rect(Screen.width / 2f - 100, Screen.height / 2f + 72,
                        200f * _interactor.HoldProgress, 8), GUIContent.none);
            }

            // crosshair dot
            GUI.Label(new Rect(Screen.width / 2f - 4, Screen.height / 2f - 12, 20, 20), "·", _big);

            // bottom-right: log (last 8 lines)
            int shown = Mathf.Min(8, rt.Log.Count);
            for (int i = 0; i < shown; i++)
            {
                string line = rt.Log[rt.Log.Count - shown + i];
                GUI.Label(new Rect(Screen.width - 640, Screen.height - 30 - (shown - i) * 22, 620, 22), line, _log);
            }
        }
    }
}
