using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Converts every material in the open scene to the PSX shader (vertex
    /// snapping + affine warp + dither), spawns a PSX character body on the
    /// player prefab, and drops a live cast of puppets into the courthouse so
    /// the building stops feeling empty.
    /// Menu: Case Closed > Apply PSX Theme + Characters.
    /// </summary>
    public static class PsxThemeApplier
    {
        [MenuItem("Case Closed/Apply PSX Theme + Characters")]
        public static void Run()
        {
            var psx = Shader.Find("CaseClosed/PSX");
            if (psx == null)
            {
                EditorUtility.DisplayDialog("Case Closed",
                    "PSX shader not found. Let Unity finish importing Assets/Shaders/PSX.shader, then run again.", "OK");
                return;
            }

            // rebake the procedural textures first - wall/floor noise was too
            // high-frequency and shimmered under the dither
            TextureFactory.Plaster(); TextureFactory.Concrete(); TextureFactory.WoodPlanks();
            TextureFactory.CheckerFloor(); TextureFactory.TileFloor();
            TextureFactory.Brick(); TextureFactory.Carpet(); TextureFactory.CeilingTiles();

            int converted = ConvertMaterials(psx);
            int cast = SpawnCast();
            AttachBodyToPlayerPrefab();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[CaseClosed] PSX theme applied: {converted} materials converted, {cast} characters placed.");
        }

        /// <summary>Swap every project material onto the PSX shader, keeping colour + texture.</summary>
        private static int ConvertMaterials(Shader psx)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == psx) continue;

                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                        : m.HasProperty("_Color") ? m.color : Color.white;
                Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                Vector2 scale = m.mainTextureScale, offset = m.mainTextureOffset;
                bool emissive = m.IsKeywordEnabled("_EMISSION");
                Color emis = emissive && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;

                m.shader = psx;
                m.SetColor("_BaseColor", c);
                if (tex != null) m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", scale);
                m.SetTextureOffset("_BaseMap", offset);
                // emissive surfaces (tubes, screens) keep glowing via ambient boost
                m.SetFloat("_AmbientBoost", emissive ? 2f + emis.maxColorComponent : 0.85f);
                // Big architectural surfaces: warp/snap kept subtle (see shader
                // header). Aggressive values swim horribly on 45m quads.
                m.SetFloat("_SnapAmount", 220f);
                m.SetFloat("_AffineAmount", 0.10f);
                m.SetFloat("_ColorDepth", 48f);
                m.SetFloat("_DitherAmount", 0.18f);
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        /// <summary>Give the networked player prefab a visible PSX body.</summary>
        private static void AttachBodyToPlayerPrefab()
        {
            const string path = "Assets/Prefabs/Player.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            var root = PrefabUtility.LoadPrefabContents(path);
            var old = root.transform.Find("Visual");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var stale = root.transform.Find("Body");
            if (stale != null) Object.DestroyImmediate(stale.gameObject);

            var marker = root.GetComponent<PlayerBodySpawner>();
            if (marker == null) root.AddComponent<PlayerBodySpawner>();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>Place idle NPCs around the building so it reads as staffed.</summary>
        private static int SpawnCast()
        {
            var existing = GameObject.Find("Cast");
            if (existing != null) Object.DestroyImmediate(existing);

            var anchors = Object.FindObjectsByType<ZoneAnchor>(FindObjectsSortMode.None);
            if (anchors.Length == 0) return 0;

            var root = new GameObject("Cast");
            string[] zones = { "MainHall", "EvidenceLocker", "Cafeteria", "Security",
                               "Archives", "RecordsRoom", "PressRoom", "ProsecutionOffice" };
            int placed = 0;
            foreach (var z in zones)
            {
                var a = anchors.FirstOrDefault(x => x.ZoneName == z);
                if (a == null) continue;

                var go = new GameObject("NPC_" + z);
                go.transform.SetParent(root.transform);
                go.transform.position = a.transform.position + new Vector3(
                    Mathf.Cos(placed * 2.4f) * 1.4f, 0f, Mathf.Sin(placed * 2.4f) * 1.4f);
                go.transform.rotation = Quaternion.Euler(0f, placed * 47f, 0f);

                var rig = CharacterBuilder.Build(go.transform, 1000 + placed * 17, false);
                var anim = go.AddComponent<CharacterAnimator>();
                anim.Init(rig, 1000 + placed * 17);
                anim.Stress = Mathf.Repeat(placed * 0.23f, 0.7f);

                var idler = go.AddComponent<NpcIdle>();
                idler.HomePosition = go.transform.position;
                placed++;
            }
            return placed;
        }
    }
}
