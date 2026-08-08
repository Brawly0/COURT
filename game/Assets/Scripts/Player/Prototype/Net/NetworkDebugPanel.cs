using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Prototype.Net
{
    /// <summary>
    /// Developer-only network readout. Off by default; F8 toggles it.
    ///
    /// WHAT IT DELIBERATELY OMITS: anything from CompleteCaseTruth. The case SEED is
    /// shown because it is already public (it is in PublicCaseInfo and printed on
    /// every client), and the local ROLE is shown because it is this player's own.
    /// The perpetrator, the guilt bit and the proof chain are not here and must not
    /// be added — a debug panel is exactly the place a leak gets rationalised in.
    /// </summary>
    public class NetworkDebugPanel : MonoBehaviour
    {
        [Tooltip("Toggles the panel. Developer tool, off by default.")]
        public Key ToggleKey = Key.F8;

        public bool Visible = false;

        private GUIStyle _label, _heading;
        private Texture2D _panel;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (System.Enum.IsDefined(typeof(Key), ToggleKey) && keyboard[ToggleKey].wasPressedThisFrame)
                Visible = !Visible;
        }

        private void OnGUI()
        {
            if (!Visible) return;
            EnsureStyles();

            var nm = NetworkManager.Singleton;
            float w = 320f, h = 250f;
            float x = Screen.width - w - 16f, y = Screen.height - h - 16f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 12f, y + 10f, w - 24f, h - 20f));

            GUILayout.Label($"NETWORK DEBUG  [{ToggleKey}]", _heading);
            GUILayout.Label(BuildStamp.Line, _label);
            GUILayout.Space(4f);

            if (nm == null)
            {
                GUILayout.Label("no NetworkManager", _label);
                GUILayout.EndArea();
                return;
            }

            var utp = nm.GetComponent<UnityTransport>();
            string role = nm.IsHost ? "HOST" : nm.IsServer ? "SERVER" : nm.IsClient ? "CLIENT" : "OFFLINE";
            string state = nm.IsConnectedClient ? "CONNECTED" : nm.IsListening ? "LISTENING" : "OFFLINE";

            GUILayout.Label($"transport : {(utp == null ? "?" : utp.GetType().Name)}", _label);
            GUILayout.Label($"role      : {role}", _label);
            GUILayout.Label($"state     : {state}", _label);

            if (utp != null)
            {
                var c = utp.ConnectionData;
                GUILayout.Label($"dial      : {c.Address}:{c.Port}", _label);
                GUILayout.Label($"listen    : {c.ServerListenAddress}:{c.Port}", _label);
            }

            if (nm.IsListening)
            {
                GUILayout.Label($"my id     : {nm.LocalClientId}", _label);

                // Round-trip time is only meaningful on a real client; a host is
                // talking to itself and would always report 0.
                if (nm.IsClient && !nm.IsServer && utp != null)
                    GUILayout.Label($"rtt       : {utp.GetCurrentRtt(NetworkManager.ServerClientId)} ms", _label);

                if (nm.IsServer)
                    GUILayout.Label($"players   : {nm.ConnectedClientsIds.Count} " +
                                    $"[{string.Join(", ", nm.ConnectedClientsIds)}]", _label);
            }

            var flow = CaseClosed.Game.Match.MatchFlowController.Instance;
            if (flow != null && flow.IsSpawned)
                GUILayout.Label($"phase     : {flow.Phase}  ({flow.SecondsRemaining:F0}s)", _label);

            var caseNet = CaseClosed.Game.Cases.CaseNetworkController.Instance;
            if (caseNet != null && caseNet.IsSpawned)
                GUILayout.Label($"case seed : {caseNet.PublicInfo.Seed}", _label);

            var roster = CaseClosed.Game.Cases.Roles.PlayerRoster.Instance;
            if (roster != null && roster.IsSpawned)
                GUILayout.Label($"my role   : {roster.LocalRole}", _label);

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _heading = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(0.55f, 0.85f, 1f);

            _label = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _label.normal.textColor = new Color(0.82f, 0.82f, 0.86f);

            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.08f, 0.93f));
            texture.Apply();
            _panel = texture;
        }
    }
}
