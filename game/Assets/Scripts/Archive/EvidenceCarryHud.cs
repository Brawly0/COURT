using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Archive
{
    /// <summary>
    /// The carry indicator, the drop key, and the inspect panel.
    ///
    /// Knowledge is kept LOCALLY here once granted, which is the visible consequence
    /// of separating knowledge from custody: hand the folder to somebody else and
    /// this list still remembers what you read. Nothing removes an entry.
    ///
    /// Placeholder presentation. Everything shown arrived in a filtered packet, so
    /// there is no path from here to case truth.
    /// </summary>
    public class EvidenceCarryHud : MonoBehaviour
    {
        [Tooltip("Drops whatever is being carried.")]
        public Key DropKey = Key.G;

        [Tooltip("Hold to inspect what you know.")]
        public Key InspectKey = Key.Tab;

        [Tooltip("Seconds an action banner stays up.")]
        public float BannerSeconds = 2.2f;

        [Tooltip("Toggles the developer carry readout. Not part of the normal UI.")]
        public Key DebugKey = Key.F6;

        private bool _debug;

        /// <summary>Everything this player has legitimately read. Never shrinks.</summary>
        private readonly List<EvidenceDiscovery> _known = new();

        /// <summary>How many items THIS client has read. For cross-process assertions.</summary>
        public int KnownCount => _known.Count;

        private string _banner = "";
        private float _bannerUntil;

        private GUIStyle _heading, _body, _small;
        private Texture2D _panel, _accent;

        private void Update()
        {
            var director = EvidenceCustodyDirector.Instance;
            if (director != null)
            {
                director.KnowledgeGranted -= OnKnowledge;
                director.KnowledgeGranted += OnKnowledge;
                director.CarryChanged -= OnCarryChanged;
                director.CarryChanged += OnCarryChanged;
                director.LocalRegistrationSucceeded -= OnRegistered;
                director.LocalRegistrationSucceeded += OnRegistered;
                director.RegistrationAnnounced -= OnRegistrationAnnounced;
                director.RegistrationAnnounced += OnRegistrationAnnounced;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || director == null) return;

            if (System.Enum.IsDefined(typeof(Key), DropKey) &&
                keyboard[DropKey].wasPressedThisFrame && director.LocalIsCarrying)
                director.RequestDrop();

            if (System.Enum.IsDefined(typeof(Key), DebugKey) && keyboard[DebugKey].wasPressedThisFrame)
                _debug = !_debug;
        }

        private void OnDestroy()
        {
            var director = EvidenceCustodyDirector.Instance;
            if (director == null) return;
            director.KnowledgeGranted -= OnKnowledge;
            director.CarryChanged -= OnCarryChanged;
            director.LocalRegistrationSucceeded -= OnRegistered;
            director.RegistrationAnnounced -= OnRegistrationAnnounced;
        }

        private void OnKnowledge(EvidenceDiscovery packet)
        {
            string id = packet.EvidenceId.ToString();
            if (_known.Exists(e => e.EvidenceId.ToString() == id)) return;   // already read

            _known.Add(packet);
            Banner($"PICKED UP\n{packet.Title}");
        }

        private void OnCarryChanged(string title, bool carrying)
        {
            // Registration also clears the carry HUD, and it announces itself. Do
            // not also claim the folder was dropped.
            if (Time.time < _suppressDropBannerUntil) return;
            if (!carrying && !string.IsNullOrEmpty(title)) Banner("EVIDENCE DROPPED");
        }

        /// <summary>The registrant, and only the registrant, learns which document it was.</summary>
        private void OnRegistered(string title)
        {
            _suppressDropBannerUntil = Time.time + 0.5f;
            Banner($"EVIDENCE REGISTERED\n{title}");
        }

        /// <summary>
        /// Everyone sees this. Note what it can say: an id and a team, because that
        /// is all the packet carries. There is no title here to leak.
        /// </summary>
        private void OnRegistrationAnnounced(EvidenceRegistrationNotice notice)
        {
            var team = (CaseClosed.Game.Cases.Roles.PlayerTeam)notice.Team;
            _publicLog.Add($"{notice.EvidenceId}  registered by {team}");
            if (_publicLog.Count > 8) _publicLog.RemoveAt(0);
        }

        private float _suppressDropBannerUntil;
        private readonly List<string> _publicLog = new();

        private void Banner(string text)
        {
            _banner = text;
            _bannerUntil = Time.time + BannerSeconds;
        }

        private void OnGUI()
        {
            EnsureStyles();

            DrawCarryIndicator();
            DrawBanner();
            DrawInspect();
            DrawPublicRegister();
            DrawCarryDebug();
        }

        /// <summary>
        /// The public record, visible to everyone including the opposing team.
        ///
        /// It can only ever show an id and a team, because that is the entire
        /// contents of EvidenceRegistrationNotice. The opposing side learns that the
        /// other team has entered something — which is the intended pressure — and
        /// nothing about what the document says.
        /// </summary>
        private void DrawPublicRegister()
        {
            if (_publicLog.Count == 0) return;

            float w = 300f, h = 30f + _publicLog.Count * 18f;
            float x = 16f, y = 16f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, 3f, h), _accent);
            GUI.Label(new Rect(x + 12f, y + 5f, w - 20f, 18f), "EVIDENCE REGISTERED", _small);

            for (int i = 0; i < _publicLog.Count; i++)
                GUI.Label(new Rect(x + 12f, y + 24f + i * 18f, w - 20f, 18f), _publicLog[i], _body);
        }

        /// <summary>
        /// Developer readout, off by default and behind its own key. Shows every
        /// evidence body this machine knows about, so "the folder is in the wrong
        /// place" can be told apart from "custody says something different from what
        /// I am looking at" — which are completely different bugs.
        /// </summary>
        private void DrawCarryDebug()
        {
            if (!_debug) return;

            var bodies = PhysicalEvidence.DebugSnapshot();

            float w = 470f, h = 44f + Mathf.Max(1, bodies.Count) * 46f;
            float x = Screen.width - w - 16f, y = 150f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 12f, y + 8f, w - 24f, h - 16f));

            GUILayout.Label($"CARRY DEBUG  [{DebugKey}]", _heading);
            DrawCameraDebug();

            if (bodies.Count == 0)
            {
                GUILayout.Label("No evidence bodies spawned.", _small);
            }
            else
            {
                foreach (var line in bodies) GUILayout.Label(line, _small);
            }

            GUILayout.EndArea();
        }

        /// <summary>Bottom-centre, always visible while holding something.</summary>
        private void DrawCarryIndicator()
        {
            var director = EvidenceCustodyDirector.Instance;
            if (director == null || !director.LocalIsCarrying) return;

            float w = 340f, h = 42f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height - 96f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, 3f, h), _accent);

            GUI.Label(new Rect(x + 12f, y + 4f, w - 20f, 18f), "CARRYING", _small);
            GUI.Label(new Rect(x + 12f, y + 19f, w - 20f, 20f), director.LocalCarriedTitle, _body);
            GUI.Label(new Rect(x + w - 74f, y + 13f, 66f, 18f), $"[{DropKey}] drop", _small);
        }

        private void DrawBanner()
        {
            if (Time.time > _bannerUntil || string.IsNullOrEmpty(_banner)) return;

            float w = 400f, h = 54f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.30f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.Label(new Rect(x + 14f, y + 8f, w - 28f, h - 16f), _banner, _body);
        }

        /// <summary>
        /// Hold Tab: everything you have read, including items you no longer hold.
        /// That persistence is the whole point of keeping knowledge separate.
        /// </summary>
        private void DrawInspect()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !System.Enum.IsDefined(typeof(Key), InspectKey)) return;
            if (!keyboard[InspectKey].isPressed) return;

            float w = 480f;
            float h = 54f + Mathf.Max(1, _known.Count) * 62f;
            float x = 16f, y = 150f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUILayout.BeginArea(new Rect(x + 14f, y + 10f, w - 28f, h - 20f));

            GUILayout.Label($"EVIDENCE YOU KNOW ({_known.Count})", _heading);
            GUILayout.Space(4f);

            if (_known.Count == 0)
            {
                GUILayout.Label("Nothing yet. Search the Archive.", _small);
            }
            else
            {
                foreach (var entry in _known)
                {
                    GUILayout.Label(entry.Title.ToString(), _body);
                    GUILayout.Label(entry.Kind.ToString(), _small);
                    GUILayout.Label(entry.Description.ToString(), _small);
                    GUILayout.Space(6f);
                }
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// Camera-mode readout, alongside the carry state so the two can be compared
        /// in one glance — "folder is at the socket but I cannot see it" is a very
        /// different bug from "the camera is somewhere unexpected".
        /// </summary>
        private void DrawCameraDebug()
        {
            var rig = FindAnyObjectByType<CaseClosed.Game.Prototype.PlayerCameraRig>();
            if (rig == null) { GUILayout.Label("no camera rig", _small); return; }

            string body = "-";
            string carryOffset = "-";
            if (rig.Target != null)
            {
                var local = rig.Target.GetComponent<CaseClosed.Game.Prototype.PlayerLocalBody>();
                body = local == null ? "n/a" : (local.LocalBodyVisible ? "visible" : "hidden");

                var socket = rig.Target.GetComponent<CaseClosed.Game.Prototype.PlayerCarrySocket>();
                carryOffset = socket == null ? "n/a" : socket.LocalViewOffset.ToString("F2");
            }

            GUILayout.Label(
                $"mode={rig.Mode}  blend={rig.ModeBlend:F2}  dist={rig.CurrentDistance:F2}\n" +
                $"pivot={rig.Pivot:F2}\n" +
                $"localBody={body}  carryLocalOffset={carryOffset}", _small);
            GUILayout.Space(4f);
        }

        private void EnsureStyles()
        {
            if (_body != null) return;

            _heading = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _body.normal.textColor = Color.white;

            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _small.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

            _panel = Solid(new Color(0.05f, 0.05f, 0.07f, 0.93f));
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
