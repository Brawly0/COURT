using UnityEditor;
using UnityEngine;
using CaseClosed.Game.Greybox;

namespace CaseClosed.EditorTools.Greybox
{
    /// <summary>
    /// Blockout primitives: solid boxes, walls with doorways punched in them, floors
    /// and text signs. Everything the courthouse is made of.
    ///
    /// Walls are the only non-trivial part. A doorway is not a hole in a mesh — it is
    /// three boxes (left of the door, right of the door, lintel above it). Building
    /// them by hand is where blockouts usually go wrong, so it happens in one place.
    /// </summary>
    public static class GreyboxKit
    {
        public const float WallThickness = 0.4f;
        public const float DoorWidth = 4f;
        public const float DoorHeight = 3.2f;

        public static Material Floor, Wall, Accent, Wood, Metal, Seat, Bench, Sign, Glass;

        public static void BuildMaterials()
        {
            Floor  = Mat("Grey_Floor",  new Color(0.55f, 0.55f, 0.57f));
            Wall   = Mat("Grey_Wall",   new Color(0.72f, 0.71f, 0.68f));
            Accent = Mat("Grey_Accent", new Color(0.45f, 0.52f, 0.62f));
            Wood   = Mat("Grey_Wood",   new Color(0.48f, 0.35f, 0.23f));
            Metal  = Mat("Grey_Metal",  new Color(0.40f, 0.42f, 0.45f));
            Seat   = Mat("Grey_Seat",   new Color(0.35f, 0.42f, 0.38f));
            Bench  = Mat("Grey_Bench",  new Color(0.56f, 0.42f, 0.28f));
            Sign   = Mat("Grey_Sign",   new Color(0.15f, 0.16f, 0.18f));
            Glass  = Mat("Grey_Glass",  new Color(0.60f, 0.72f, 0.78f));
        }

        private static Material Mat(string name, Color color)
        {
            PrototypeAssetsBridge.EnsureFolder("Assets/Greybox/Materials");
            string path = $"Assets/Greybox/Materials/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>A solid, collidable box. Centre position, full size.</summary>
        public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
            return go;
        }

        /// <summary>Floor slab spanning a rectangle, top surface at y.</summary>
        public static void Slab(string name, Transform parent, float x0, float z0, float x1, float z1,
                                float y, Material material, float thickness = 0.5f)
        {
            Box(name, parent,
                new Vector3((x0 + x1) * 0.5f, y - thickness * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(Mathf.Abs(x1 - x0), thickness, Mathf.Abs(z1 - z0)),
                material);
        }

        /// <summary>
        /// Wall running along X at fixed Z. Pass doorAt to punch a doorway centred on
        /// that X coordinate; leave it NaN for a solid wall.
        /// </summary>
        public static void WallAlongX(string name, Transform parent, float x0, float x1, float z,
                                      float height, Material material,
                                      float doorAt = float.NaN, float doorWidth = DoorWidth,
                                      float doorHeight = DoorHeight)
        {
            if (float.IsNaN(doorAt))
            {
                Box(name, parent, new Vector3((x0 + x1) * 0.5f, height * 0.5f, z),
                    new Vector3(Mathf.Abs(x1 - x0), height, WallThickness), material);
                return;
            }

            float left = doorAt - doorWidth * 0.5f;
            float right = doorAt + doorWidth * 0.5f;

            if (left > x0)
                Box($"{name}_A", parent, new Vector3((x0 + left) * 0.5f, height * 0.5f, z),
                    new Vector3(left - x0, height, WallThickness), material);

            if (x1 > right)
                Box($"{name}_B", parent, new Vector3((right + x1) * 0.5f, height * 0.5f, z),
                    new Vector3(x1 - right, height, WallThickness), material);

            if (height > doorHeight)
                Box($"{name}_Lintel", parent,
                    new Vector3(doorAt, doorHeight + (height - doorHeight) * 0.5f, z),
                    new Vector3(doorWidth, height - doorHeight, WallThickness), material);
        }

        /// <summary>Wall running along Z at fixed X. Same doorway rules.</summary>
        public static void WallAlongZ(string name, Transform parent, float z0, float z1, float x,
                                      float height, Material material,
                                      float doorAt = float.NaN, float doorWidth = DoorWidth,
                                      float doorHeight = DoorHeight)
        {
            if (float.IsNaN(doorAt))
            {
                Box(name, parent, new Vector3(x, height * 0.5f, (z0 + z1) * 0.5f),
                    new Vector3(WallThickness, height, Mathf.Abs(z1 - z0)), material);
                return;
            }

            float near = doorAt - doorWidth * 0.5f;
            float far = doorAt + doorWidth * 0.5f;

            if (near > z0)
                Box($"{name}_A", parent, new Vector3(x, height * 0.5f, (z0 + near) * 0.5f),
                    new Vector3(WallThickness, height, near - z0), material);

            if (z1 > far)
                Box($"{name}_B", parent, new Vector3(x, height * 0.5f, (far + z1) * 0.5f),
                    new Vector3(WallThickness, height, z1 - far), material);

            if (height > doorHeight)
                Box($"{name}_Lintel", parent,
                    new Vector3(x, doorHeight + (height - doorHeight) * 0.5f, doorAt),
                    new Vector3(WallThickness, height - doorHeight, doorWidth), material);
        }

        /// <summary>
        /// Flat 3D text on a backing plate. Uses legacy TextMesh on purpose — it needs
        /// no font asset, no TMP import step, and renders as plain readable letters,
        /// which is all a blockout sign should be.
        /// </summary>
        public static void SignText(string name, Transform parent, Vector3 position, Quaternion rotation,
                                    string text, float size = 1f)
        {
            var plate = Box($"{name}_Plate", parent, position, Vector3.one, Sign);
            plate.transform.localRotation = rotation;
            plate.transform.localScale = new Vector3(text.Length * 0.62f * size + 1.2f, 1.5f * size, 0.15f);

            var textGo = new GameObject($"{name}_Text");
            textGo.transform.SetParent(parent, false);
            textGo.transform.localPosition = position + rotation * new Vector3(0f, 0f, -0.12f);
            textGo.transform.localRotation = rotation * Quaternion.Euler(0f, 180f, 0f);

            var mesh = textGo.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = 0.34f * size;
            mesh.fontSize = 64;
            mesh.color = new Color(0.95f, 0.95f, 0.92f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                mesh.font = font;
                textGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        /// <summary>Named region used by the debug HUD and the travel timer.</summary>
        public static void Volume(string roomName, Transform parent, float x0, float z0, float x1, float z1,
                                  float height, bool transitional = false)
        {
            var go = new GameObject($"Room_{roomName.Replace(' ', '_')}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((x0 + x1) * 0.5f, height * 0.5f, (z0 + z1) * 0.5f);

            var volume = go.AddComponent<RoomVolume>();
            volume.RoomName = roomName;
            volume.Size = new Vector3(Mathf.Abs(x1 - x0), height, Mathf.Abs(z1 - z0));
            volume.IsTransitional = transitional;
        }
    }

    /// <summary>Tiny shim so the greybox does not depend on the prototype's asset helper.</summary>
    public static class PrototypeAssetsBridge
    {
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
