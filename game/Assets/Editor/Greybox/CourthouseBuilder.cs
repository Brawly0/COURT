using CaseClosed.Game.Greybox;
using CaseClosed.Game.Prototype;
using CaseClosed.Game.Prototype.Net;
using CaseClosed.Game.Prototype.Voice;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaseClosed.EditorTools.Greybox
{
    /// <summary>
    /// COURT greybox v1 — Archive, Witness Lounge and Courtroom around a central Atrium.
    ///
    /// THE PLAN IS A HUB AND THREE SPOKES, and the distances are the design:
    ///
    ///                        COURTROOM
    ///                            |            (40 m from atrium centre)
    ///          ARCHIVE ------ ATRIUM ------ WITNESS LOUNGE
    ///        (40 m west)                      (40 m east)
    ///
    /// Every room is 40 m from the middle, which at the controller's default run speed
    /// of 4.3 m/s is ~9.3 s — inside the 8-12 s target. Any two rooms are 80 m apart
    /// via the atrium (~18.6 s), under the 20 s ceiling.
    ///
    /// Everything routes through the atrium ON PURPOSE. There are no shortcuts between
    /// wings, so players crossing the building must pass through the one space where
    /// everyone else is, which is where chance encounters and overheard conversations
    /// happen. A ring corridor would be kinder to navigate and would kill the game.
    ///
    /// Menu: Case Closed > Greybox > Build Courthouse
    /// </summary>
    public static class CourthouseBuilder
    {
        public const string ScenePath = "Assets/Scenes/Courthouse.unity";

        // ---- the plan, in metres ----
        private const float AtriumHalfX = 14f;
        private const float AtriumHalfZ = 10f;
        private const float AtriumHeight = 11f;

        private const float RoomDistance = 40f;   // atrium centre -> room centre
        private const float WingHalfX = 11f;      // archive / lounge half-width
        private const float WingHalfZ = 10f;
        private const float RoomHeight = 5.5f;

        private const float CourtHalfX = 13f;
        private const float CourtHalfZ = 11f;
        private const float CourtHeight = 7.5f;

        private const float CorridorHalf = 3f;
        private const float CorridorHeight = 4.5f;

        [MenuItem("Case Closed/Greybox/Build Courthouse", priority = 0)]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
            EditorUtility.DisplayDialog("Courthouse greybox built",
                "Press Play, then HOST.\n\nF1 movement · F3 greybox debug · V push-to-talk", "OK");
        }

        public static void Build()
        {
            PrototypeAssetsBridge.EnsureFolder("Assets/Greybox/Materials");
            PrototypeAssetsBridge.EnsureFolder("Assets/Scenes");
            GreyboxKit.BuildMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildAtrium();
            BuildCorridors();
            BuildArchive();
            BuildWitnessLounge();
            BuildCourtroom();
            BuildVolumes();
            BuildSpawns();
            BuildSystems();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Greybox] Courthouse saved to {ScenePath}");
        }

        // ------------------------------------------------------------------

        private static void BuildLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1f, 0.97f, 0.92f);
            go.transform.rotation = Quaternion.Euler(52f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.53f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.40f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.20f);
        }

        /// <summary>
        /// The hub. Tall and open so you can see across it, with pillars that break
        /// sightlines just enough that people are not permanently visible to each other.
        /// </summary>
        private static void BuildAtrium()
        {
            var root = new GameObject("Atrium").transform;

            GreyboxKit.Slab("Floor", root, -AtriumHalfX, -AtriumHalfZ, AtriumHalfX, AtriumHalfZ, 0f, GreyboxKit.Floor);

            // Doorways face the three wings; the south wall is the public entrance.
            GreyboxKit.WallAlongZ("Wall_West", root, -AtriumHalfZ, AtriumHalfZ, -AtriumHalfX, AtriumHeight, GreyboxKit.Wall, 0f, 5f, 4f);
            GreyboxKit.WallAlongZ("Wall_East", root, -AtriumHalfZ, AtriumHalfZ, AtriumHalfX, AtriumHeight, GreyboxKit.Wall, 0f, 5f, 4f);
            GreyboxKit.WallAlongX("Wall_North", root, -AtriumHalfX, AtriumHalfX, AtriumHalfZ, AtriumHeight, GreyboxKit.Wall, 0f, 6f, 4.5f);
            GreyboxKit.WallAlongX("Wall_South", root, -AtriumHalfX, AtriumHalfX, -AtriumHalfZ, AtriumHeight, GreyboxKit.Wall, 0f, 6f, 4f);

            foreach (var (x, z) in new[] { (-7f, -5f), (7f, -5f), (-7f, 5f), (7f, 5f) })
                GreyboxKit.Box($"Pillar_{x}_{z}", root, new Vector3(x, AtriumHeight * 0.5f, z),
                    new Vector3(1.4f, AtriumHeight, 1.4f), GreyboxKit.Accent);

            // Reception desk: a landmark so "meet at the desk" means something.
            GreyboxKit.Box("Desk", root, new Vector3(0f, 0.55f, -6f), new Vector3(7f, 1.1f, 1.6f), GreyboxKit.Wood);

            // Signs over each doorway, readable from the middle of the room.
            GreyboxKit.SignText("Sign_Archive", root, new Vector3(-AtriumHalfX + 0.4f, 5.2f, 0f),
                Quaternion.Euler(0f, -90f, 0f), "ARCHIVE", 1.1f);
            GreyboxKit.SignText("Sign_Lounge", root, new Vector3(AtriumHalfX - 0.4f, 5.2f, 0f),
                Quaternion.Euler(0f, 90f, 0f), "WITNESS LOUNGE", 1.1f);
            GreyboxKit.SignText("Sign_Courtroom", root, new Vector3(0f, 5.6f, AtriumHalfZ - 0.4f),
                Quaternion.identity, "COURTROOM", 1.2f);
            GreyboxKit.SignText("Sign_Atrium", root, new Vector3(0f, 3.2f, -AtriumHalfZ + 0.6f),
                Quaternion.Euler(0f, 180f, 0f), "ATRIUM", 0.9f);
        }

        /// <summary>
        /// Deliberately long and narrow. A corridor is dead space visually, but it is
        /// what makes the wings feel separate, gives proximity voice somewhere to fall
        /// off, and forces a committed decision when you set off somewhere.
        /// </summary>
        private static void BuildCorridors()
        {
            var root = new GameObject("Corridors").transform;

            // West -> Archive
            GreyboxKit.Slab("Floor_W", root, -RoomDistance + WingHalfX, -CorridorHalf, -AtriumHalfX, CorridorHalf, 0f, GreyboxKit.Floor);
            GreyboxKit.WallAlongX("W_North", root, -RoomDistance + WingHalfX, -AtriumHalfX, CorridorHalf, CorridorHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongX("W_South", root, -RoomDistance + WingHalfX, -AtriumHalfX, -CorridorHalf, CorridorHeight, GreyboxKit.Wall);

            // East -> Witness Lounge
            GreyboxKit.Slab("Floor_E", root, AtriumHalfX, -CorridorHalf, RoomDistance - WingHalfX, CorridorHalf, 0f, GreyboxKit.Floor);
            GreyboxKit.WallAlongX("E_North", root, AtriumHalfX, RoomDistance - WingHalfX, CorridorHalf, CorridorHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongX("E_South", root, AtriumHalfX, RoomDistance - WingHalfX, -CorridorHalf, CorridorHeight, GreyboxKit.Wall);

            // North -> Courtroom
            GreyboxKit.Slab("Floor_N", root, -CorridorHalf, AtriumHalfZ, CorridorHalf, RoomDistance - CourtHalfZ, 0f, GreyboxKit.Floor);
            GreyboxKit.WallAlongZ("N_West", root, AtriumHalfZ, RoomDistance - CourtHalfZ, -CorridorHalf, CorridorHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongZ("N_East", root, AtriumHalfZ, RoomDistance - CourtHalfZ, CorridorHalf, CorridorHeight, GreyboxKit.Wall);

            // A bench partway down each hall: somewhere to loiter, and a landmark that
            // tells you how far along you are.
            GreyboxKit.Box("Bench_W", root, new Vector3(-26f, 0.45f, 2.2f), new Vector3(3f, 0.9f, 0.8f), GreyboxKit.Bench);
            GreyboxKit.Box("Bench_E", root, new Vector3(26f, 0.45f, -2.2f), new Vector3(3f, 0.9f, 0.8f), GreyboxKit.Bench);
            GreyboxKit.Box("Bench_N", root, new Vector3(2.2f, 0.45f, 20f), new Vector3(0.8f, 0.9f, 3f), GreyboxKit.Bench);
        }

        /// <summary>
        /// Shelf rows are the point of this room. They chop it into aisles, so two
        /// players searching can be metres apart and neither see nor clearly hear each
        /// other — which is what makes searching together feel different from alone.
        /// </summary>
        private static void BuildArchive()
        {
            var root = new GameObject("Archive").transform;
            float cx = -RoomDistance;
            float x0 = cx - WingHalfX, x1 = cx + WingHalfX;
            float z0 = -WingHalfZ, z1 = WingHalfZ;

            GreyboxKit.Slab("Floor", root, x0, z0, x1, z1, 0f, GreyboxKit.Floor);
            GreyboxKit.WallAlongZ("Wall_West", root, z0, z1, x0, RoomHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongZ("Wall_East", root, z0, z1, x1, RoomHeight, GreyboxKit.Wall, 0f);   // door to corridor
            GreyboxKit.WallAlongX("Wall_North", root, x0, x1, z1, RoomHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongX("Wall_South", root, x0, x1, z0, RoomHeight, GreyboxKit.Wall);

            // Four aisles of shelving, split so you can cut through the middle.
            for (int row = 0; row < 4; row++)
            {
                float x = cx - 7.5f + row * 4.2f;
                GreyboxKit.Box($"Shelf_{row}_A", root, new Vector3(x, 1.3f, -5.2f), new Vector3(1f, 2.6f, 7f), GreyboxKit.Wood);
                GreyboxKit.Box($"Shelf_{row}_B", root, new Vector3(x, 1.3f, 5.2f), new Vector3(1f, 2.6f, 7f), GreyboxKit.Wood);
            }

            // Reading tables by the door, and crates as stand-in search spots.
            GreyboxKit.Box("Table_A", root, new Vector3(cx + 7.5f, 0.45f, -4f), new Vector3(3f, 0.9f, 1.6f), GreyboxKit.Wood);
            GreyboxKit.Box("Table_B", root, new Vector3(cx + 7.5f, 0.45f, 0f), new Vector3(3f, 0.9f, 1.6f), GreyboxKit.Wood);
            GreyboxKit.Box("Table_C", root, new Vector3(cx + 7.5f, 0.45f, 4f), new Vector3(3f, 0.9f, 1.6f), GreyboxKit.Wood);

            for (int i = 0; i < 5; i++)
                GreyboxKit.Box($"SearchSpot_{i}", root,
                    new Vector3(cx - 9.2f + i * 4.4f, 0.35f, 8.6f),
                    new Vector3(1.1f, 0.7f, 1.1f), GreyboxKit.Metal);

            GreyboxKit.SignText("Sign", root, new Vector3(x1 - 0.4f, 4.2f, 0f), Quaternion.Euler(0f, 90f, 0f), "ARCHIVE", 1f);
        }

        /// <summary>
        /// Split in two: an open waiting area, and an interview nook behind a partition.
        /// The partition is the interesting bit — it is the first place in the map where
        /// you can hold a conversation that people nearby cannot quite hear.
        /// </summary>
        private static void BuildWitnessLounge()
        {
            var root = new GameObject("WitnessLounge").transform;
            float cx = RoomDistance;
            float x0 = cx - WingHalfX, x1 = cx + WingHalfX;
            float z0 = -WingHalfZ, z1 = WingHalfZ;

            GreyboxKit.Slab("Floor", root, x0, z0, x1, z1, 0f, GreyboxKit.Floor);
            GreyboxKit.WallAlongZ("Wall_West", root, z0, z1, x0, RoomHeight, GreyboxKit.Wall, 0f);   // door to corridor
            GreyboxKit.WallAlongZ("Wall_East", root, z0, z1, x1, RoomHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongX("Wall_North", root, x0, x1, z1, RoomHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongX("Wall_South", root, x0, x1, z0, RoomHeight, GreyboxKit.Wall);

            // Waiting area, south half.
            for (int i = 0; i < 3; i++)
                GreyboxKit.Box($"Bench_{i}", root, new Vector3(cx - 6f + i * 5f, 0.45f, -7.5f),
                    new Vector3(4f, 0.9f, 1.2f), GreyboxKit.Bench);

            GreyboxKit.Box("Sofa_A", root, new Vector3(cx - 6.5f, 0.5f, -2.5f), new Vector3(1.4f, 1f, 4f), GreyboxKit.Seat);
            GreyboxKit.Box("Sofa_B", root, new Vector3(cx - 1.5f, 0.5f, -2.5f), new Vector3(1.4f, 1f, 4f), GreyboxKit.Seat);
            GreyboxKit.Box("CoffeeTable", root, new Vector3(cx - 4f, 0.3f, -2.5f), new Vector3(2f, 0.6f, 1.6f), GreyboxKit.Wood);

            // Interview nook, north-east corner, screened but not sealed.
            GreyboxKit.WallAlongX("Partition", root, cx - 1f, x1, 3f, 3.2f, GreyboxKit.Accent);
            GreyboxKit.WallAlongZ("Partition_Side", root, 3f, z1, cx - 1f, 3.2f, GreyboxKit.Accent, 7f, 3f, 2.6f);

            GreyboxKit.Box("InterviewTable", root, new Vector3(cx + 5f, 0.45f, 6.5f), new Vector3(2.4f, 0.9f, 1.4f), GreyboxKit.Wood);
            GreyboxKit.Box("Chair_Interviewer", root, new Vector3(cx + 5f, 0.5f, 5f), new Vector3(0.9f, 1f, 0.9f), GreyboxKit.Seat);
            GreyboxKit.Box("Chair_Witness", root, new Vector3(cx + 5f, 0.5f, 8f), new Vector3(0.9f, 1f, 0.9f), GreyboxKit.Seat);

            GreyboxKit.SignText("Sign", root, new Vector3(x0 + 0.4f, 4.2f, 0f), Quaternion.Euler(0f, -90f, 0f), "WITNESS LOUNGE", 0.85f);
            GreyboxKit.SignText("Sign_Interview", root, new Vector3(cx - 1f, 2.6f, 3f), Quaternion.Euler(0f, 180f, 0f), "INTERVIEW", 0.55f);
        }

        /// <summary>
        /// Positions only, no mechanics. Everything is placed so that the trial has an
        /// obvious geography: judge raised and facing the room, counsel tables opposed
        /// across an aisle, witness boxed in beside the judge, gallery behind the bar.
        /// </summary>
        private static void BuildCourtroom()
        {
            var root = new GameObject("Courtroom").transform;
            float cz = RoomDistance;
            float x0 = -CourtHalfX, x1 = CourtHalfX;
            float z0 = cz - CourtHalfZ, z1 = cz + CourtHalfZ;

            GreyboxKit.Slab("Floor", root, x0, z0, x1, z1, 0f, GreyboxKit.Floor);
            GreyboxKit.WallAlongX("Wall_South", root, x0, x1, z0, CourtHeight, GreyboxKit.Wall, 0f, 5f, 4f);  // door
            GreyboxKit.WallAlongX("Wall_North", root, x0, x1, z1, CourtHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongZ("Wall_West", root, z0, z1, x0, CourtHeight, GreyboxKit.Wall);
            GreyboxKit.WallAlongZ("Wall_East", root, z0, z1, x1, CourtHeight, GreyboxKit.Wall);

            // Judge: raised, back to the north wall, facing the room.
            GreyboxKit.Box("JudgeDais", root, new Vector3(0f, 0.4f, cz + 8f), new Vector3(11f, 0.8f, 4f), GreyboxKit.Accent);
            GreyboxKit.Box("JudgeBench", root, new Vector3(0f, 1.4f, cz + 8.6f), new Vector3(8f, 1.2f, 1.2f), GreyboxKit.Wood);
            GreyboxKit.SignText("Sign_Judge", root, new Vector3(0f, 3.6f, z1 - 0.4f), Quaternion.Euler(0f, 180f, 0f), "JUDGE", 0.6f);

            // Witness stand, beside the dais.
            GreyboxKit.Box("WitnessStand", root, new Vector3(7f, 0.3f, cz + 4.5f), new Vector3(3f, 0.6f, 3f), GreyboxKit.Accent);
            GreyboxKit.Box("WitnessRail", root, new Vector3(7f, 1.1f, cz + 3.2f), new Vector3(3f, 1f, 0.3f), GreyboxKit.Wood);
            GreyboxKit.SignText("Sign_Witness", root, new Vector3(7f, 2.4f, cz + 6.1f), Quaternion.Euler(0f, 180f, 0f), "WITNESS", 0.45f);

            // Counsel tables, opposed across the aisle.
            GreyboxKit.Box("ProsecutionTable", root, new Vector3(-6f, 0.45f, cz + 0.5f), new Vector3(6f, 0.9f, 1.8f), GreyboxKit.Wood);
            GreyboxKit.SignText("Sign_Prosecution", root, new Vector3(-6f, 2.2f, cz + 2.2f), Quaternion.Euler(0f, 180f, 0f), "PROSECUTION", 0.45f);

            GreyboxKit.Box("DefenseTable", root, new Vector3(6f, 0.45f, cz - 2.5f), new Vector3(6f, 0.9f, 1.8f), GreyboxKit.Wood);
            GreyboxKit.SignText("Sign_Defense", root, new Vector3(6f, 2.2f, cz - 0.8f), Quaternion.Euler(0f, 180f, 0f), "DEFENSE", 0.45f);

            // The bar: separates the well from the public, with a gap to walk through.
            GreyboxKit.WallAlongX("Bar", root, x0, x1, cz - 5f, 1.1f, GreyboxKit.Wood, 0f, 3f, 1.1f);

            // Gallery / jury benches.
            for (int row = 0; row < 4; row++)
                GreyboxKit.Box($"Gallery_{row}", root, new Vector3(0f, 0.45f, cz - 7f - row * 1.8f),
                    new Vector3(18f, 0.9f, 1f), GreyboxKit.Bench);

            GreyboxKit.SignText("Sign_Gallery", root, new Vector3(-11f, 2.6f, cz - 8f), Quaternion.Euler(0f, 90f, 0f), "JURY / GALLERY", 0.45f);
            GreyboxKit.SignText("Sign", root, new Vector3(0f, 4.6f, z0 + 0.4f), Quaternion.identity, "COURTROOM", 1f);
        }

        private static void BuildVolumes()
        {
            var root = new GameObject("RoomVolumes").transform;

            GreyboxKit.Volume("Atrium", root, -AtriumHalfX, -AtriumHalfZ, AtriumHalfX, AtriumHalfZ, AtriumHeight);
            GreyboxKit.Volume("Archive", root, -RoomDistance - WingHalfX, -WingHalfZ, -RoomDistance + WingHalfX, WingHalfZ, RoomHeight);
            GreyboxKit.Volume("Witness Lounge", root, RoomDistance - WingHalfX, -WingHalfZ, RoomDistance + WingHalfX, WingHalfZ, RoomHeight);
            GreyboxKit.Volume("Courtroom", root, -CourtHalfX, RoomDistance - CourtHalfZ, CourtHalfX, RoomDistance + CourtHalfZ, CourtHeight);

            GreyboxKit.Volume("West Hall", root, -RoomDistance + WingHalfX, -CorridorHalf, -AtriumHalfX, CorridorHalf, CorridorHeight, true);
            GreyboxKit.Volume("East Hall", root, AtriumHalfX, -CorridorHalf, RoomDistance - WingHalfX, CorridorHalf, CorridorHeight, true);
            GreyboxKit.Volume("North Hall", root, -CorridorHalf, AtriumHalfZ, CorridorHalf, RoomDistance - CourtHalfZ, CorridorHeight, true);
        }

        /// <summary>
        /// Six spawns in an arc south of the reception desk: central, facing the
        /// courtroom, spread far enough apart that nobody spawns inside anybody.
        /// </summary>
        private static void BuildSpawns()
        {
            var root = new GameObject("PlayerSpawns").transform;

            var points = new[]
            {
                new Vector3(-4.5f, 0.3f, -3f), new Vector3(-1.5f, 0.3f, -3f),
                new Vector3( 1.5f, 0.3f, -3f), new Vector3( 4.5f, 0.3f, -3f),
                new Vector3(-3f,   0.3f, -0.5f), new Vector3( 3f,  0.3f, -0.5f),
            };

            for (int i = 0; i < points.Length; i++)
            {
                var marker = new GameObject($"PlayerSpawn_{i}");
                marker.transform.SetParent(root, false);
                marker.transform.position = points[i];
            }
        }

        /// <summary>Networking, camera and HUDs — identical wiring to the movement playground.</summary>
        private static void BuildSystems()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/Prefabs/PlayerPrototype.prefab");
            if (prefab == null)
                Debug.LogError("[Greybox] PlayerPrototype prefab missing - run Case Closed > Prototype > Build Everything first.");

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
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
            cameraGo.transform.position = new Vector3(0f, 5f, -18f);
            cameraGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);

            var hudGo = new GameObject("DebugHUD");
            hudGo.AddComponent<PlayerDebugHud>();
            hudGo.AddComponent<VoiceHud>();
            hudGo.AddComponent<GreyboxDebugHud>();
        }
    }
}
