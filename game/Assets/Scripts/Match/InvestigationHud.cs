using System.Linq;
using Unity.Netcode;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Greybox;

namespace CaseClosed.Game.Match
{
    /// <summary>
    /// The screen that answers "what am I doing and how long have I got".
    ///
    /// WHY IT IS SHAPED LIKE THIS. A new player dropped into this building could
    /// previously see a courthouse and nothing else — no role, no goal, no clock, no
    /// idea which door mattered. Every block below exists to answer one question a
    /// confused player actually asks, in the order they ask it: who am I, what do I
    /// want, how long do I have, where am I, where should I go, what do I know.
    ///
    /// Everything drawn here is either local presentation or already-replicated safe
    /// state. The case TITLE is public (it is in PublicCaseInfo); the role and team
    /// are this player's own briefing; the notes arrived addressed to them. There is
    /// no path from this screen to case truth.
    ///
    /// Placeholder presentation, deliberately.
    /// </summary>
    public class InvestigationHud : MonoBehaviour
    {
        [Tooltip("Hold to read your own case notes during and after the investigation.")]
        public UnityEngine.InputSystem.Key NotesKey = UnityEngine.InputSystem.Key.N;

        private GUIStyle _label, _value, _big, _huge, _small, _warn;
        private Texture2D _panel, _accent, _danger;

        private float _lastWarned = -1f;
        private string _banner = "";
        private float _bannerUntil;

        private void OnGUI()
        {
            var flow = MatchFlowController.Instance;
            if (flow == null) return;

            EnsureStyles();

            switch (flow.Phase)
            {
                case MatchPhase.InvestigationCountdown: DrawCountdown(flow); break;
                case MatchPhase.Investigation:          DrawInvestigation(flow); break;
                case MatchPhase.InvestigationEnding:
                case MatchPhase.CourtroomTransition:    DrawEnding(flow); break;
                case MatchPhase.CourtroomReady:         DrawCourtroomReady(); break;
            }

            DrawBanner();
            if (flow.HasNotes) DrawNotes(flow);
        }

        /// <summary>CASE BEGINS IN 3 / 2 / 1 / INVESTIGATE!</summary>
        private void DrawCountdown(MatchFlowController flow)
        {
            int seconds = Mathf.CeilToInt(flow.SecondsRemaining);
            string text = seconds > 0 ? seconds.ToString() : "INVESTIGATE!";

            Rect area = new(0f, Screen.height * 0.32f, Screen.width, 60f);
            GUI.Label(new Rect(0f, area.y - 30f, Screen.width, 28f), "CASE BEGINS IN", Centered(_label));
            GUI.Label(area, text, Centered(_huge));
        }

        /// <summary>The always-on panel: who, what, how long, where.</summary>
        private void DrawInvestigation(MatchFlowController flow)
        {
            WarnIfDue(flow);

            var briefing = flow.LocalBriefing;
            float w = 300f, x = 16f, y = 16f;
            float h = 250f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, 3f, h), _accent);

            float row = y + 10f;
            Field(x, ref row, w, "CASE", CaseTitle());
            Field(x, ref row, w, "ROLE", RoleInfo.DisplayName(briefing.Role));
            Field(x, ref row, w, "TEAM", RoleInfo.TeamName(briefing.Team));

            // TIME, given its own weight — it is the thing players glance at.
            GUI.Label(new Rect(x + 12f, row, w - 24f, 16f), "TIME", _label);
            row += 15f;
            float left = flow.SecondsRemaining;
            GUI.Label(new Rect(x + 12f, row, w - 24f, 30f), Clock(left),
                      left <= 60f ? _warn : _big);
            row += 34f;

            GUI.Label(new Rect(x + 12f, row, w - 24f, 16f), "OBJECTIVE", _label);
            row += 15f;
            GUI.Label(new Rect(x + 12f, row, w - 24f, 34f),
                      RoleInfo.Objective(briefing.Role), _value);
            row += 38f;

            GUI.Label(new Rect(x + 12f, row, w - 24f, 60f), Checklist(briefing.Role), _small);

            DrawWhereAmI();
            DrawWhereToGo();

            GUI.Label(new Rect(x, y + h + 4f, w, 18f), $"[{NotesKey}] your case notes", _small);
        }

        /// <summary>
        /// The current room, bottom-centre. The single cheapest fix for "I do not
        /// know where I am in this building".
        /// </summary>
        private void DrawWhereAmI()
        {
            string room = CurrentRoom();
            if (string.IsNullOrEmpty(room)) return;

            var area = new Rect((Screen.width - 260f) * 0.5f, Screen.height - 62f, 260f, 26f);
            GUI.Label(area, room.ToUpperInvariant(), Centered(_big));
        }

        /// <summary>
        /// What the rooms are for. Static, and that is fine.
        ///
        /// Dropped on a narrow window: at 480px the left panel and this one would
        /// meet in the middle, and two overlapping panels teach less than one.
        /// </summary>
        private void DrawWhereToGo()
        {
            float w = 250f, h = 112f;
            if (Screen.width < 640f) return;

            float x = Screen.width - w - 16f, y = 16f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.Label(new Rect(x + 12f, y + 8f, w - 24f, 16f), "WHERE TO GO", _label);
            GUI.Label(new Rect(x + 12f, y + 26f, w - 24f, h - 34f),
                "ARCHIVE — search records\n" +
                "WITNESS LOUNGE — interview\n" +
                "FORENSICS LAB — process samples\n" +
                "EVIDENCE LOCKER — register\n" +
                "COURTROOM — when time is up", _small);
        }

        private void DrawEnding(MatchFlowController flow)
        {
            float w = 460f, h = 96f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.18f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, 4f, h), _danger);

            GUI.Label(new Rect(x, y + 10f, w, 26f), "INVESTIGATION OVER", Centered(_big));
            GUI.Label(new Rect(x, y + 40f, w, 22f), "REPORT TO THE COURTROOM", Centered(_value));

            string hint = flow.Phase == MatchPhase.CourtroomTransition
                ? $"North from the atrium — {Mathf.CeilToInt(flow.SecondsRemaining)}s before you are moved"
                : "Wrapping up…";
            GUI.Label(new Rect(x, y + 66f, w, 20f), hint, Centered(_small));
        }

        private void DrawCourtroomReady()
        {
            GUI.Label(new Rect(0f, Screen.height * 0.2f, Screen.width, 30f),
                      "COURT IS IN SESSION", Centered(_big));
            GUI.Label(new Rect(0f, Screen.height * 0.2f + 30f, Screen.width, 22f),
                      "(trial not implemented yet)", Centered(_small));
        }

        /// <summary>Their own notes, and nobody else's.</summary>
        private void DrawNotes(MatchFlowController flow)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool held = keyboard != null &&
                        System.Enum.IsDefined(typeof(UnityEngine.InputSystem.Key), NotesKey) &&
                        keyboard[NotesKey].isPressed;

            // After the bell the notes are the point, so show them unprompted.
            bool auto = flow.Phase is MatchPhase.InvestigationEnding
                                   or MatchPhase.CourtroomTransition
                                   or MatchPhase.CourtroomReady;
            if (!held && !auto) return;

            var n = flow.LocalNotes;
            float w = 420f, h = 300f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.38f;

            GUI.DrawTexture(new Rect(x, y, w, h), _panel);
            GUI.DrawTexture(new Rect(x, y, 3f, h), _accent);

            GUILayout.BeginArea(new Rect(x + 14f, y + 10f, w - 28f, h - 20f));
            GUILayout.Label("YOUR CASE NOTES", _big);
            GUILayout.Space(6f);
            GUILayout.Label($"EVIDENCE YOU LEARNED ({n.EvidenceCount})", _label);
            GUILayout.Label(n.EvidenceLearned.ToString(), _small);
            GUILayout.Space(6f);
            GUILayout.Label($"WITNESSES YOU INTERVIEWED ({n.WitnessCount})", _label);
            GUILayout.Label(n.WitnessesInterviewed.ToString(), _small);
            GUILayout.Space(6f);
            GUILayout.Label($"EVIDENCE YOU REGISTERED ({n.RegisteredCount})", _label);
            GUILayout.Label(n.EvidenceRegistered.ToString(), _small);
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Warnings scaled to the actual duration, so a 5-minute development run does
        /// not fire "5:00 remaining" the instant it starts and then sit silent.
        /// </summary>
        private void WarnIfDue(MatchFlowController flow)
        {
            float left = flow.SecondsRemaining;
            float total = Mathf.Max(1f, flow.PhaseTotalSeconds);

            float[] marks = total > 600f
                ? new[] { 300f, 120f, 60f, 30f }
                : new[] { total * 0.5f, 120f, 60f, 30f };

            foreach (float mark in marks.Where(m => m < total).Distinct().OrderByDescending(m => m))
            {
                if (left <= mark && _lastWarned != mark)
                {
                    _lastWarned = mark;
                    Banner(mark <= 30f ? "COURTROOM SOON — 30 SECONDS"
                                       : $"{Clock(mark)} REMAINING");
                    break;
                }
            }
        }

        private void Banner(string text) { _banner = text; _bannerUntil = Time.time + 3f; }

        private void DrawBanner()
        {
            if (Time.time > _bannerUntil || string.IsNullOrEmpty(_banner)) return;
            GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, 30f), _banner, Centered(_warn));
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        private static string CaseTitle()
        {
            var net = Object.FindAnyObjectByType<CaseNetworkController>();
            string title = net != null ? net.PublicInfo.Title.ToString() : "";
            return string.IsNullOrEmpty(title) ? "(case pending)" : title;
        }

        /// <summary>Reuses the RoomVolumes the greybox already places. No new system.</summary>
        private static string CurrentRoom()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsClient) return "";
            var player = manager.LocalClient.PlayerObject;
            if (player == null) return "";

            foreach (var volume in Object.FindObjectsByType<RoomVolume>(FindObjectsInactive.Exclude))
                if (!volume.IsTransitional && volume.Contains(player.transform.position))
                    return volume.RoomName;

            return "";
        }

        /// <summary>
        /// Practical next actions per role. Only things that actually exist today —
        /// no invented abilities, and no restrictions the game does not enforce.
        /// </summary>
        private static string Checklist(PlayerRole role) => role switch
        {
            PlayerRole.Defendant =>
                "You know whether you did it.\n" +
                "• Talk to your attorney\n" +
                "• Decide what to tell them\n" +
                "• Help investigate",

            PlayerRole.DefenseAttorney =>
                "• Speak with your client\n" +
                "• Search the Archive\n" +
                "• Interview witnesses\n" +
                "• Register helpful evidence",

            PlayerRole.Prosecutor =>
                "• Search the Archive\n" +
                "• Interview witnesses\n" +
                "• Review forensic results\n" +
                "• Register evidence\n" +
                "Convicting an innocent defendant\nwill carry a penalty.",

            PlayerRole.Investigator =>
                "• Search the Archive\n" +
                "• Interview witnesses\n" +
                "• Process samples in the Lab\n" +
                "• Bring findings to the Prosecutor",

            _ => "• Explore the courthouse",
        };

        private void Field(float x, ref float y, float w, string label, string value)
        {
            GUI.Label(new Rect(x + 12f, y, w - 24f, 16f), label, _label);
            y += 15f;
            GUI.Label(new Rect(x + 12f, y, w - 24f, 20f), value, _value);
            y += 22f;
        }

        private static GUIStyle Centered(GUIStyle style) =>
            new(style) { alignment = TextAnchor.MiddleCenter };

        private void EnsureStyles()
        {
            if (_value != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 10 };
            _label.normal.textColor = new Color(0.62f, 0.62f, 0.66f);

            _value = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _value.normal.textColor = Color.white;

            _big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _big.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _huge = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold };
            _huge.normal.textColor = new Color(1f, 0.85f, 0.45f);

            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _small.normal.textColor = new Color(0.75f, 0.75f, 0.78f);

            _warn = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _warn.normal.textColor = new Color(1f, 0.42f, 0.35f);

            _panel = Solid(new Color(0.05f, 0.05f, 0.07f, 0.9f));
            _accent = Solid(new Color(1f, 0.85f, 0.45f));
            _danger = Solid(new Color(1f, 0.42f, 0.35f));
        }

        private static Texture2D Solid(Color color)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }
    }
}
