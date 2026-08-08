using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game;
using CaseClosed.Game.Greybox;
using CaseClosed.Game.Prototype;
using CaseClosed.Game.Prototype.Net;
using CaseClosed.Game.Prototype.Voice;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Wires Brawly0's gameplay stack into the Revit courthouse scene, mirroring
    /// CourthouseBuilder.BuildSystems/BuildCaseSystems/BuildSpawns EXACTLY - same
    /// components, same settings - but placed against Omar's map geometry.
    /// After this runs, the two projects play identically; only the map differs.
    /// Menu: Case Closed > Wire Friend Gameplay Into RVT.
    /// </summary>
    public static class WireFriendGameplay
    {
        [MenuItem("Case Closed/Wire Friend Gameplay Into RVT")]
        public static void Run()
        {
            // ---- retire OUR parallel stack (scene objects only; scripts stay) ----
            foreach (var name in new[]
                     { "GameSystems", "CaseNetSync", "NetworkManager", "SpawnPoint",
                       "FriendGameplay", "PlayerSpawns", "PlayerCamera", "DebugHUD",
                       "ActiveCaseManager", "CaseNetworkController", "MatchFlowController",
                       "InteractionNetworkController", "ArchiveDirector",
                       "EvidenceCustodyDirector", "EvidenceBodies", "ArchiveContainers",
                       "RoomVolumes", "RegistrationArea" })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }

            // ---- anchor geometry we place things against ----
            var hallKit = GameObject.Find("MapDressing/Kit_MainHall");
            var lockerKit = GameObject.Find("MapDressing/Kit_EvidenceLocker");
            Vector3 hall = hallKit != null ? hallKit.transform.position : new Vector3(12f, 3.7f, 0f);
            Physics.SyncTransforms();

            BuildSpawns(hall);
            BuildSystems();
            BuildArchiveInLocker(lockerKit != null ? lockerKit.transform : null, hall);
            BuildRoomVolumes();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Wire] friend gameplay wired into CourthouseRVT");
        }

        // ------------------------------------------------- spawns (their arc, our hall)
        private static void BuildSpawns(Vector3 hall)
        {
            var root = new GameObject("PlayerSpawns").transform;
            var offsets = new[]
            {
                new Vector3(-4.5f, 0f, -3f), new Vector3(-1.5f, 0f, -3f),
                new Vector3( 1.5f, 0f, -3f), new Vector3( 4.5f, 0f, -3f),
                new Vector3(-3f,   0f, -0.5f), new Vector3( 3f,  0f, -0.5f),
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                var p = hall + offsets[i];
                // must land on OUR floor - pull toward the hall centre until it does
                for (int step = 0; step < 3; step++)
                {
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f)
                        && hit.normal.y > 0.6f) { p.y = hit.point.y + 0.3f; break; }
                    p = Vector3.Lerp(p, hall, 0.5f);
                }
                var marker = new GameObject($"PlayerSpawn_{i}");
                marker.transform.SetParent(root, false);
                marker.transform.position = p;
            }
        }

        // ------------------------------------------------- their systems, verbatim
        private static void BuildSystems()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/Prefabs/PlayerPrototype.prefab");
            if (prefab == null)
                Debug.LogError("[Wire] PlayerPrototype prefab missing - run Case Closed > Prototype > Build Everything first.");

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = prefab;
            nm.NetworkConfig.ConnectionApproval = false;
            transport.SetConnectionData("127.0.0.1", 7777);

            var hud = nmGo.AddComponent<PrototypeNetworkHud>();
            hud.Address = "127.0.0.1";
            hud.Port = 7777;
            hud.OfflinePlayerPrefab = prefab;
            nmGo.AddComponent<PrototypeMpAutoTest>();

            var cameraGo = new GameObject("PlayerCamera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<PlayerCameraRig>();
            cameraGo.transform.position = new Vector3(0f, 8f, -18f);
            cameraGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);

            var hudGo = new GameObject("DebugHUD");
            hudGo.AddComponent<PlayerDebugHud>();
            hudGo.AddComponent<VoiceHud>();
            hudGo.AddComponent<GreyboxDebugHud>();
            hudGo.AddComponent<CaseClosed.Game.Cases.CaseDebugPanel>();
            hudGo.AddComponent<CaseClosed.Game.Cases.Roles.PlayerRoleHud>();
            hudGo.AddComponent<CaseClosed.Game.Match.BriefingScreen>();
            hudGo.AddComponent<CaseClosed.Game.Interaction.InteractionPromptUI>();
            hudGo.AddComponent<CaseClosed.Game.Archive.EvidenceDiscoveryUI>();
            hudGo.AddComponent<CaseClosed.Game.Archive.EvidenceCarryHud>();

            var vault = new GameObject("ActiveCaseManager");
            vault.AddComponent<CaseClosed.Game.Cases.ActiveCaseManager>();

            var netGo = new GameObject("CaseNetworkController");
            netGo.AddComponent<NetworkObject>();
            netGo.AddComponent<CaseClosed.Game.Cases.CaseNetworkController>();
            netGo.AddComponent<CaseClosed.Game.Cases.Roles.PlayerRoster>();

            var matchGo = new GameObject("MatchFlowController");
            matchGo.AddComponent<NetworkObject>();
            var flow = matchGo.AddComponent<CaseClosed.Game.Match.MatchFlowController>();
            flow.RequiredPlayers = 4;
            flow.AllowPartialTable = true;

            var interactionGo = new GameObject("InteractionNetworkController");
            interactionGo.AddComponent<NetworkObject>();
            interactionGo.AddComponent<CaseClosed.Game.Interaction.InteractionNetworkController>();

            var archiveGo = new GameObject("ArchiveDirector");
            archiveGo.AddComponent<NetworkObject>();
            archiveGo.AddComponent<CaseClosed.Game.Archive.ArchiveDirector>();

            var custodyGo = new GameObject("EvidenceCustodyDirector");
            custodyGo.AddComponent<NetworkObject>();
            custodyGo.AddComponent<CaseClosed.Game.Archive.EvidenceCustodyDirector>();

            BuildEvidenceBodyPool();
        }

        private static Material Mat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{name}.mat");
            return m != null ? m : AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Plaster.mat");
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, string mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mat(mat);
            return go;
        }

        private static void BuildEvidenceBodyPool()
        {
            var root = new GameObject("EvidenceBodies").transform;
            for (int i = 0; i < 6; i++)
            {
                var body = Box(root, $"EvidenceBody_{i:00}",
                    new Vector3(0f, -50f, 0f), new Vector3(0.32f, 0.05f, 0.42f), "Manila");
                GameObjectUtility.SetStaticEditorFlags(body, 0);
                body.isStatic = false;
                body.AddComponent<NetworkObject>();
                var evidence = body.AddComponent<CaseClosed.Game.Archive.PhysicalEvidence>();
                evidence.Prompt = "Pick Up Evidence";
                evidence.HoldDuration = 0f;
                evidence.MaxDistance = 2.5f;
                evidence.RequiresLineOfSight = true;
            }
        }

        // ------------------------------------------------- archive in OUR locker room
        private static void BuildArchiveInLocker(Transform locker, Vector3 hallFallback)
        {
            Vector3 basePos = locker != null ? locker.position : hallFallback + new Vector3(8f, 0f, 0f);
            Quaternion rot = locker != null ? locker.rotation : Quaternion.identity;
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;

            var root = new GameObject("ArchiveContainers").transform;
            var kinds = new (string Kind, float Seconds, Vector3 Size, string Mat)[]
            {
                ("Desk Drawer",    1.5f, new Vector3(1.0f, 0.8f, 0.7f),  "Wood"),
                ("Records Box",    2.5f, new Vector3(0.9f, 0.7f, 0.9f),  "Metal"),
                ("Filing Cabinet", 3.0f, new Vector3(0.9f, 1.6f, 0.7f),  "Metal"),
                ("Shelf Section",  4.0f, new Vector3(1.4f, 2.2f, 0.6f),  "WoodDark"),
            };

            // two rows of five flanking the locker kit, facing inward - same
            // container indices/kinds/timings as the greybox Archive
            for (int i = 0; i < 10; i++)
            {
                bool near = i < 5;
                int column = i % 5;
                var kind = kinds[i % kinds.Length];

                Vector3 p = basePos + right * (-3.4f + column * 1.7f) + fwd * (near ? 2.6f : -2.6f);
                if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f))
                    p.y = hit.point.y;
                p.y += kind.Size.y * 0.5f;

                var go = Box(root, $"Container_{i:00}_{kind.Kind.Replace(" ", "")}", p, kind.Size, kind.Mat);
                go.AddComponent<NetworkObject>();
                var container = go.AddComponent<CaseClosed.Game.Archive.ArchiveContainer>();
                container.ContainerIndex = i;
                container.ContainerKind = kind.Kind;
                container.Prompt = $"Search {kind.Kind}";
                container.HoldDuration = kind.Seconds;
                container.MaxDistance = 3f;
                container.RequiresLineOfSight = true;
                container.RevealDirection = near ? -fwd : fwd;   // into the aisle
            }

            // registration desk + terminal (their EvidenceLockerBuilder, our room)
            var area = new GameObject("RegistrationArea").transform;
            Vector3 deskPos = basePos + fwd * 0.0f + right * 4.6f;
            if (Physics.Raycast(deskPos + Vector3.up * 1.5f, Vector3.down, out var dh, 3f)) deskPos.y = dh.point.y;
            Box(area, "RegistrationDesk", deskPos + Vector3.up * 0.5f, new Vector3(2.4f, 1.0f, 0.9f), "Wood");

            var terminal = Box(area, "RegistrationTerminal",
                deskPos + Vector3.up * 1.32f - fwd * 0.1f, new Vector3(0.62f, 0.5f, 0.1f), "Screen");
            GameObjectUtility.SetStaticEditorFlags(terminal, 0);
            terminal.isStatic = false;
            terminal.AddComponent<NetworkObject>();
            var registration = terminal.AddComponent<CaseClosed.Game.Archive.RegistrationTerminal>();
            registration.Prompt = "Register Evidence";
            registration.HoldDuration = 2.5f;
            registration.MaxDistance = 2.5f;
            registration.RequiresLineOfSight = true;
        }

        // ------------------------------------------------- room volumes from our anchors
        private static void BuildRoomVolumes()
        {
            var root = new GameObject("RoomVolumes").transform;
            foreach (var a in Object.FindObjectsByType<ZoneAnchor>(FindObjectsSortMode.None))
            {
                Vector3 p = a.transform.position;
                float xp = Dist(p, Vector3.right), xn = Dist(p, Vector3.left);
                float zp = Dist(p, Vector3.forward), zn = Dist(p, Vector3.back);

                var go = new GameObject("Room_" + a.ZoneName);
                go.transform.SetParent(root, false);
                go.transform.position = p + new Vector3((xp - xn) * 0.5f, 1.7f, (zp - zn) * 0.5f);
                var vol = go.AddComponent<RoomVolume>();
                vol.RoomName = a.ZoneName;
                vol.Size = new Vector3(xp + xn, 3.4f, zp + zn);
            }
            // the stair core is a room too - transitional, so the travel timer skips it
            var stair = GameObject.Find("OmarBuilding");
            if (stair != null)
            {
                Bounds? sb = null;
                foreach (var r in stair.GetComponentsInChildren<Renderer>())
                    if (r.gameObject.name.Contains("Stair") || r.gameObject.name.Contains("Landing"))
                        sb = sb == null ? r.bounds : Enc(sb.Value, r.bounds);
                if (sb.HasValue)
                {
                    var go = new GameObject("Room_Stairwell");
                    go.transform.SetParent(root, false);
                    go.transform.position = sb.Value.center;
                    var vol = go.AddComponent<RoomVolume>();
                    vol.RoomName = "Stairwell";
                    vol.Size = sb.Value.size + new Vector3(2f, 2f, 2f);
                    vol.IsTransitional = true;
                }
            }
        }

        private static float Dist(Vector3 p, Vector3 dir)
            => Physics.Raycast(p + Vector3.up * 1.4f, dir, out var hit, 9f) ? hit.distance : 9f;

        private static Bounds Enc(Bounds a, Bounds b) { a.Encapsulate(b); return a; }
    }
}
