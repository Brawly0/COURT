using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// Developer window: the GENERATE CASE controls, and an inspector for the full
    /// hidden truth.
    ///
    /// GATED THREE WAYS, because this displays the perpetrator:
    ///   1. compiled out entirely unless UNITY_EDITOR or DEVELOPMENT_BUILD
    ///   2. the truth section renders only for the server/host
    ///   3. hidden behind a keypress that must be pressed deliberately
    ///
    /// A client running a development build still cannot see the truth here — it
    /// reads ActiveCaseManager.Truth, which is null on clients. There is nothing to
    /// reveal, rather than something withheld.
    /// </summary>
    public class CaseDebugPanel : MonoBehaviour
    {
        [Tooltip("Shows/hides the panel.")]
        public Key ToggleKey = Key.F2;

        [Tooltip("Start with the panel open.")]
        public bool Visible = true;

        private string _seedText = "1";
        private Vector2 _scroll;
        private string _cachedTruthText = "";
        private ulong _cachedForSeed = ulong.MaxValue;

        private GUIStyle _label, _mono;
        private Texture2D _panel;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[ToggleKey].wasPressedThisFrame) Visible = !Visible;
        }

        private void OnGUI()
        {
            if (!Visible) return;
            EnsureStyles();

            var controller = CaseNetworkController.Instance;
            float w = 560f, h = Screen.height - 24f;
            var area = new Rect(12f, 12f, w, h);

            GUI.DrawTexture(area, _panel);
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f));

            GUILayout.Label($"CASE DEBUG   ({ToggleKey} hide)   — development build only", _label);
            GUILayout.Space(4f);

            if (controller == null || !controller.IsSpawned)
            {
                GUILayout.Label("No session. Start HOST to generate a case.", _label);
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"lifecycle   {controller.State}", _label);
            DrawLocalView(controller);
            GUILayout.Space(6f);

            if (controller.IsServer) DrawHostControls(controller);
            else GUILayout.Label("Client: the hidden truth is not on this machine.", _label);

            GUILayout.EndArea();
        }

        /// <summary>Safe on any machine: this is what the local player is allowed to know.</summary>
        private void DrawLocalView(CaseNetworkController controller)
        {
            var info = controller.PublicInfo;
            GUILayout.Space(4f);
            GUILayout.Label("--- public (everyone sees this) ---", _label);
            GUILayout.Label($"title       {info.Title}", _label);
            GUILayout.Label($"crime       {info.CrimeDescription}", _label);
            GUILayout.Label($"seed        {info.Seed}", _label);
            GUILayout.Label($"duration    {info.InvestigationSeconds / 60}:00", _label);

            GUILayout.Space(4f);
            GUILayout.Label("--- your private view ---", _label);
            if (!controller.HasLocalView)
            {
                GUILayout.Label("(not received yet)", _label);
                return;
            }

            var view = controller.LocalView;
            GUILayout.Label($"role        {view.Role}", _label);
            GUILayout.Label(view.KnowsOwnGuilt
                ? $"your guilt  {(view.IsActuallyGuilty ? "GUILTY" : "INNOCENT")}"
                : "your guilt  (not yours to know)", _label);

            DrawRoster();
        }

        /// <summary>
        /// The dealt table. Public information, so it is safe on every machine —
        /// and without it there was no way to see that roles had been dealt at all.
        /// </summary>
        private void DrawRoster()
        {
            var roster = Roles.PlayerRoster.Instance;

            GUILayout.Space(4f);
            GUILayout.Label("--- the table (public) ---", _label);

            if (roster == null || roster.Count == 0)
            {
                GUILayout.Label("(no roles dealt - generate a case)", _label);
                return;
            }

            ulong me = Unity.Netcode.NetworkManager.Singleton.LocalClientId;
            foreach (var seat in roster.Snapshot())
                GUILayout.Label($"  player {seat.Key}   {seat.Value}{(seat.Key == me ? "   <- you" : "")}", _label);

            if (roster.DefendantMissing)
                GUILayout.Label("  ! defendant seat vacant", _label);
        }

        private void DrawHostControls(CaseNetworkController controller)
        {
            GUILayout.Label("--- host controls ---", _label);

            GUILayout.BeginHorizontal();
            GUILayout.Label("seed", _label, GUILayout.Width(40f));
            _seedText = GUILayout.TextField(_seedText, GUILayout.Width(160f));

            if (GUILayout.Button("RANDOM", GUILayout.Width(80f)))
                _seedText = CaseGenerationService.RandomSeed().ToString();
            GUILayout.EndHorizontal();

            // START MATCH runs the whole sequence: deal roles, generate, distribute
            // briefings, then wait on Ready. GENERATE CASE below is the narrower
            // tool — it rebuilds the case without touching the lobby.
            var flow = Match.MatchFlowController.Instance;
            if (flow != null)
            {
                GUILayout.Space(2f);
                if (GUILayout.Button("START MATCH  (deal + generate + brief)", GUILayout.Height(34f)))
                {
                    if (ulong.TryParse(_seedText, out ulong matchSeed))
                        flow.HostStartMatch(matchSeed);
                }
                GUILayout.Label($"phase       {flow.Phase}", _label);
                GUILayout.Label($"ready       {flow.ReadyCount} / {flow.RequiredCount}", _label);
                GUILayout.Space(4f);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("GENERATE CASE", GUILayout.Height(30f)))
                Generate(controller);

            // Regenerate re-runs the SAME seed. If the digest below changes between
            // two presses, determinism is broken and that is a real bug.
            if (GUILayout.Button("REGENERATE (same seed)", GUILayout.Height(30f)))
                Generate(controller);
            GUILayout.EndHorizontal();

            var truth = ActiveCaseManager.Instance?.Truth;
            if (truth == null)
            {
                GUILayout.Label("no case generated yet", _label);
                return;
            }

            // Formatting the whole truth allocates a lot of string; only redo it
            // when the case actually changes, not every OnGUI frame.
            if (_cachedForSeed != truth.Seed || string.IsNullOrEmpty(_cachedTruthText))
            {
                _cachedTruthText = CaseViewFactory.BuildDeveloperDebugView(truth);
                _cachedForSeed = truth.Seed;
            }

            GUILayout.Space(6f);
            GUILayout.Label("--- COMPLETE TRUTH (host only, never sent) ---", _label);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(_cachedTruthText, _mono);
            GUILayout.EndScrollView();
        }

        private void Generate(CaseNetworkController controller)
        {
            if (!ulong.TryParse(_seedText, out ulong seed))
            {
                Debug.LogWarning($"[Case] '{_seedText}' is not a valid seed; using 1.");
                seed = 1;
            }

            _cachedForSeed = ulong.MaxValue;   // force the truth view to rebuild
            controller.HostGenerateCase(seed);
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _label.normal.textColor = Color.white;

            _mono = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, richText = false };
            _mono.normal.textColor = new Color(0.85f, 0.92f, 0.85f);

            _panel = new Texture2D(1, 1);
            _panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.82f));
            _panel.Apply();
        }
#endif
    }
}
