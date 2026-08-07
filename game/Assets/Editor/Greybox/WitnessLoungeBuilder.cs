using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game.Witnesses;

namespace CaseClosed.EditorTools.Greybox
{
    /// <summary>
    /// Seats a pool of witness NPCs in the existing Witness Lounge.
    ///
    /// POOLED, NOT SPAWNED, matching the evidence bodies: scene objects with
    /// NetworkObjects need no NGO prefab registration, and the cast is small and
    /// known (six names, one of whom is the defendant). A case assigns names to pool
    /// entries; unassigned entries stay invisible.
    ///
    /// Placed in the north-WEST of the lounge. The north-east is the existing
    /// interview nook, walled off along x = 39 and z = 3, and the south half already
    /// holds the benches and sofas — so this is the one quadrant that was free.
    /// Nothing existing moves.
    ///
    /// Menu: Case Closed > Greybox > Build Witness Lounge NPCs
    /// </summary>
    public static class WitnessLoungeBuilder
    {
        private const string RootName = "WitnessNpcs";
        private const float LoungeCentreX = 40f;

        /// <summary>Standing positions, clear of the nook partition and the sofas.</summary>
        private static readonly Vector2[] Stations =
        {
            new Vector2(-9.5f, 6.5f),
            new Vector2(-7.0f, 7.4f),
            new Vector2(-4.5f, 6.5f),
            new Vector2(-8.5f, 3.6f),
            new Vector2(-5.5f, 3.6f),
        };

        [MenuItem("Case Closed/Greybox/Build Witness Lounge NPCs", priority = 2)]
        public static void BuildFromMenu()
        {
            int n = Build();
            EditorUtility.DisplayDialog("Witness lounge built",
                $"{n} witness NPCs pooled in the lounge.\n\n" +
                "They stay invisible until a case seats them.",
                "OK");
        }

        public static int Build()
        {
            GreyboxKit.BuildMaterials();

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(RootName).transform;

            for (int i = 0; i < Stations.Length; i++)
                BuildWitness(root, i, new Vector3(LoungeCentreX + Stations[i].x, 0f, Stations[i].y));

            GreyboxKit.SignText("Sign_Waiting", root,
                new Vector3(LoungeCentreX - 7f, 3.4f, 9.85f), Quaternion.Euler(0f, 180f, 0f),
                "WITNESS WAITING", 0.6f);

            EnsureDirector();
            EnsureHud();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[Witness] Built {Stations.Length} pooled witness NPCs in the lounge.");
            return Stations.Length;
        }

        /// <summary>
        /// A blocky standing figure in the courthouse style. Deliberately NOT the
        /// player prefab: that carries movement, input, voice, carry sockets and a
        /// ClientNetworkTransform, none of which a stationary witness should own.
        ///
        /// The generator supplies no appearance data at all — only names — so the
        /// figures differ by material alone and identity comes from the name label.
        /// </summary>
        private static void BuildWitness(Transform parent, int index, Vector3 position)
        {
            var go = new GameObject($"Witness_{index:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            // Facing roughly back into the room, so players approach a face.
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            Material coat = index switch
            {
                0 => GreyboxKit.Seat,
                1 => GreyboxKit.Wood,
                2 => GreyboxKit.Metal,
                3 => GreyboxKit.Bench,
                _ => GreyboxKit.Accent,
            };

            GreyboxKit.Box("Legs", go.transform, new Vector3(0f, 0.42f, 0f), new Vector3(0.36f, 0.84f, 0.28f), GreyboxKit.Wall);
            GreyboxKit.Box("Torso", go.transform, new Vector3(0f, 1.16f, 0f), new Vector3(0.50f, 0.64f, 0.30f), coat);
            GreyboxKit.Box("Arm_L", go.transform, new Vector3(-0.31f, 1.14f, 0f), new Vector3(0.11f, 0.58f, 0.13f), coat);
            GreyboxKit.Box("Arm_R", go.transform, new Vector3(0.31f, 1.14f, 0f), new Vector3(0.11f, 0.58f, 0.13f), coat);
            GreyboxKit.Box("Head", go.transform, new Vector3(0f, 1.63f, 0f), new Vector3(0.30f, 0.30f, 0.28f), GreyboxKit.Sign);

            // GreyboxKit marks its boxes static, which would batch them at their
            // authoring position. Harmless while they never move, but they DO get
            // hidden and shown, and a batched renderer is a trap worth not setting.
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
                renderer.gameObject.isStatic = false;
            }
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            go.isStatic = false;

            // One collider on the root, so the interaction ray targets the person
            // rather than whichever limb happened to be under the crosshair.
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.95f, 0f);
            collider.size = new Vector3(0.7f, 1.9f, 0.6f);

            go.AddComponent<NetworkObject>();

            var npc = go.AddComponent<WitnessNpc>();
            npc.Prompt = "Interview Witness";
            npc.HoldDuration = 3f;
            npc.InterviewSeconds = 3f;
            npc.ReviewSeconds = 0.15f;
            npc.MaxDistance = 2.8f;
            npc.RequiresLineOfSight = true;
        }

        private static void EnsureDirector()
        {
            var existing = Object.FindAnyObjectByType<WitnessDirector>();
            if (existing != null) return;

            var go = new GameObject("WitnessDirector");
            go.AddComponent<NetworkObject>();
            go.AddComponent<WitnessDirector>();
        }

        private static void EnsureHud()
        {
            var hud = GameObject.Find("DebugHUD");
            if (hud == null) return;
            if (hud.GetComponent<WitnessStatementHud>() == null)
                hud.AddComponent<WitnessStatementHud>();
        }
    }
}
