using CaseClosed.Game.Prototype;
using CaseClosed.Game.Prototype.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Builds MovementPlayground.unity: a bare test box with one obstacle per
    /// thing worth testing. Nothing here is COURT content — it is a lab, and it is
    /// meant to be thrown away.
    ///
    /// The ramp angles bracket CharacterController.slopeLimit (47 degrees) on
    /// purpose: 15/25/35 are walkable, 45 is marginal, 60 must slide you back down.
    /// The stairs likewise bracket stepOffset (0.42m).
    ///
    /// Menu: Case Closed > Prototype > 3. Build Test Playground
    /// </summary>
    public static class PrototypePlaygroundBuilder
    {
        private static Material _floor, _ramp, _stair, _wall, _obstacle, _platform;

        [MenuItem("Case Closed/Prototype/3. Build Test Playground", priority = 102)]
        public static void BuildFromMenu()
        {
            Build();
            EditorUtility.DisplayDialog("Playground built",
                $"Saved to {PrototypeAssets.TestScenePath}\n\nPress Play to test.", "OK");
        }

        public static void Build()
        {
            PrototypeAssets.EnsureAllFolders();
            CacheMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildFloor();
            BuildRamps();
            BuildStairs();
            BuildWalls();
            BuildJumpCourse();
            BuildObstacles();

            // No player is placed in the scene any more. Netcode spawns one per
            // connected client from the prefab, so a pre-placed copy would be a
            // second, ownerless character.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeAssets.CharacterPrefabPath);
            if (prefab == null) Debug.LogError("[Prototype] Character prefab missing - run step 1 first.");

            BuildSpawnMarker();
            BuildNetwork(prefab);
            BuildCamera();
            BuildDebugHud();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, PrototypeAssets.TestScenePath);
            Debug.Log($"[Prototype] Playground saved to {PrototypeAssets.TestScenePath}");
        }

        private static void CacheMaterials()
        {
            _floor = PrototypeAssets.GetOrCreateMaterial("Proto_Floor", new Color(0.58f, 0.58f, 0.60f));
            _ramp = PrototypeAssets.GetOrCreateMaterial("Proto_Ramp", new Color(0.36f, 0.50f, 0.68f));
            _stair = PrototypeAssets.GetOrCreateMaterial("Proto_Stair", new Color(0.72f, 0.62f, 0.44f));
            _wall = PrototypeAssets.GetOrCreateMaterial("Proto_Wall", new Color(0.44f, 0.44f, 0.47f));
            _obstacle = PrototypeAssets.GetOrCreateMaterial("Proto_Obstacle", new Color(0.78f, 0.47f, 0.24f));
            _platform = PrototypeAssets.GetOrCreateMaterial("Proto_Platform", new Color(0.40f, 0.64f, 0.42f));
        }

        /// <summary>A solid box with a collider. The world's counterpart to PrototypeAssets.Box.</summary>
        private static GameObject Solid(string name, Transform parent, Vector3 position,
                                        Vector3 size, Material material, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return go;
        }

        private static void BuildLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1f, 0.97f, 0.91f);
            lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.56f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.23f, 0.22f);
        }

        private static void BuildFloor()
        {
            var root = new GameObject("Floor").transform;
            Solid("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(70f, 1f, 70f), _floor);

            // A low lip around the edge so you cannot walk off into the void.
            const float half = 35f;
            Solid("Edge_N", root, new Vector3(0f, 0.5f, half), new Vector3(70f, 2f, 1f), _wall);
            Solid("Edge_S", root, new Vector3(0f, 0.5f, -half), new Vector3(70f, 2f, 1f), _wall);
            Solid("Edge_E", root, new Vector3(half, 0.5f, 0f), new Vector3(1f, 2f, 70f), _wall);
            Solid("Edge_W", root, new Vector3(-half, 0.5f, 0f), new Vector3(1f, 2f, 70f), _wall);
        }

        /// <summary>
        /// Five ramps straddling the 47-degree slope limit. Walk up each one in
        /// turn: the first three should be easy, 45 should be a grind, 60 should
        /// refuse and slide you back.
        /// </summary>
        private static void BuildRamps()
        {
            var root = new GameObject("Ramps").transform;
            float[] angles = { 15f, 25f, 35f, 45f, 60f };
            float x = -16f;

            foreach (float angle in angles)
            {
                const float length = 7f;
                float rise = Mathf.Sin(angle * Mathf.Deg2Rad) * length;
                float run = Mathf.Cos(angle * Mathf.Deg2Rad) * length;

                var ramp = Solid($"Ramp_{angle:0}deg", root,
                    new Vector3(x, rise * 0.5f, 12f + run * 0.5f),
                    new Vector3(4f, 0.4f, length), _ramp,
                    new Vector3(-angle, 0f, 0f));

                // A landing at the top of each, so a successful climb has a payoff
                // and you can jump back down.
                if (angle <= 45f)
                {
                    Solid($"RampTop_{angle:0}", root,
                        new Vector3(x, rise, 12f + run + 1.5f),
                        new Vector3(4f, 0.4f, 3f), _platform);
                }

                x += 8f;
            }
        }

        /// <summary>
        /// Two flights. 0.2m steps should be walked up without a hitch; 0.55m steps
        /// exceed stepOffset and must be jumped.
        /// </summary>
        private static void BuildStairs()
        {
            var root = new GameObject("Stairs").transform;

            for (int i = 0; i < 10; i++)
            {
                float h = 0.2f * (i + 1);
                Solid($"StepLow_{i}", root,
                    new Vector3(-24f, h * 0.5f, -6f - i * 0.7f),
                    new Vector3(5f, h, 0.7f), _stair);
            }

            for (int i = 0; i < 5; i++)
            {
                float h = 0.55f * (i + 1);
                Solid($"StepHigh_{i}", root,
                    new Vector3(-16f, h * 0.5f, -6f - i * 1.4f),
                    new Vector3(4f, h, 1.4f), _stair);
            }
        }

        /// <summary>
        /// A corridor and a corner. This is the camera-collision test: walk into
        /// the corner and the camera should pull in tight instead of punching
        /// through the wall.
        /// </summary>
        private static void BuildWalls()
        {
            var root = new GameObject("Walls").transform;

            Solid("Corridor_L", root, new Vector3(9f, 2f, -12f), new Vector3(0.5f, 4f, 14f), _wall);
            Solid("Corridor_R", root, new Vector3(13f, 2f, -12f), new Vector3(0.5f, 4f, 14f), _wall);
            Solid("Corridor_End", root, new Vector3(11f, 2f, -19f), new Vector3(4.5f, 4f, 0.5f), _wall);
            Solid("Corridor_Roof", root, new Vector3(11f, 4f, -14f), new Vector3(4.5f, 0.4f, 10f), _wall);

            Solid("Corner_A", root, new Vector3(22f, 2f, -6f), new Vector3(8f, 4f, 0.5f), _wall);
            Solid("Corner_B", root, new Vector3(26f, 2f, -10f), new Vector3(0.5f, 4f, 8f), _wall);
        }

        /// <summary>
        /// Platforms at rising heights and a widening gap. JumpHeight is 1.4m, so
        /// the 1.2m platform is comfortable, 1.6m needs a running start, and the
        /// last one should be impossible — that is the point.
        /// </summary>
        private static void BuildJumpCourse()
        {
            var root = new GameObject("JumpCourse").transform;

            float[] heights = { 0.5f, 1.0f, 1.4f, 1.9f, 2.6f };
            for (int i = 0; i < heights.Length; i++)
            {
                Solid($"Platform_{heights[i]:0.0}m", root,
                    new Vector3(-6f + i * 3.5f, heights[i] * 0.5f, -14f),
                    new Vector3(2.6f, heights[i], 2.6f), _platform);
            }

            // Gap jumps: the spacing grows, so you find the limit by falling into it.
            float z = -22f;
            float[] gaps = { 1.5f, 2.5f, 3.5f, 4.5f };
            float cursor = -8f;
            Solid("Gap_Start", root, new Vector3(cursor, 0.6f, z), new Vector3(3f, 1.2f, 3f), _platform);
            foreach (float gap in gaps)
            {
                cursor += 3f + gap;
                Solid($"Gap_{gap:0.0}m", root, new Vector3(cursor, 0.6f, z), new Vector3(3f, 1.2f, 3f), _platform);
            }
        }

        private static void BuildObstacles()
        {
            var root = new GameObject("Obstacles").transform;

            Solid("Crate_A", root, new Vector3(4f, 0.5f, 4f), Vector3.one, _obstacle);
            Solid("Crate_B", root, new Vector3(5.4f, 0.5f, 5.4f), Vector3.one, _obstacle);
            Solid("Crate_C", root, new Vector3(4.7f, 1.5f, 4.7f), Vector3.one, _obstacle);
            Solid("Pillar_A", root, new Vector3(-5f, 1.75f, 6f), new Vector3(1f, 3.5f, 1f), _obstacle);
            Solid("Pillar_B", root, new Vector3(-8f, 1.75f, 3f), new Vector3(1f, 3.5f, 1f), _obstacle);
            Solid("LowBar", root, new Vector3(0f, 1.1f, 8f), new Vector3(6f, 0.3f, 0.3f), _obstacle);
        }

        /// <summary>
        /// Four spawn points, deliberately spread out. PrototypeNetPlayer looks these
        /// up by name and assigns one per client id.
        ///
        /// The spacing is chosen for the voice test: adjacent points are ~6 m apart
        /// (well inside the 18 m voice range) and the far pair are ~28 m apart (well
        /// outside it), so two players can start audible or inaudible without walking.
        /// </summary>
        private static void BuildSpawnMarker()
        {
            var root = new GameObject("PlayerSpawns").transform;

            Vector3[] points =
            {
                new Vector3(-3f, 0.3f, -2f),
                new Vector3( 3f, 0.3f, -2f),
                new Vector3(-3f, 0.3f, -8f),
                new Vector3(25f, 0.3f, -2f),   // deliberately out of voice range
            };

            for (int i = 0; i < points.Length; i++)
            {
                var marker = new GameObject($"PlayerSpawn_{i}");
                marker.transform.SetParent(root, false);
                marker.transform.position = points[i];
            }
        }

        /// <summary>
        /// The NetworkManager is the session itself: it owns the transport, decides
        /// which prefab represents a player, and spawns one per connected client.
        /// Assigning PlayerPrefab is what makes spawning automatic — without it you
        /// connect successfully and nobody appears.
        /// </summary>
        private static void BuildNetwork(GameObject playerPrefab)
        {
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();

            // See CourthouseBuilder: NetworkConfig can come back null on a component
            // added in edit mode, depending on what ran just before.
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();

            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
            nm.NetworkConfig.ConnectionApproval = false;
            transport.SetConnectionData("127.0.0.1", 7777);

            var hud = nmGo.AddComponent<PrototypeNetworkHud>();
            hud.Address = "127.0.0.1";
            hud.Port = 7777;
            hud.OfflinePlayerPrefab = playerPrefab;

            // Inert unless the process is launched with -mpauto.
            nmGo.AddComponent<PrototypeMpAutoTest>();
        }

        /// <summary>
        /// Built with no target on purpose. There is no local player until a session
        /// starts, so PrototypeNetPlayer points this at whichever character turns out
        /// to be ours. Same for the debug HUD.
        /// </summary>
        private static void BuildCamera()
        {
            var cameraGo = new GameObject("PlayerCamera");
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 400f;
            cameraGo.AddComponent<AudioListener>();

            cameraGo.AddComponent<PlayerCameraRig>();

            // Overlooks the course so the menu is not shown against a black screen.
            cameraGo.transform.position = new Vector3(0f, 4f, -10f);
            cameraGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        }

        private static void BuildDebugHud()
        {
            var hudGo = new GameObject("DebugHUD");
            hudGo.AddComponent<PlayerDebugHud>();
            hudGo.AddComponent<CaseClosed.Game.Prototype.Voice.VoiceHud>();
        }
    }
}
