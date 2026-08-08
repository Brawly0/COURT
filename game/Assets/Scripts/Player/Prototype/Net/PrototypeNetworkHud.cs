using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CaseClosed.Game.Prototype.Net
{
    /// <summary>
    /// WHY THIS EXISTS: something has to decide whether this process is the host or
    /// a client, and NGO will not do it for you. Until that choice is made, no
    /// player exists at all.
    ///
    /// Also handles the unglamorous half of multiplayer: showing who is connected,
    /// noticing when someone drops, and getting back to the menu cleanly.
    ///
    /// OnGUI on purpose — this is a dev harness, not the real lobby. The Steam-relay
    /// lobby replaces it later; nothing else depends on it.
    /// </summary>
    public class PrototypeNetworkHud : MonoBehaviour
    {
        [Header("Connection")]
        [Tooltip("Address a client dials. 127.0.0.1 = another instance on this machine.")]
        public string Address = "127.0.0.1";

        public ushort Port = 7777;

        [Header("Offline")]
        [Tooltip("Spawned by PLAY OFFLINE so the movement playground still works solo.")]
        public GameObject OfflinePlayerPrefab;

        private string _status = "";
        private bool _offline;

        private void Start()
        {
            Recall();

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // These fire on the host for every client, and on a client for itself.
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            _status = $"client {clientId} connected";
            Debug.Log($"[Net] {_status}");
        }

        /// <summary>
        /// Two very different events arrive here. If the id is ours we were dropped
        /// and must return to the menu; otherwise somebody else left and the session
        /// simply continues — NGO has already despawned their character for us.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            bool itWasUs = nm != null && clientId == nm.LocalClientId;

            if (itWasUs && nm != null && !nm.IsServer)
            {
                // NGO puts a reason here when the server supplied one; empty is the
                // ordinary case of the host simply going away.
                string reason = nm.DisconnectReason;
                _state = "DISCONNECTED";
                _status = string.IsNullOrEmpty(reason)
                    ? "Connection lost. The host closed, crashed, or the network dropped."
                    : $"Connection lost: {reason}";

                Debug.Log("[Net] local client dropped - returning to menu. " + _status);
                nm.Shutdown();   // back to the menu; nothing here crashes the client
            }
            else
            {
                _status = $"client {clientId} left";
                Debug.Log($"[Net] {_status}");
            }
        }

        /// <summary>
        /// THE LINE THAT DECIDES WHETHER A FRIEND CAN CONNECT AT ALL.
        ///
        /// The two-argument SetConnectionData leaves ServerListenAddress at whatever
        /// was serialised — which was 127.0.0.1, meaning the host bound its socket to
        /// loopback and accepted connections only from its own machine. No IP the
        /// other player typed could ever have worked.
        ///
        /// A HOST must listen on 0.0.0.0 (every interface: loopback, LAN, and the
        /// public side of the router). A CLIENT dials the address the player typed.
        /// Those are different questions, which is exactly why the three-argument
        /// overload exists.
        /// </summary>
        private void ConfigureTransport(bool asHost)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null) return;

            transport.SetConnectionData(Address, Port, asHost ? ListenOnAllInterfaces : null);
        }

        /// <summary>Bind every interface, so LAN and forwarded traffic both arrive.</summary>
        private const string ListenOnAllInterfaces = "0.0.0.0";

        /// <summary>
        /// This machine's LAN address, so the host can read it out to a friend
        /// instead of hunting through ipconfig. Loopback and virtual adapters are
        /// skipped; a 169.254.x.x is reported as-is because "no DHCP" is worth seeing.
        /// </summary>
        public static string LocalLanAddress()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        string text = ip.Address.ToString();
                        if (text.StartsWith("127.")) continue;
                        return text;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Net] could not read LAN address: " + e.Message); }

            return "(unknown)";
        }

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                GUI.Label(new Rect(12, 12, 460, 24), "No NetworkManager in scene.");
                return;
            }

            // Offline mode borrows the scene but not the network - no menu needed.
            if (_offline) return;

            bool live = nm.IsClient || nm.IsServer;
            GUILayout.BeginArea(new Rect(Screen.width - 260, 12, 246, 220));

            if (!live) DrawMenu(nm);
            else DrawSessionInfo(nm);

            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);

            GUILayout.EndArea();
        }

        private void DrawMenu(NetworkManager nm)
        {
            GUILayout.Label("COURT — DEV BUILD");
            GUILayout.Label(BuildStamp.Line, GUILayout.Height(16));
            GUILayout.Space(4f);
            GUILayout.Label($"STATE: {_state}");
            GUILayout.Space(4f);

            if (GUILayout.Button("HOST GAME", GUILayout.Height(32)))
            {
                ConfigureTransport(asHost: true);
                _state = "HOSTING";
                if (!nm.StartHost())
                {
                    _state = "FAILED TO HOST";
                    _status = $"could not bind UDP {Port} — is another copy already hosting?";
                }
                else
                {
                    _status = $"listening on 0.0.0.0:{Port} — friends dial {LocalLanAddress()}:{Port}";
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label("SERVER ADDRESS");
            Address = GUILayout.TextField(Address);

            GUILayout.Label("PORT");
            string typed = GUILayout.TextField(Port.ToString());
            if (ushort.TryParse(typed, out ushort parsed) && parsed != 0) Port = parsed;

            if (GUILayout.Button("JOIN GAME", GUILayout.Height(32)))
            {
                Remember();
                ConfigureTransport(asHost: false);
                _state = "CONNECTING...";
                _connectingSince = Time.realtimeSinceStartup;

                if (!nm.StartClient())
                {
                    _state = "FAILED TO CONNECT";
                    _status = "transport refused to start — check the address and port";
                }
                else
                {
                    _status = $"dialling {Address}:{Port}";
                }
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("PLAY OFFLINE", GUILayout.Height(24))) StartOffline();
            if (GUILayout.Button("QUIT", GUILayout.Height(24))) Quit();
        }

        /// <summary>Connection state, in the player's words rather than NGO's.</summary>
        private string _state = "OFFLINE";
        private float _connectingSince = -1f;

        /// <summary>
        /// A dial that never completes looks identical to one still in progress, so
        /// give up out loud rather than leaving CONNECTING... on screen forever.
        /// </summary>
        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (nm.IsHost || nm.IsServer) { _state = "HOSTING"; _connectingSince = -1f; return; }

            if (nm.IsConnectedClient) { _state = "CONNECTED"; _connectingSince = -1f; return; }

            if (_connectingSince > 0f && Time.realtimeSinceStartup - _connectingSince > ConnectTimeoutSeconds)
            {
                _connectingSince = -1f;
                _state = "FAILED TO CONNECT";
                _status = $"no reply from {Address}:{Port} — host not running, wrong IP, " +
                          "firewall blocking, or port not forwarded";
                nm.Shutdown();
            }
        }

        [Tooltip("Seconds to wait for a host to answer before admitting it failed.")]
        public float ConnectTimeoutSeconds = 10f;

        private const string AddressKey = "court.dev.address";
        private const string PortKey = "court.dev.port";

        /// <summary>Typing an IP once per launch is a tax on every test session.</summary>
        private void Remember()
        {
            PlayerPrefs.SetString(AddressKey, Address);
            PlayerPrefs.SetInt(PortKey, Port);
            PlayerPrefs.Save();
        }

        private void Recall()
        {
            if (PlayerPrefs.HasKey(AddressKey)) Address = PlayerPrefs.GetString(AddressKey);
            if (PlayerPrefs.HasKey(PortKey)) Port = (ushort)PlayerPrefs.GetInt(PortKey);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawSessionInfo(NetworkManager nm)
        {
            string role = nm.IsHost ? "HOST" : nm.IsServer ? "SERVER" : "CLIENT";
            GUILayout.Label($"role: {role}");
            GUILayout.Label($"my id: {nm.LocalClientId}");

            if (nm.IsServer) GUILayout.Label($"players: {nm.ConnectedClientsIds.Count}");

            if (GUILayout.Button("DISCONNECT", GUILayout.Height(28)))
            {
                nm.Shutdown();
                _status = "left the session";
            }
        }

        /// <summary>
        /// Solo mode: instantiate the prefab and strip its NetworkObject, so the
        /// movement playground stays testable without starting a session.
        /// </summary>
        private void StartOffline()
        {
            if (OfflinePlayerPrefab == null)
            {
                Debug.LogError("[Net] OfflinePlayerPrefab is not assigned.");
                return;
            }

            var player = Instantiate(OfflinePlayerPrefab, new Vector3(0f, 0.3f, -2f), Quaternion.identity);
            player.name = "Player (offline)";

            // These only mean anything inside a session.
            var netPlayer = player.GetComponent<PrototypeNetPlayer>();
            if (netPlayer != null) Destroy(netPlayer);
            var sync = player.GetComponent<PlayerNetworkSync>();
            if (sync != null) Destroy(sync);
            var transform2 = player.GetComponent<ClientNetworkTransform>();
            if (transform2 != null) Destroy(transform2);
            var netObj = player.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);

            var rig = FindAnyObjectByType<PlayerCameraRig>();
            if (rig != null)
            {
                rig.Target = player.transform;
                rig.Input = player.GetComponent<PlayerInputReader>();
                var movement = player.GetComponent<PlayerMovement>();
                if (movement != null) movement.CameraTransform = rig.transform;
            }

            var hud = FindAnyObjectByType<PlayerDebugHud>();
            if (hud != null)
            {
                hud.Movement = player.GetComponent<PlayerMovement>();
                hud.AnimatorDriver = player.GetComponent<PlayerAnimatorDriver>();
            }

            _offline = true;
        }
    }
}
