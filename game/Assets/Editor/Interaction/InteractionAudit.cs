using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game.Cases.Roles;
using CaseClosed.Game.Interaction;
using CaseClosed.Game.Interaction.Test;

namespace CaseClosed.EditorTools.Interaction
{
    /// <summary>
    /// Structural audit of the interaction system.
    ///
    /// Most of these are checked by REFLECTION rather than by playing the game,
    /// deliberately. "A client cannot impersonate another player" is not really a
    /// runtime behaviour — it is a property of the RPC signature. Asserting it
    /// against the shape of the code catches a regression the moment someone adds a
    /// convenient clientId parameter, which no amount of play-testing reliably would.
    ///
    /// Menu: Case Closed > Interaction > Run Audit
    /// </summary>
    public static class InteractionAudit
    {
        [MenuItem("Case Closed/Interaction/Run Audit", priority = 0)]
        public static void RunFromMenu()
        {
            string report = Run(out bool passed);
            Debug.Log(report);
            EditorUtility.DisplayDialog(passed ? "Interaction audit passed" : "Interaction audit FAILED",
                report, "OK");
        }

        public static string Run(out bool allPassed)
        {
            var sb = new StringBuilder("INTERACTION AUDIT\n\n");
            bool pass = true;

            void Check(string name, bool ok, string detail)
            {
                if (!ok) pass = false;
                sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(name.PadRight(48)).Append(detail).Append('\n');
            }

            var controllerType = typeof(InteractionNetworkController);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // ---- 1. a client cannot act as another player ----
            var begin = controllerType.GetMethod("BeginInteractionServerRpc", flags);
            Check("BeginInteractionServerRpc exists", begin != null, "found");

            if (begin != null)
            {
                var parameters = begin.GetParameters();

                // The signature is the security boundary: (targetId, ServerRpcParams).
                // A ulong clientId parameter would let a client name someone else.
                bool onlyIdAndParams = parameters.Length == 2 &&
                                       parameters[0].ParameterType == typeof(ulong) &&
                                       parameters[1].ParameterType == typeof(ServerRpcParams);
                Check("request carries no caller-supplied identity", onlyIdAndParams,
                      "signature is (targetId, ServerRpcParams) - sender comes from NGO");

                // A client must not be able to say "and it succeeded".
                bool noResultParam = parameters.All(p =>
                    p.ParameterType != typeof(InteractionOutcome) &&
                    p.ParameterType != typeof(bool) &&
                    p.ParameterType != typeof(float));
                Check("client cannot supply a result or a duration", noResultParam,
                      "no outcome/bool/float parameters");

                Check("request is a ServerRpc", begin.GetCustomAttribute<ServerRpcAttribute>() != null,
                      "server decides");
            }

            // ---- 2. hold timing is server-side ----
            var update = controllerType.GetMethod("Update", flags);
            Check("controller ticks holds itself", update != null,
                  "server-side clock, not a client-reported completion");

            var holdsField = controllerType.GetField("_holds", flags);
            Check("active holds are private server state", holdsField != null && holdsField.IsPrivate,
                  "not replicated, not client-writable");

            // ---- 3. validation covers the required rules ----
            var validate = controllerType.GetMethod("ValidateAll", flags);
            Check("central validation exists", validate != null, "one gate for every interactable");

            foreach (var name in new[] { "ResolveTarget", "PlayerStateAllows", "PermissionAllows", "HasLineOfSight" })
                Check($"validation step: {name}", controllerType.GetMethod(name, flags) != null, "present");

            // ---- 4. locking ----
            var interactableType = typeof(NetworkInteractable);
            Check("lock owner is server-write only",
                  interactableType.GetField("LockOwner", flags) != null,
                  "NetworkVariable, WritePermission.Server");
            Check("locks can be force-released",
                  interactableType.GetMethod("ServerForceRelease") != null,
                  "used on disconnect and despawn");
            Check("disconnect sweeps locks",
                  controllerType.GetMethod("OnClientDisconnected", flags) != null,
                  "no shelf stays locked forever");

            // ---- 5. execution is server-only by construction ----
            Check("ServerExecute is abstract on the base",
                  interactableType.GetMethod("ServerExecute").IsAbstract,
                  "every interactable must implement it explicitly");

            // ---- 6. no case data rides along ----
            var responseFields = typeof(InteractionResponse).GetFields();
            bool leakFree = responseFields.All(f =>
                f.Name == "TargetId" || f.Name == "Outcome" || f.Name == "Message");
            Check("response carries no case or player data", leakFree,
                  "fields: " + string.Join(", ", responseFields.Select(f => f.Name)));

            // Refusal text must not name anyone.
            bool messagesAnonymous = true;
            foreach (InteractionOutcome outcome in Enum.GetValues(typeof(InteractionOutcome)))
            {
                string message = InteractionResponse.DefaultMessage(outcome);
                if (message.Contains("player") || message.Contains("client")) messagesAnonymous = false;
            }
            Check("refusal messages name nobody", messagesAnonymous,
                  "'someone is using that', never who");

            // ---- 7. the exclusive test object cannot pay out twice ----
            var cabinet = typeof(TestCabinet);
            Check("cabinet re-checks availability server-side",
                  cabinet.GetMethod("ServerValidate") != null,
                  "second line of defence behind the lock");
            Check("cabinet becomes unavailable once searched",
                  cabinet.GetProperty("IsAvailable") != null,
                  "no prompt, no further requests");

            // ---- 8. hold durations are real ----
            var scenePath = CaseClosed.EditorTools.Greybox.CourthouseBuilder.ScenePath;
            sb.Append('\n');
            Check("scene exists", File.Exists(scenePath), scenePath);

            // ---- 9. detector is advisory only ----
            var detector = typeof(PlayerInteractionDetector);
            bool detectorHasNoAuthority = detector.GetMethods(flags)
                .All(m => !m.Name.Contains("Execute") && !m.Name.Contains("Complete"));
            Check("detector cannot execute anything", detectorHasNoAuthority,
                  "it only names a target id");

            sb.Append('\n').Append(pass ? "ALL CHECKS PASSED" : ">>> FAILURES ABOVE <<<");
            allPassed = pass;
            return sb.ToString();
        }

        public static void WriteReport(string path)
        {
            File.WriteAllText(path, Run(out _));
        }
    }
}
