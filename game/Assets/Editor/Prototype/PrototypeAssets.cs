using System.IO;
using UnityEditor;
using UnityEngine;

namespace CaseClosed.EditorTools.Prototype
{
    /// <summary>
    /// Shared plumbing for the movement-prototype builders: folder creation and
    /// flat-colour URP materials. Kept in one place so the character, the
    /// animations and the playground all agree on where things live.
    /// </summary>
    public static class PrototypeAssets
    {
        public const string RootFolder = "Assets/Prototype";
        public const string MaterialFolder = RootFolder + "/Materials";
        public const string AnimationFolder = RootFolder + "/Animation";
        public const string PrefabFolder = RootFolder + "/Prefabs";
        public const string SceneFolder = "Assets/Scenes";

        public const string CharacterPrefabPath = PrefabFolder + "/PlayerPrototype.prefab";
        public const string ControllerPath = AnimationFolder + "/PlayerPrototype.controller";
        public const string TestScenePath = SceneFolder + "/MovementPlayground.unity";

        /// <summary>Creates every folder in a path that does not exist yet.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static void EnsureAllFolders()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(AnimationFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(SceneFolder);
        }

        /// <summary>
        /// A flat, unlit-looking URP material. Reuses the asset if it already
        /// exists so re-running a builder does not spawn duplicates — and so any
        /// colour you tweak by hand survives a rebuild.
        /// </summary>
        public static Material GetOrCreateMaterial(string name, Color color, float smoothness = 0.05f)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var material = new Material(shader) { name = name };
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// A box mesh with no collider. The CharacterController capsule handles all
        /// player collision — leaving colliders on the body parts would make the
        /// character snag on itself and on the world.
        /// </summary>
        public static GameObject Box(string name, Transform parent, Vector3 localCenter,
                                     Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            return go;
        }

        /// <summary>
        /// An empty pivot. Animation rotates these, never the meshes, so a mesh can
        /// be resized or replaced without touching a single animation curve.
        /// </summary>
        public static Transform Joint(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }
    }
}
