using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CaseClosed.Game.Prototype.Net
{
    /// <summary>
    /// WHY THIS EXISTS: two-player sync cannot be verified from inside one process.
    /// This lets two built instances drive themselves and dump enough state to a log
    /// that a harness can assert on it. Same idea as the existing MpAutoTest, but for
    /// the movement prototype and with no case dependencies.
    ///
    /// Completely inert unless launched with -mpauto, so it costs nothing in play.
    ///
    ///   PlayerPrototype.exe -mpauto host   -mptest -logFile host.log
    ///   PlayerPrototype.exe -mpauto client -mptest -logFile client.log
    ///
    /// It drives a SYNTHETIC KEYBOARD rather than shoving the CharacterController
    /// around. That matters: pushing the controller directly would move the body
    /// while leaving PlayerMovement's speed and state at zero, so the animation half
    /// of the sync would silently go untested. Going in through the real input path
    /// exercises input -> movement -> animation -> network end to end.
    ///
    /// Assertions that matter, per instance:
    ///   * the REMOTE player's position must CHANGE (not frozen at spawn)
    ///   * the REMOTE player's replicated speed must go above zero
    ///   * the REMOTE player's replicated state must reach Run/Sprint and Jump
    /// </summary>
    public class PrototypeMpAutoTest : MonoBehaviour
    {
        private string _mode;
        private bool _test;
        private bool _voiceTone;
        private bool _toneApplied;
        private bool _wantsCase;
        private bool _caseRequested;
        private ulong _caseSeed;
        private float _leaveAfter = -1f;
        private bool _left;
        private float _t;
        private int _phase = -1;

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-mpauto");
            if (i >= 0 && i + 1 < args.Length) _mode = args[i + 1].ToLowerInvariant();
            _test = args.Contains("-mptest");
            if (_mode == null) return;

            Application.runInBackground = true;

            // -batchmode has no real keyboard. A virtual device gives the normal
            // input path something to read, so nothing in the game has to know it
            // is being driven by a test.
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();

            // -voicetone makes this instance transmit a synthetic tone, so proximity
            // voice can be verified end to end without a microphone.
            _voiceTone = args.Contains("-voicetone");

            // -caseseed <n> makes the HOST generate a case once everyone is connected,
            // so case replication and secrecy can be checked across two processes.
            // -leaveafter <seconds> makes this instance shut down cleanly mid-test.
            // A hard kill is NOT equivalent: it sends nothing, so the server only
            // notices after the transport timeout (~30 s), which is longer than the
            // test runs. Graceful leave is also what a player clicking DISCONNECT does.
            int leaveIndex = Array.IndexOf(args, "-leaveafter");
            if (leaveIndex >= 0 && leaveIndex + 1 < args.Length &&
                float.TryParse(args[leaveIndex + 1], out float leaveAt))
                _leaveAfter = leaveAt;

            // -runseconds <n> lets a longer scenario play out: the investigation now
            // has a countdown, a clock and a courtroom transition after it, none of
            // which fit in the 24 s this harness originally needed.
            int runIndex = Array.IndexOf(args, "-runseconds");
            if (runIndex >= 0 && runIndex + 1 < args.Length &&
                float.TryParse(args[runIndex + 1], out float runFor))
                _runSeconds = runFor;

            // -investigation <n> shortens the phase itself, so a whole match fits in
            // a test run without touching the production default.
            int invIndex = Array.IndexOf(args, "-investigation");
            if (invIndex >= 0 && invIndex + 1 < args.Length &&
                float.TryParse(args[invIndex + 1], out float invFor))
                _investigationSeconds = invFor;

            int caseIndex = Array.IndexOf(args, "-caseseed");
            if (caseIndex >= 0 && caseIndex + 1 < args.Length &&
                ulong.TryParse(args[caseIndex + 1], out ulong parsedSeed))
            {
                _caseSeed = parsedSeed;
                _wantsCase = true;
            }

            if (_mode == "host")
            {
                NetworkManager.Singleton.StartHost();
                Debug.Log("[MPPROTO] started HOST");
            }
            else
            {
                Invoke(nameof(StartClient), 3f); // give the host time to bind
            }

            if (_test) InvokeRepeating(nameof(Report), 7f, 1.5f);
        }

        private void StartClient()
        {
            NetworkManager.Singleton.StartClient();
            Debug.Log("[MPPROTO] started CLIENT");
        }

        private void Send(params Key[] keys)
        {
            if (Keyboard.current == null) return;
            // Queue only. Letting the normal input tick apply it is what makes
            // wasPressedThisFrame visible to every script in the frame.
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        }

        private void Update()
        {
            if (_mode == null || !_test) return;
            _t += Time.deltaTime;
            if (_t < 5f) return; // let both sides connect and spawn first

            if (_leaveAfter > 0f && !_left && _t > _leaveAfter)
            {
                _left = true;
                Debug.Log("[MPPROTO] leaving the session cleanly");
                NetworkManager.Singleton.Shutdown();
                Application.Quit();
                return;
            }

            float elapsed = _t - 5f;

            if (_voiceTone && !_toneApplied)
            {
                var mine = FindObjectsByType<PrototypeNetPlayer>().FirstOrDefault(p => p.IsOwner);
                var capture = mine != null
                    ? mine.GetComponent<CaseClosed.Game.Prototype.Voice.VoiceCapture>() : null;
                if (capture != null)
                {
                    capture.UseTestTone = true;
                    _toneApplied = true;
                    Debug.Log("[MPPROTO] transmitting synthetic voice tone");
                }
            }

            // Host generates once, a few seconds in, so the client is connected and
            // we can observe replication rather than initial spawn state.
            if (_wantsCase && !_caseRequested && _mode == "host" && elapsed > 3f)
            {
                var flow = CaseClosed.Game.Match.MatchFlowController.Instance;
                if (flow != null && flow.IsSpawned)
                {
                    if (_investigationSeconds > 0f)
                    {
                        flow.UseDevelopmentDuration = true;
                        flow.DevelopmentDurationSeconds = _investigationSeconds;
                        flow.CourtroomWalkSeconds = 5f;
                    }

                    // The whole sequence: roles, case, briefings, then wait on Ready.
                    flow.HostStartMatch(_caseSeed);
                    _caseRequested = true;
                }
            }

            // Every instance presses Ready a moment after its briefing arrives, so
            // the ready count and the phase advance can be observed across processes.
            var matchFlow = CaseClosed.Game.Match.MatchFlowController.Instance;
            if (matchFlow != null && matchFlow.IsSpawned && matchFlow.HasBriefing &&
                !matchFlow.LocalReadySent && elapsed > 6f)
            {
                matchFlow.RequestReady();
                Debug.Log("[MPREADY] pressed READY");
            }

            // walk -> run -> sprint -> jump -> back, on a loop.
            // Host and client walk opposite directions so their paths differ.
            bool host = _mode == "host";
            Key forward = host ? Key.W : Key.S;

            int phase = ((int)(elapsed / 2.5f)) % 4;
            if (phase != _phase)
            {
                _phase = phase;
                switch (phase)
                {
                    case 0: Send(forward, Key.LeftCtrl); break;  // walk
                    case 1: Send(forward); break;                // run
                    case 2: Send(forward, Key.LeftShift); break; // sprint
                    case 3: Send(forward, Key.Space); break;     // jump while moving
                }
                Debug.Log($"[MPPROTO] phase {phase}");
            }

            DriveInvestigation(elapsed);

            if (_t > _runSeconds) Application.Quit();
        }

        /// <summary>
        /// The host performs world actions through the SERVER entry points, and both
        /// instances report what they locally know.
        ///
        /// WHY SERVER-SIDE RATHER THAN SYNTHETIC AIMING: walking 40 m and landing a
        /// crosshair on a drawer inside a scripted window is a test of the test. The
        /// interesting question across two processes is not "can a robot aim" but
        /// "does what the host learned stay off the client" — and that is answered by
        /// driving the real custody/knowledge path on one machine and reading the
        /// other machine's own view of it.
        /// </summary>
        private void DriveInvestigation(float elapsed)
        {
            var flow = CaseClosed.Game.Match.MatchFlowController.Instance;
            if (flow == null || !flow.IsSpawned) return;
            if (_mode != "host") return;
            if (flow.Phase != CaseClosed.Game.Match.MatchPhase.Investigation) return;

            // One search, one pickup, one interview — enough to make the client's
            // knowledge provably different from the host's.
            if (!_didSearch && elapsed > 12f)
            {
                _didSearch = true;
                var archive = CaseClosed.Game.Archive.ArchiveDirector.Instance;
                if (archive != null)
                {
                    foreach (var container in FindObjectsByType<CaseClosed.Game.Archive.ArchiveContainer>())
                        archive.ServerResolveSearch(container, NetworkManager.Singleton.LocalClientId);
                    Debug.Log("[MPDRIVE] host searched every Archive container");
                }
            }

            if (!_didPickup && elapsed > 14f)
            {
                _didPickup = true;
                var custody = CaseClosed.Game.Archive.EvidenceCustodyDirector.Instance;
                var body = FindObjectsByType<CaseClosed.Game.Archive.PhysicalEvidence>()
                    .FirstOrDefault(b => b.InUse && b.IsAvailable);
                if (custody != null && body != null)
                {
                    // Stand next to it so the server's own distance check passes.
                    var me = NetworkManager.Singleton.LocalClient.PlayerObject;
                    var cc = me.GetComponent<CharacterController>();
                    bool had = cc != null && cc.enabled;
                    if (had) cc.enabled = false;
                    me.transform.position = body.transform.position + new Vector3(0f, -0.9f, -1.0f);
                    if (had) cc.enabled = true;

                    custody.ServerRequestPickup(body, NetworkManager.Singleton.LocalClientId);
                    Debug.Log($"[MPDRIVE] host picked up {body.EvidenceId}");
                }
            }

            if (!_didInterview && elapsed > 16f)
            {
                _didInterview = true;
                var witnesses = CaseClosed.Game.Witnesses.WitnessDirector.Instance;
                var npc = FindObjectsByType<CaseClosed.Game.Witnesses.WitnessNpc>()
                    .FirstOrDefault(n => n.Assigned);
                if (witnesses != null && npc != null)
                {
                    witnesses.ServerCompleteInterview(npc.DisplayName, NetworkManager.Singleton.LocalClientId);
                    Debug.Log($"[MPDRIVE] host interviewed {npc.DisplayName}");
                }
            }
        }

        private bool _didSearch, _didPickup, _didInterview;
        private float _runSeconds = 24f;
        private float _investigationSeconds = -1f;

        /// <summary>
        /// What THIS process knows and sees. Comparing the two logs is the whole
        /// point: the same line printed by host and client must agree about public
        /// facts (phase, clock, who is carrying what) and must NOT agree about
        /// private ones (which documents and statements each has read).
        /// </summary>
        private void ReportInvestigation()
        {
            string who = _mode == "host" ? "HOST  " : "CLIENT";

            var flow = CaseClosed.Game.Match.MatchFlowController.Instance;
            if (flow != null && flow.IsSpawned)
                Debug.Log($"[MPPHASE] {who} phase={flow.Phase} " +
                          $"remaining={flow.SecondsRemaining:F1} total={flow.PhaseTotalSeconds:F0} " +
                          $"investigationActive={flow.InvestigationActive}");

            // PRIVATE knowledge, as this machine holds it.
            var carryHud = FindAnyObjectByType<CaseClosed.Game.Archive.EvidenceCarryHud>();
            int evidenceKnown = carryHud != null ? carryHud.KnownCount : -1;
            int witnessKnown = CaseClosed.Game.Witnesses.WitnessDirector.KnownStatements.Count;

            Debug.Log($"[MPKNOW] {who} evidenceKnownLocally={evidenceKnown} " +
                      $"witnessStatementsLocally={witnessKnown} " +
                      $"carrying={(CaseClosed.Game.Archive.EvidenceCustodyDirector.Instance?.LocalIsCarrying ?? false)}");

            // PUBLIC: every machine should agree about who holds which folder.
            foreach (var body in FindObjectsByType<CaseClosed.Game.Archive.PhysicalEvidence>())
            {
                if (!body.InUse) continue;
                var renderer = body.GetComponent<Renderer>();
                Debug.Log($"[MPCARRY] {who} id={body.EvidenceId} custody={body.Custody} " +
                          $"carrier={body.CarrierClientId} " +
                          $"drawnAt=({body.transform.position.x:F2},{body.transform.position.y:F2},{body.transform.position.z:F2}) " +
                          $"visible={(renderer != null && renderer.enabled)}");
            }
        }

        /// <summary>
        /// The line that proves secrecy across processes: every instance prints
        /// whether it holds the hidden truth. The host must say YES, the client NO.
        /// </summary>
        private void ReportCase()
        {
            var controller = CaseClosed.Game.Cases.CaseNetworkController.Instance;
            if (controller == null || !controller.IsSpawned) return;

            var vault = CaseClosed.Game.Cases.ActiveCaseManager.Instance;
            bool holdsTruth = vault != null && vault.HasCase;
            var info = controller.PublicInfo;
            var view = controller.LocalView;

            Debug.Log($"[MPCASE] role={(_mode == "host" ? "HOST  " : "CLIENT")} " +
                      $"state={controller.State} " +
                      $"holdsHiddenTruth={(holdsTruth ? "YES" : "NO")} " +
                      $"publicTitle=\"{info.Title}\" " +
                      $"publicSeed={info.Seed}");

            if (controller.HasLocalView)
                Debug.Log($"[MPCASE] {(_mode == "host" ? "HOST  " : "CLIENT")} " +
                          $"myRole={view.Role} knowsGuilt={view.KnowsOwnGuilt} guiltBit={view.IsActuallyGuilty}");

            // Briefing + readiness, per machine. The guilt field is printed for every
            // role on purpose: if a non-defendant ever shows guilt=True, that is the
            // leak this whole milestone exists to prevent.
            var flow = CaseClosed.Game.Match.MatchFlowController.Instance;
            if (flow != null && flow.IsSpawned)
            {
                var card = flow.LocalBriefing;
                Debug.Log($"[MPBRIEF] {(_mode == "host" ? "HOST  " : "CLIENT")} " +
                          $"phase={flow.Phase} " +
                          $"role={card.Role} team={card.Team} " +
                          $"knowsGuilt={card.KnowsOwnGuilt} guiltBit={card.IsActuallyGuilty} " +
                          $"ready={flow.ReadyCount}/{flow.RequiredCount}");
            }

            // The roster is public, so every machine should agree on the table.
            var roster = CaseClosed.Game.Cases.Roles.PlayerRoster.Instance;
            if (roster != null && roster.IsSpawned && roster.Count > 0)
            {
                var seats = new System.Text.StringBuilder();
                foreach (var pair in roster.Snapshot()) seats.Append($"{pair.Key}:{pair.Value} ");

                Debug.Log($"[MPROLE] {(_mode == "host" ? "HOST  " : "CLIENT")} " +
                          $"localRole={roster.LocalRole} " +
                          $"defendantMissing={roster.DefendantMissing} " +
                          $"table=[ {seats}]");
            }

            if (holdsTruth)
                Debug.Log($"[MPCASE] HOST   truthDigest={vault.Truth.Digest().Substring(0, 30)} " +
                          $"perp={vault.Truth.File.Perpetrator ?? "-"}");
        }

        private void Report()
        {
            ReportCase();
            ReportInvestigation();

            foreach (var player in FindObjectsByType<PrototypeNetPlayer>())
            {
                var movement = player.GetComponent<PlayerMovement>();
                var sync = player.GetComponent<PlayerNetworkSync>();
                Vector3 p = player.transform.position;

                if (player.IsOwner)
                {
                    Debug.Log($"[MPPROTO] OWN    id={player.OwnerClientId} " +
                              $"pos=({p.x:F2},{p.y:F2},{p.z:F2}) " +
                              $"speed={(movement != null ? movement.CurrentSpeed : -1f):F2} " +
                              $"state={(movement != null ? movement.State.ToString() : "?")}");
                }
                else
                {
                    // PlayerMovement is disabled here, so read what the network sent.
                    var voice = player.GetComponent<CaseClosed.Game.Prototype.Voice.PlayerVoice>();
                    var playback = player.GetComponent<CaseClosed.Game.Prototype.Voice.VoicePlayback>();

                    var me = FindObjectsByType<PrototypeNetPlayer>().FirstOrDefault(x => x.IsOwner);
                    float distance = me != null ? Vector3.Distance(me.transform.position, p) : -1f;

                    Debug.Log($"[MPPROTO] REMOTE id={player.OwnerClientId} " +
                              $"pos=({p.x:F2},{p.y:F2},{p.z:F2}) " +
                              $"netSpeed={(sync != null ? sync.ReplicatedSpeed : -1f):F2} " +
                              $"netState={(sync != null ? sync.ReplicatedState.ToString() : "?")}");

                    Debug.Log($"[MPVOICE] dist={distance:F1}m " +
                              $"range={(voice != null ? voice.MaxVoiceDistance : -1f):F0}m " +
                              $"speaking={(voice != null && voice.IsSpeaking)} " +
                              $"audioArriving={(playback != null && playback.IsPlaying)} " +
                              $"level={(playback != null ? playback.OutputLevel : 0f):F3}");
                }
            }
        }
    }
}
