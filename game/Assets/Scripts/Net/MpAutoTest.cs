using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Headless multiplayer smoke test. Activated only by command line:
    ///   CaseClosed.exe -batchmode -nographics -mpauto host -mptest -logFile host.log
    ///   CaseClosed.exe -batchmode -nographics -mpauto client -mptest -logFile client.log
    /// The owner oscillates east-west; every 2s each instance logs every player's
    /// position; at t=12 the host collects Evidence_0; at t=22 both log the final
    /// evidence count; t=26 quit. Assertions live in the harness that parses logs:
    /// remote positions must CHANGE, and EVFOUND must be 1 on both instances.
    /// </summary>
    public class MpAutoTest : MonoBehaviour
    {
        private string _mode;
        private bool _test;
        private float _t;
        private bool _collected, _reported;

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-mpauto");
            if (i >= 0 && i + 1 < args.Length) _mode = args[i + 1].ToLowerInvariant();
            _test = args.Contains("-mptest");
            if (_mode == null) return;

            if (_mode == "host")
            {
                NetworkManager.Singleton.StartHost();
                Debug.Log("[MPTEST] started HOST");
            }
            else
            {
                Invoke(nameof(StartClient), 3f); // let the host bind first
            }
            if (_test) InvokeRepeating(nameof(Report), 2f, 2f);
        }

        private void StartClient()
        {
            NetworkManager.Singleton.StartClient();
            Debug.Log("[MPTEST] started CLIENT");
        }

        private void Update()
        {
            if (_mode == null || !_test) return;
            _t += Time.deltaTime;

            // drive the owned player east-west so remotes have motion to replicate
            var own = FindObjectsByType<NetPlayer>()
                .FirstOrDefault(p => p.IsOwner);
            if (own != null)
            {
                var cc = own.GetComponent<CharacterController>();
                if (cc != null && cc.enabled)
                    cc.Move(new Vector3(Mathf.Sin(_t * 1.5f) * 3f, -1f, 0f) * Time.deltaTime);
            }

            if (_mode == "host" && !_collected && _t > 12f)
            {
                _collected = true;
                Debug.Log("[MPTEST] host collecting Evidence_0");
                CaseNetSync.Instance.RequestCollect(0, "smoke-test item");
            }

            if (!_reported && _t > 22f)
            {
                _reported = true;
                Debug.Log($"[MPTEST] EVFOUND:{(CaseRuntime.Instance != null ? CaseRuntime.Instance.EvidenceFound : -1)}");
            }

            if (_t > 26f) Application.Quit();
        }

        private void Report()
        {
            foreach (var p in FindObjectsByType<NetPlayer>())
                Debug.Log($"[MPTEST] {(p.IsOwner ? "OWN " : "REMOTE")} id={p.OwnerClientId} " +
                          $"pos=({p.transform.position.x:F2},{p.transform.position.y:F2},{p.transform.position.z:F2})");
        }
    }
}

