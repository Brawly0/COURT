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
                _status = "disconnected from host";
                Debug.Log("[Net] local client dropped - returning to menu");
                nm.Shutdown();
            }
            else
            {
                _status = $"client {clientId} left";
                Debug.Log($"[Net] {_status}");
            }
        }

        private void ConfigureTransport()
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null) transport.SetConnectionData(Address, Port);
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
            GUILayout.Label("MULTIPLAYER PROTOTYPE");

            if (GUILayout.Button("HOST (start a game)", GUILayout.Height(34)))
            {
                ConfigureTransport();
                nm.StartHost();
            }

            if (GUILayout.Button($"JOIN {Address}", GUILayout.Height(34)))
            {
                ConfigureTransport();
                nm.StartClient();
            }

            if (GUILayout.Button("PLAY OFFLINE", GUILayout.Height(28)))
                StartOffline();

            Address = GUILayout.TextField(Address);
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
