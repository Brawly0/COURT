using System.Linq;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Mockup-canon HUD (GDD 10): white monospace, ALL-CAPS labels, hard
    /// rectangles, no icons where a word fits. TIME REMAINING + EVIDENCE FOUND
    /// top-left, INTEGRITY (green) + STAMINA (blue) bars bottom-left, 5-slot
    /// hotbar bottom-center, CURRENT OBJECTIVES checklist top-right, event log
    /// bottom-right. Still OnGUI - replaced by uGUI in the art phase.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        private FirstPersonController _player;
        private Interactor _interactor;
        private Font _mono;
        private GUIStyle _label, _big, _log, _obj, _slot;
        private Texture2D _texDark, _texGreen, _texBlue, _texWhite;

        private void Start()
        {
            _mono = Font.CreateDynamicFontFromOSFont("Courier New", 16);
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { font = _mono, fontSize = 15, normal = { textColor = Color.white } };
            _big = new GUIStyle(_label) { fontSize = 30, fontStyle = FontStyle.Bold };
            _log = new GUIStyle(_label) { fontSize = 13, normal = { textColor = new Color(0.85f, 0.85f, 0.78f) } };
            _obj = new GUIStyle(_label) { fontSize = 13 };
            _slot = new GUIStyle(_label) { fontSize = 11, alignment = TextAnchor.LowerCenter,
                normal = { textColor = new Color(0.8f, 0.8f, 0.75f) } };
            _texDark = Tex(new Color(0f, 0f, 0f, 0.55f));
            _texGreen = Tex(new Color(0.30f, 0.75f, 0.30f));
            _texBlue = Tex(new Color(0.25f, 0.45f, 0.85f));
            _texWhite = Tex(new Color(1f, 1f, 1f, 0.9f));
        }

        private void OnGUI()
        {
            EnsureStyles();
            var rt = CaseRuntime.Instance;
            if (rt == null || rt.Case == null) return;
            if (_player == null) _player = FindFirstObjectByType<FirstPersonController>();
            if (_interactor == null) _interactor = FindFirstObjectByType<Interactor>();

            // ---- top-left: clock + evidence ----
            int m = Mathf.FloorToInt(rt.TimeRemaining / 60f);
            int s = Mathf.FloorToInt(rt.TimeRemaining % 60f);
            GUI.Label(new Rect(18, 12, 400, 24), "TIME REMAINING:", _label);
            GUI.Label(new Rect(18, 30, 400, 40), $"{m:00}:{s:00}", _big);
            GUI.Label(new Rect(18, 66, 500, 24), $"EVIDENCE FOUND: {rt.EvidenceFound} / {rt.EvidenceTotal}", _label);

            // ---- top-right: case title + CURRENT OBJECTIVES ----
            GUI.Label(new Rect(Screen.width - 480, 12, 460, 24),
                $"{rt.Case.Title}  (seed {rt.Case.Seed})",
                new GUIStyle(_label) { alignment = TextAnchor.UpperRight });
            DrawObjectives(rt);

            // ---- bottom-left: INTEGRITY + STAMINA ----
            DrawBar(18, Screen.height - 96, "INTEGRITY", 1.0f, _texGreen, "100%");
            float stam = _player != null ? _player.Stamina / 100f : 1f;
            DrawBar(18, Screen.height - 52, "STAMINA", stam, _texBlue, $"{Mathf.RoundToInt(stam * 100)}%");

            // ---- bottom-center: 5-slot hotbar ----
            DrawHotbar();

            // ---- center: interact prompt ----
            if (_interactor != null && _interactor.CurrentPrompt != null)
            {
                GUI.Label(new Rect(Screen.width / 2f - 250, Screen.height / 2f + 42, 500, 26),
                    "[HOLD E]  " + _interactor.CurrentPrompt,
                    new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
                if (_interactor.HoldProgress > 0f)
                    GUI.DrawTexture(new Rect(Screen.width / 2f - 90, Screen.height / 2f + 72,
                        180f * _interactor.HoldProgress, 6), _texWhite);
            }
            GUI.Label(new Rect(Screen.width / 2f - 4, Screen.height / 2f - 14, 20, 24), "·", _big);

            // ---- trial banner ----
            if (rt.BellRung)
                GUI.Label(new Rect(Screen.width / 2f - 320, 16, 640, 36),
                    "TRIAL PHASE - COURT IS IN SESSION",
                    new GUIStyle(_big) { fontSize = 24, alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1f, 0.82f, 0.35f) } });

            // ---- bottom-right: log ----
            int shown = Mathf.Min(7, rt.Log.Count);
            for (int i = 0; i < shown; i++)
            {
                string line = rt.Log[rt.Log.Count - shown + i];
                GUI.Label(new Rect(Screen.width - 620, Screen.height - 26 - (shown - i) * 20, 600, 20), line, _log);
            }
        }

        private void DrawObjectives(CaseRuntime rt)
        {
            int n = Mathf.Min(4, rt.Case.Evidence.Count);
            float w = 360, h = 30 + n * 21;
            var box = new Rect(Screen.width - w - 18, 44, w, h);
            GUI.DrawTexture(box, _texDark);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 1.5f), _texWhite);
            GUI.Label(new Rect(box.x + 10, box.y + 5, w - 20, 20), "CURRENT OBJECTIVES", _label);
            for (int i = 0; i < n; i++)
            {
                bool done = rt.Collected.Contains(i);
                string name = rt.Case.Evidence[i].Name;
                if (name.Length > 34) name = name.Substring(0, 33) + "…";
                GUI.Label(new Rect(box.x + 12, box.y + 27 + i * 21, w - 24, 20),
                    (done ? "[x] " : "[ ] ") + "Recover " + name,
                    done ? new GUIStyle(_obj) { normal = { textColor = new Color(0.55f, 0.8f, 0.55f) } } : _obj);
            }
        }

        private void DrawBar(float x, float y, string label, float fill, Texture2D tex, string pct)
        {
            GUI.Label(new Rect(x, y, 220, 20), label, _label);
            GUI.Label(new Rect(x + 150, y, 80, 20), pct, _label);
            GUI.DrawTexture(new Rect(x, y + 20, 200, 12), _texDark);
            GUI.DrawTexture(new Rect(x, y + 20, 200 * Mathf.Clamp01(fill), 12), tex);
        }

        private void DrawHotbar()
        {
            string[] slots = { "1", "2 FILE", "3", "4", "5 RADIO" };
            float size = 56, pad = 8;
            float total = slots.Length * size + (slots.Length - 1) * pad;
            float x0 = Screen.width / 2f - total / 2f, y = Screen.height - 76;
            for (int i = 0; i < slots.Length; i++)
            {
                var r = new Rect(x0 + i * (size + pad), y, size, size);
                GUI.DrawTexture(r, _texDark);
                if (i == 1) // the case file slot, always held (mockup canon)
                {
                    GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), _texWhite);
                    GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), _texWhite);
                    GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), _texWhite);
                    GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), _texWhite);
                }
                GUI.Label(new Rect(r.x, r.y + 4, r.width, r.height - 8), slots[i], _slot);
            }
        }
    }
}
