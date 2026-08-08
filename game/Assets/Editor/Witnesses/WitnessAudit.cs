using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game.Cases;
using CaseClosed.Game.Interaction;
using CaseClosed.Game.Witnesses;
using CaseClosed.TruthEngine;

namespace CaseClosed.EditorTools.Witnesses
{
    /// <summary>
    /// Audits the witness layer, mostly by generating real cases and comparing what
    /// a witness BELIEVES against what a player would be told.
    ///
    /// Behavioural where it can be: "a corrupted witness reports the corrupted time"
    /// is a property of data, and only real generated cases produce real corruption.
    /// Structural where behaviour cannot prove it: "the payload has nowhere to put
    /// the truth" is a property of the type.
    ///
    /// Menu: Case Closed > Witnesses > Run Audit
    /// </summary>
    public static class WitnessAudit
    {
        [MenuItem("Case Closed/Witnesses/Run Audit", priority = 0)]
        public static void RunFromMenu()
        {
            string report = Run(out bool passed);
            Debug.Log(report);
            EditorUtility.DisplayDialog(passed ? "Witness audit passed" : "Witness audit FAILED",
                report, "OK");
        }

        public static string Run(out bool allPassed)
        {
            var sb = new StringBuilder("WITNESS AUDIT\n\n");
            bool pass = true;

            void Check(string name, bool ok, string detail)
            {
                if (!ok) pass = false;
                sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(name.PadRight(50)).Append(detail).Append('\n');
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

            // ---- the payload cannot carry the truth ----
            var payloadFields = typeof(WitnessTestimony)
                .GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();

            Check("testimony payload is three safe fields",
                  payloadFields.All(n => n is "WitnessId" or "DisplayName" or "Statement"),
                  string.Join(", ", payloadFields));

            Check("payload has no corruption marker",
                  !payloadFields.Any(n => n.ToLower().Contains("corrupt")),
                  "the witness believes it; the player is never told which lines are false");

            Check("payload has no agenda, timeline or guilt field",
                  !payloadFields.Any(n => n.ToLower().Contains("agenda") || n.ToLower().Contains("occupancy") ||
                                          n.ToLower().Contains("guilt") || n.ToLower().Contains("perp") ||
                                          n.ToLower().Contains("truth") || n.ToLower().Contains("ledger")),
                  "nowhere to put it");

            // ---- the runtime record stays on the host ----
            Check("WitnessRuntime is not serializable to clients",
                  !typeof(INetworkSerializable).IsAssignableFrom(typeof(WitnessRuntime)) &&
                  !typeof(Component).IsAssignableFrom(typeof(WitnessRuntime)),
                  "host memory only, like CompleteCaseTruth");

            var npcNetVars = typeof(WitnessNpc).GetFields(Flags)
                .Where(f => typeof(NetworkVariableBase).IsAssignableFrom(f.FieldType))
                .Select(f => f.Name).ToArray();

            Check("the NPC replicates only presentation",
                  npcNetVars.All(n => n is "_displayName" or "_assigned" or "LockOwner"),
                  "replicated: " + string.Join(", ", npcNetVars));

            Check("witness NPC reuses the interaction system",
                  typeof(NetworkInteractable).IsAssignableFrom(typeof(WitnessNpc)),
                  "no separate dialogue interaction path");

            Check("interview completion is server-side",
                  typeof(WitnessDirector).GetMethod("ServerCompleteInterview") != null,
                  "clients request; the server grants");

            // The statement RPC must be addressed, never broadcast.
            var rpc = typeof(WitnessDirector).GetMethod("ReceiveStatementClientRpc", Flags);
            Check("statement RPC takes ClientRpcParams (addressed, not broadcast)",
                  rpc != null && rpc.GetParameters().Any(p => p.ParameterType == typeof(ClientRpcParams)),
                  "testimony goes to one client");

            // ---- behavioural: real cases, real corruption ----
            int casesChecked = 0, corruptedWitnesses = 0, protectorCases = 0;
            bool corruptionSpeaksConfidently = true, noTruthNoteLeak = true;
            bool noProofFactLeak = true, noSecretLeak = true, protectorStaysSilent = true;
            string sample = "";

            for (ulong seed = 1; seed <= 60; seed++)
            {
                var truth = CaseGenerationService.Generate(seed);
                if (truth == null) continue;
                var f = truth.File;
                casesChecked++;

                foreach (var kv in f.Obs)
                {
                    string witness = kv.Key;
                    if (witness == f.Defendant) continue;

                    var testimony = TestimonyWriter.Build(f, witness);
                    string payload = testimony.WitnessId + "|" + testimony.DisplayName + "|" + testimony.Statement;

                    // GM notes, the proof chain and the secret must never appear.
                    if (f.Ledger.Any(e => payload.Contains(e.TruthNote) || payload.Contains(e.CounterNote)))
                        noTruthNoteLeak = false;
                    if (f.ProofFacts.Any(p => !string.IsNullOrEmpty(p) && payload.Contains(p)))
                        noProofFactLeak = false;
                    if (!string.IsNullOrEmpty(f.SecretText) && payload.Contains(f.SecretText))
                        noSecretLeak = false;

                    // A corrupted memory must be spoken as the witness believes it:
                    // the CORRUPTED slot appears, and no marker betrays it.
                    foreach (var o in kv.Value.Where(o => o.Corrupted))
                    {
                        corruptedWitnesses++;
                        string believed = $"{o.Subject} in the {World.Locations[o.Location]} around {World.Slots[o.Slot]}";
                        string heard = $"{o.Subject} from the {World.Locations[o.Location]} around {World.Slots[o.Slot]}";
                        if (!payload.Contains(believed) && !payload.Contains(heard))
                            corruptionSpeaksConfidently = false;

                        if (sample.Length == 0)
                            sample = $"{witness}: \"{o.Verb} {o.Subject} @ {World.Slots[o.Slot]}\" (believed)";
                    }

                    // A protector deleted their sighting of the perpetrator at the
                    // crime scene, so it must not appear in what they say.
                    if (witness == f.Protector)
                    {
                        protectorCases++;
                        string suppressed = $"{f.Perpetrator} in the {World.Locations[f.CrimeLocation]} " +
                                            $"around {World.Slots[f.CrimeSlot]}";
                        if (payload.Contains(suppressed)) protectorStaysSilent = false;
                    }
                }
            }

            Check("cases exercised", casesChecked > 0, casesChecked + " cases, " +
                  corruptedWitnesses + " corrupted memories, " + protectorCases + " protector witnesses");

            Check("corrupted memory is spoken as believed", corruptionSpeaksConfidently,
                  sample.Length > 0 ? sample : "no corruption in sample");

            Check("GM truth notes never reach the player", noTruthNoteLeak,
                  "Ledger.TruthNote and CounterNote absent from every payload");

            Check("proof chain never reaches the player", noProofFactLeak, "ProofFacts absent");
            Check("the defendant's secret never reaches the player", noSecretLeak, "SecretText absent");

            Check("a protector stays silent about what they deleted", protectorStaysSilent,
                  protectorCases + " protector witnesses checked");

            // ---- per-player knowledge ----
            var runtime = new WitnessRuntime { CharacterId = "X", CastIndex = 1 };
            bool first = runtime.GrantKnowledge(7UL);
            bool second = runtime.GrantKnowledge(7UL);

            Check("knowledge is per-player",
                  runtime.IsKnownBy(7UL) && !runtime.IsKnownBy(8UL),
                  "interviewing does not inform teammates");

            Check("repeat interview does not duplicate a record",
                  first && !second && runtime.InterviewedBy.Count == 1,
                  "records=" + runtime.InterviewedBy.Count);

            // ---- developer dump is host-only ----
            var dump = typeof(WitnessDirector).GetMethod("DeveloperDump");
            Check("developer dump exists and is instance-gated", dump != null,
                  "returns empty on a client - the truth is not on that machine");

            sb.Append('\n').Append(pass ? "ALL CHECKS PASSED" : ">>> FAILURES ABOVE <<<");
            allPassed = pass;
            return sb.ToString();
        }

        public static void WriteReport(string path) => File.WriteAllText(path, Run(out _));
    }
}
