using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Prototype;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// The briefing card, and the thing that stops you wandering off during it.
    ///
    /// Movement is blocked by disabling the local PlayerMovement component rather
    /// than by swallowing input. Two reasons: the character physically stops instead
    /// of sliding to a halt, and nothing has to be remembered to undo — re-enabling
    /// one component restores exactly the previous behaviour.
    ///
    /// Placeholder UI on purpose (OnGUI, no Canvas). It shows precisely what this
    /// player is entitled to see, so it doubles as a live secrecy check: a guilt line
    /// on a Defense Attorney's screen means something upstream is broken.
    /// </summary>
    public class BriefingScreen : MonoBehaviour
    {
        [Tooltip("Re-open the briefing after closing it. J, not Tab — Tab already opens " +
                 "the evidence you know (EvidenceCarryHud.InspectKey), and both panels " +
                 "were drawing on top of each other.")]
        public Key ToggleKey = Key.J;

        [Tooltip("Freeze the local player while the briefing is open.")]
        public bool BlockMovementUntilReady = true;

        private bool _open;
        private bool _dismissed;
        private PlayerMovement _blocked;

        private GUIStyle _label, _heading, _small;
        private Texture2D _panel, _dim;
        private Vector2 _scroll;

        private void Update()
        {
            var flow = MatchFlowController.Instance;
            if (flow == null) return;

            // Opens by itself the moment a briefing arrives — the player should not
            // have to discover a keybind to learn who they are.
            if (flow.HasBriefing && !_dismissed) _open = true;

            var keyboard = Keyboard.current;
            if (keyboard != null && System.Enum.IsDefined(typeof(Key), ToggleKey) &&
                keyboard[ToggleKey].wasPressedThisFrame && flow.HasBriefing)
            {
                _open = !_open;
                if (_open) _dismissed = false;
            }

            ApplyMovementBlock(flow);
        }

        /// <summary>
        /// Freeze while the card is up AND the player has not confirmed. After Ready
        /// they can move around the atrium waiting for everyone else, which is a
        /// better lobby than standing frozen.
        /// </summary>
        private void ApplyMovementBlock(MatchFlowController flow)
        {
            bool shouldBlock = BlockMovementUntilReady && _open && !flow.LocalReadySent;

            if (shouldBlock && _blocked == null)
            {
                _blocked = FindLocalMovement();
                if (_blocked != null)
                {
                    _blocked.enabled = false;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
            else if (!shouldBlock && _blocked != null)
            {
                _blocked.enabled = true;
                _blocked = null;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>The one PlayerMovement that is ours: remote copies are disabled.</summary>
        private static PlayerMovement FindLocalMovement() =>
            Object.FindObjectsByType<PlayerMovement>()
                  .FirstOrDefault(m => m.GetComponent<Unity.Netcode.NetworkObject>() is { IsOwner: true });

        private void OnGUI()
        {
            var flow = MatchFlowController.Instance;
            if (flow == null || !_open || !flow.HasBriefing) return;

            EnsureStyles();

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);

            float w = Mathf.Min(660f, Screen.width - 60f);
            float h = Mathf.Min(560f, Screen.height - 60f);
            float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 22f, y + 18f, w - 44f, h - 36f));

            var briefing = flow.LocalBriefing;
            var info = CaseNetworkController.Instance != null
                ? CaseNetworkController.Instance.PublicInfo
                : PublicCaseInfo.Empty;

            GUILayout.Label(info.Title.ToString(), _heading);
            GUILayout.Space(6f);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(h - 150f));

            Field("YOUR ROLE", RoleInfo.DisplayName(briefing.Role));
            Field("YOUR TEAM", RoleInfo.TeamName(briefing.Team));

            GUILayout.Space(8f);
            GUILayout.Label("PUBLIC BRIEFING", _heading);
            GUILayout.Label(info.CrimeDescription.ToString(), _label);
            GUILayout.Label(info.Briefing.ToString(), _label);
            GUILayout.Label($"Investigation: {info.InvestigationSeconds / 60} minutes", _small);

            GUILayout.Space(8f);
            GUILayout.Label("PRIVATE INFORMATION", _heading);

            // The defendant's card, and only theirs, carries the answer.
            if (briefing.KnowsOwnGuilt)
            {
                var previous = GUI.color;
                GUI.color = briefing.IsActuallyGuilty
                    ? new Color(1f, 0.55f, 0.5f) : new Color(0.55f, 1f, 0.6f);
                GUILayout.Label(briefing.IsActuallyGuilty
                    ? "YOU ARE GUILTY — nobody else has been told."
                    : "YOU ARE INNOCENT — nobody else has been told.", _heading);
                GUI.color = previous;
            }

            GUILayout.Label(briefing.PrivateInformation.ToString(), _label);

            GUILayout.Space(8f);
            GUILayout.Label("YOUR OBJECTIVE", _heading);
            GUILayout.Label(briefing.Objective.ToString(), _label);
            GUILayout.Label(briefing.Ability.ToString(), _small);

            GUILayout.EndScrollView();
            GUILayout.Space(8f);

            DrawFooter(flow);
            GUILayout.EndArea();
        }

        private void DrawFooter(MatchFlowController flow)
        {
            if (flow.Phase == MatchPhase.PreInvestigationReady)
            {
                var previous = GUI.color;
                GUI.color = new Color(0.55f, 1f, 0.6f);
                GUILayout.Label("ALL PLAYERS READY", _heading);
                GUI.color = previous;
                GUILayout.Label("The investigation has not started yet.", _small);

                if (GUILayout.Button("CLOSE  (Tab reopens)", GUILayout.Height(30f)))
                { _open = false; _dismissed = true; }
                return;
            }

            GUILayout.Label($"Players Ready: {flow.ReadyCount} / {flow.RequiredCount}", _heading);

            if (!flow.LocalReadySent)
            {
                if (GUILayout.Button("READY", GUILayout.Height(38f)))
                    flow.RequestReady();
            }
            else
            {
                GUILayout.Label("You are ready. Waiting for the others.", _small);
                if (GUILayout.Button("CLOSE  (Tab reopens)", GUILayout.Height(28f)))
                { _open = false; _dismissed = true; }
            }
        }

        private void Field(string caption, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(caption + ":", _small, GUILayout.Width(110f));
            GUILayout.Label(value, _heading);
            GUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _label.normal.textColor = new Color(0.92f, 0.92f, 0.90f);

            _heading = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _small.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

            _panel = Solid(new Color(0.06f, 0.06f, 0.08f, 0.97f));
            _dim = Solid(new Color(0f, 0f, 0f, 0.55f));
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
