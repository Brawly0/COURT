using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Cases.Roles
{
    /// <summary>
    /// WHY THIS EXISTS: roles were being dealt correctly and were completely
    /// invisible — the data lived in the network layer and in test logs, so from
    /// inside the running game nothing appeared to happen at all.
    ///
    /// This is the smallest honest readout: your seat, your briefing, and the public
    /// table. It shows exactly what this player is entitled to see, so it doubles as
    /// a live check on the secrecy rules — if an investigator ever sees a guilt line
    /// here, something is wrong upstream.
    ///
    /// Deliberately NOT the real role UI, which is a later milestone. OnGUI, no
    /// Canvas, delete the component and nothing breaks.
    /// </summary>
    public class PlayerRoleHud : MonoBehaviour
    {
        [Tooltip("Toggles the panel.")]
        public Key ToggleKey = Key.F5;

        [Tooltip("Shown from the moment a case is dealt.")]
        public bool Visible = false;

        [Tooltip("Also list everyone else's seat. Roles are public information.")]
        public bool ShowTable = true;

        private GUIStyle _label, _heading;
        private Texture2D _panel;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (ToggleKey == Key.None || !System.Enum.IsDefined(typeof(Key), ToggleKey)) return;
            if (keyboard[ToggleKey].wasPressedThisFrame) Visible = !Visible;
        }

        private void OnGUI()
        {
            if (!Visible) return;

            var controller = CaseNetworkController.Instance;
            if (controller == null || !controller.IsSpawned) return;

            EnsureStyles();

            float w = 330f, x = 12f, y = 150f;
            var body = BuildText(controller, out string heading);
            float h = 46f + body.Split('\n').Length * 15f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.Label(new Rect(x + 12f, y + 8f, w - 24f, 22f), heading, _heading);
            GUI.Label(new Rect(x + 12f, y + 30f, w - 24f, h - 38f), body, _label);
        }

        private string BuildText(CaseNetworkController controller, out string heading)
        {
            var text = new StringBuilder();

            if (controller.State == CaseLifecycleState.NoCase)
            {
                heading = "NO CASE";
                text.Append("The host has not dealt a case yet.\n");
                text.Append(controller.IsServer
                    ? "Press F2 and hit GENERATE CASE."
                    : "Waiting for the host.");
                return text.ToString();
            }

            if (!controller.HasLocalView)
            {
                heading = "CASE LOADING";
                text.Append("Waiting for your briefing...");
                return text.ToString();
            }

            var view = controller.LocalView;
            heading = $"YOU ARE: {RoleInfo.DisplayName(view.Role).ToUpper()}" +
                      $"   [{RoleInfo.TeamName(RoleInfo.TeamOf(view.Role))}]";

            text.Append(view.PrivateBriefing.ToString()).Append("\n\n");

            // Only the defendant is ever told this, and only about themselves.
            if (view.KnowsOwnGuilt)
                text.Append(view.IsActuallyGuilty
                    ? ">> YOU DID IT. Lying is your choice. <<\n\n"
                    : ">> YOU DID NOT DO IT. Prove it. <<\n\n");

            if (view.PermittedFacts.Length > 0)
            {
                text.Append("WHAT YOU KNOW:\n");
                for (int i = 0; i < view.PermittedFacts.Length; i++)
                    text.Append("  - ").Append(view.PermittedFacts[i].ToString()).Append('\n');
            }
            else
            {
                text.Append("WHAT YOU KNOW: nothing yet.\n");
            }

            if (ShowTable && PlayerRoster.Instance != null && PlayerRoster.Instance.Count > 0)
            {
                text.Append("\nTHE TABLE (public):\n");
                ulong me = NetworkManager.Singleton.LocalClientId;

                foreach (var seat in PlayerRoster.Instance.Snapshot())
                    text.Append("  player ").Append(seat.Key).Append(" - ")
                        .Append(seat.Value)
                        .Append(seat.Key == me ? "   <- you" : "")
                        .Append('\n');

                if (PlayerRoster.Instance.DefendantMissing)
                    text.Append("  ! the defendant has left the building\n");
            }

            return text.ToString();
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _label.normal.textColor = new Color(0.92f, 0.92f, 0.90f);

            _heading = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _panel = new Texture2D(1, 1);
            _panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _panel.Apply();
        }
    }
}
