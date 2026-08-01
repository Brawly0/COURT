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
        private static void BuildPostFx()
        {
            const string path = "Assets/Settings/CourthouseVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.38f);
            vig.smoothness.Override(0.45f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.45f);
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.08f);
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

        private static GameObject BuildPlayerPrefab()
        {
            var player = new GameObject("Player");

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "Visual";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var suit = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Suit.mat");
            if (suit != null) visual.GetComponent<Renderer>().sharedMaterial = suit;

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;      // no skybox — the void is black
            cam.backgroundColor = new Color(0.008f, 0.008f, 0.012f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;               // vignette + grain live here
            camGo.AddComponent<AudioListener>();

            player.AddComponent<FirstPersonController>();
            player.AddComponent<Interactor>();
            player.AddComponent<NetworkObject>();
            player.AddComponent<ClientNetworkTransform>();
            player.AddComponent<NetPlayer>();

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
            UnityEngine.Object.DestroyImmediate(player);
            return prefab;
        }

        private static void BuildNetwork(GameObject playerPrefab)
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

        private static void BuildGameSystems(GameObject playerPrefab)
        {
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
