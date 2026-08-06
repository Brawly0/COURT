using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// One-shot scene assembly, runnable headless:
    ///   Unity -batchmode -projectPath game -executeMethod CaseClosed.EditorTools.ProjectSetup.Run
    /// Builds the graybox courthouse, the networked player prefab, the
    /// NetworkManager + case sync objects, saves Assets/Scenes/Courthouse.unity.
    /// Menu: Case Closed > Rebuild Courthouse Scene.
    /// </summary>
    public static class ProjectSetup
    {
        [MenuItem("Case Closed/Rebuild Courthouse Scene")]
        public static void RunFromMenu() => BuildAll(exitAfter: false);

        public static void Run() => BuildAll(exitAfter: true);

        private static void BuildAll(bool exitAfter)
        {
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                GrayboxBuilder.Build();
                BuildPostFx();
                var playerPrefab = BuildPlayerPrefab();
                BuildNetwork(playerPrefab);
                BuildGameSystems(playerPrefab);

                if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                    AssetDatabase.CreateFolder("Assets", "Scenes");
                const string path = "Assets/Scenes/Courthouse.unity";
                EditorSceneManager.SaveScene(scene, path);

                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
                AssetDatabase.SaveAssets();

                Debug.Log("[CaseClosed] Courthouse scene built and saved: " + path);
                if (exitAfter) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[CaseClosed] Scene build FAILED: " + e);
                if (exitAfter) EditorApplication.Exit(1);
            }
        }

        /// <summary>Mockup-canon post: vignette + film grain + slight desaturation.</summary>
        public static void BuildPostFx()
        {
            const string path = "Assets/Settings/CourthouseVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.38f);
            vig.smoothness.Override(0.45f);
            // grain/CA kept LOW: both are per-frame motion layered on every pixel,
            // and at 0.45 grain the whole screen visibly crawled
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.16f);
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.02f);
            var col = profile.Add<ColorAdjustments>(true);
            col.saturation.Override(-14f);
            col.contrast.Override(10f);
            col.postExposure.Override(0.15f);
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("PostFX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        public static GameObject BuildPlayerPrefab()
        {
            var player = new GameObject("Player");

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            // No capsule placeholder: PlayerBodySpawner builds the PSX puppet at
            // runtime, so remote players see the same character model as the NPCs.

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;      // no skybox - the void is black
            cam.backgroundColor = new Color(0.008f, 0.008f, 0.012f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;               // vignette + grain live here
            camGo.AddComponent<AudioListener>();

            player.AddComponent<FirstPersonController>();
            player.AddComponent<Interactor>();
            player.AddComponent<NetworkObject>();

            // owner-authoritative transform, INTERPOLATED so remote players glide
            // instead of teleporting between network ticks
            var cnt = player.AddComponent<ClientNetworkTransform>();
            cnt.Interpolate = true;
            cnt.PositionThreshold = 0.02f;
            cnt.RotAngleThreshold = 1.0f;
            cnt.SyncScaleX = cnt.SyncScaleY = cnt.SyncScaleZ = false;

            player.AddComponent<NetPlayer>();
            player.AddComponent<PlayerBodySpawner>();   // the PSX puppet everyone sees

            // proximity voice: 3D AudioSource on the head, mic capture for the owner
            var voiceGo = new GameObject("Voice");
            voiceGo.transform.SetParent(player.transform, false);
            voiceGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            var audio = voiceGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            voiceGo.AddComponent<ProximityVoice>();

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
            UnityEngine.Object.DestroyImmediate(player);
            return prefab;
        }

        public static void BuildNetwork(GameObject playerPrefab)
        {
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var utp = nmGo.AddComponent<UnityTransport>();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;

            var syncGo = new GameObject("CaseNetSync");
            syncGo.AddComponent<NetworkObject>();
            syncGo.AddComponent<CaseNetSync>();
        }

        public static void BuildGameSystems(GameObject playerPrefab)
        {
            if (GameObject.Find("SpawnPoint") == null)
            {
                var sp = new GameObject("SpawnPoint");
                sp.transform.position = new Vector3(12f, 0.1f, 0f);   // procedural hall default
            }
            var systems = new GameObject("GameSystems");
            var runtime = systems.AddComponent<CaseRuntime>();   // Seed defaults to 2 (THE HUMMUS HEIST)
            runtime.EvidenceMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Manila.mat");
            runtime.EvidenceTentMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EvidenceYellow.mat");
            runtime.WitnessSuitMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Suit.mat");
            runtime.WitnessSkinMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Skin.mat");
            systems.AddComponent<HudController>();
            systems.AddComponent<MpAutoTest>();    // inert unless launched with -mpauto
            var boot = systems.AddComponent<NetworkBootstrapHud>();
            boot.PlayerPrefab = playerPrefab;
        }
    }
}
