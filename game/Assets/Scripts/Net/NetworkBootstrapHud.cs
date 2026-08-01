using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Graybox session starter: HOST / JOIN / OFFLINE buttons until a session
    /// exists. Spawns the local player in offline mode (network modes let NGO
    /// spawn player prefabs). Lobby flow proper arrives with Steam relay.
    /// </summary>
    public class NetworkBootstrapHud : MonoBehaviour
    {
        public GameObject PlayerPrefab; // assigned by ProjectSetup

        private bool _started;

        private void OnGUI()
        {
            if (_started) return;
            var style = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            float w = 260f, h = 44f, x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - 90f;

            GUI.Label(new Rect(x, y - 40f, w, 30f), "CASE CLOSED — graybox spine",
                new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter });

            if (GUI.Button(new Rect(x, y, w, h), "HOST (this machine)", style))
            {
                NetworkManager.Singleton.StartHost();
                _started = true;
            }
            if (GUI.Button(new Rect(x, y + 54f, w, h), "JOIN 127.0.0.1", style))
            {
                NetworkManager.Singleton.StartClient();
                _started = true;
            }
            if (GUI.Button(new Rect(x, y + 108f, w, h), "PLAY OFFLINE", style))
            {
                var player = Instantiate(PlayerPrefab, new Vector3(6f, 0.1f, 0f),
                    Quaternion.Euler(0f, -90f, 0f));
                player.name = "Player (offline)";
                var netObj = player.GetComponent<NetworkObject>();
                if (netObj != null) Destroy(netObj);
                CaseRuntime.Instance.GenerateNow(CaseRuntime.Instance.Seed);
                _started = true;
            }
        }
    }
}
