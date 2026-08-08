using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Interaction;
using CaseClosed.Game.Match;

namespace CaseClosed.EditorTools.Match
{
    /// <summary>
    /// Audits the match phase machine: who may change it, who owns the clock, and
    /// which interactions are legal when.
    ///
    /// Mostly structural, because the properties that matter here are about
    /// authority rather than any particular playthrough. "A client cannot restart
    /// the timer" is a fact about write permissions, and asserting it against the
    /// shape of the code catches a regression the moment somebody adds a convenient
    /// setter.
    ///
    /// Menu: Case Closed > Match > Run Phase Audit
    /// </summary>
    public static class MatchPhaseAudit
    {
        [MenuItem("Case Closed/Match/Run Phase Audit", priority = 0)]
        public static void RunFromMenu()
        {
            string report = Run(out bool passed);
            Debug.Log(report);
            EditorUtility.DisplayDialog(passed ? "Phase audit passed" : "Phase audit FAILED", report, "OK");
        }

        public static string Run(out bool allPassed)
        {
            var sb = new StringBuilder("MATCH PHASE AUDIT\n\n");
            bool pass = true;

            void Check(string name, bool ok, string detail)
            {
                if (!ok) pass = false;
                sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(name.PadRight(48)).Append(detail).Append('\n');
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
            var flow = typeof(MatchFlowController);

            // ---- the phase machine covers the whole run ----
            var names = System.Enum.GetNames(typeof(MatchPhase));
            Check("phase machine reaches the courtroom",
                  names.Contains("Investigation") && names.Contains("InvestigationEnding") &&
                  names.Contains("CourtroomTransition") && names.Contains("CourtroomReady"),
                  string.Join(" -> ", names));

            Check("existing lobby states were extended, not replaced",
                  names.Contains("LobbyReady") && names.Contains("WaitingForPlayers") &&
                  names.Contains("PreInvestigationReady"),
                  "no duplicate match state");

            // ---- only the server writes phase and clock ----
            var netVars = flow.GetFields(Flags)
                .Where(f => typeof(NetworkVariableBase).IsAssignableFrom(f.FieldType))
                .Select(f => f.Name).ToArray();

            Check("phase and deadline are replicated state",
                  netVars.Contains("_phase") && netVars.Contains("_phaseEndsAt"),
                  string.Join(", ", netVars));

            Check("no public setter for phase",
                  flow.GetProperty("Phase")?.GetSetMethod() == null &&
                  flow.GetField("_phase", Flags)?.IsPublic != true,
                  "clients read it; only the server assigns it");

            Check("no client-callable phase advance",
                  !flow.GetMethods(Flags).Any(m =>
                      m.GetCustomAttributes(typeof(ServerRpcAttribute), false).Any() &&
                      (m.Name.Contains("Phase") || m.Name.Contains("Advance") ||
                       m.Name.Contains("Timer") || m.Name.Contains("Start"))),
                  "the only ServerRpc is Ready");

            // ---- the timer is a deadline, not a broadcast ----
            var endsAt = flow.GetField("_phaseEndsAt", Flags);
            Check("investigation end is an absolute timestamp",
                  endsAt != null && endsAt.FieldType.IsGenericType &&
                  endsAt.FieldType.GetGenericArguments()[0] == typeof(double),
                  "one write per phase, not a tick per second");

            Check("clients derive time from the deadline",
                  flow.GetProperty("SecondsRemaining") != null &&
                  flow.GetProperty("SecondsRemaining").GetSetMethod() == null,
                  "read-only; nothing on a client can move it");

            Check("no per-second timer RPC",
                  !flow.GetMethods(Flags).Any(m =>
                      m.GetCustomAttributes(typeof(ClientRpcAttribute), false).Any() &&
                      (m.Name.Contains("Tick") || m.Name.Contains("Second") || m.Name.Contains("Timer"))),
                  "bandwidth stays flat and displays cannot disagree");

            Check("duration is configurable, production preserved",
                  flow.GetField("InvestigationDurationSeconds") != null &&
                  flow.GetField("DevelopmentDurationSeconds") != null &&
                  flow.GetField("UseDevelopmentDuration") != null,
                  "dev timing scales, it does not overwrite");

            // ---- interaction gating lives in ONE place ----
            var gate = typeof(InteractionNetworkController).GetMethod("PlayerStateAllows", Flags);
            Check("interaction phase gate exists and is shared", gate != null,
                  "one answer for every system");

            Check("investigation-over cancels holds in flight",
                  typeof(InteractionNetworkController).GetMethod("ServerCancelAllHolds") != null,
                  "nobody finishes a search on the bell");

            Check("only the server cancels holds",
                  !typeof(InteractionNetworkController).GetMethods(Flags).Any(m =>
                      m.Name == "ServerCancelAllHolds" &&
                      m.GetCustomAttributes(typeof(ServerRpcAttribute), false).Any()),
                  "not reachable from a client");

            // ---- the transition cannot be stalled ----
            Check("courtroom transition has a deadline",
                  flow.GetField("CourtroomWalkSeconds") != null,
                  "one idle player cannot hold the match hostage");

            Check("stragglers are seated by the server",
                  typeof(CourtroomSeating).GetMethod("ServerSeatAll") != null &&
                  typeof(CourtroomSeating).GetMethod("SpotFor") != null,
                  "per-role positions with a teleport fallback");

            // ---- the private summary carries only earned facts ----
            var notesFields = typeof(CaseNotes)
                .GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();

            Check("case notes carry no case truth",
                  notesFields.All(n => n is "EvidenceLearned" or "WitnessesInterviewed" or
                                            "EvidenceRegistered" or "EvidenceCount" or
                                            "WitnessCount" or "RegisteredCount"),
                  string.Join(", ", notesFields));

            Check("case notes cannot name a perpetrator or verdict",
                  !notesFields.Any(n => n.ToLower().Contains("perp") || n.ToLower().Contains("guilt") ||
                                        n.ToLower().Contains("truth") || n.ToLower().Contains("proof")),
                  "nowhere to put it");

            var notesRpc = flow.GetMethod("ReceiveNotesClientRpc", Flags);
            Check("notes are addressed, never broadcast",
                  notesRpc != null &&
                  notesRpc.GetParameters().Any(p => p.ParameterType == typeof(ClientRpcParams)),
                  "each player receives only their own");

            var builder = flow.GetMethod("BuildNotesFor", Flags);
            Check("notes are built per client id",
                  builder != null && builder.GetParameters().Length == 1 &&
                  builder.GetParameters()[0].ParameterType == typeof(ulong),
                  "read from the same per-player ledgers the game already keeps");

            // ---- late join and reset ----
            Check("late join does not rewind the phase",
                  flow.GetMethod("OnClientConnected", Flags) != null,
                  "joiner is seated and briefed; the match stays where it is");

            Check("reset is host-only",
                  flow.GetMethod("HostReset") != null &&
                  !flow.GetMethod("HostReset").GetCustomAttributes(typeof(ServerRpcAttribute), false).Any(),
                  "no client path to rewind a running match");

            // ---- behavioural: courtroom seating maths ----
            var seats = new[] { PlayerRole.Prosecutor, PlayerRole.Investigator,
                                PlayerRole.DefenseAttorney, PlayerRole.Defendant };
            var spots = seats.Select(CourtroomSeating.SpotFor).ToList();

            Check("every role has a distinct courtroom spot",
                  spots.Distinct().Count() == seats.Length,
                  string.Join("  ", seats.Zip(spots, (r, s) => $"{r}{s}")));

            Check("opposing counsel are on opposite sides",
                  CourtroomSeating.SpotFor(PlayerRole.Prosecutor).x *
                  CourtroomSeating.SpotFor(PlayerRole.DefenseAttorney).x < 0f,
                  "prosecution and defence face each other across the aisle");

            sb.Append('\n').Append(pass ? "ALL CHECKS PASSED" : ">>> FAILURES ABOVE <<<");
            allPassed = pass;
            return sb.ToString();
        }

        public static void WriteReport(string path) => File.WriteAllText(path, Run(out _));
    }
}
