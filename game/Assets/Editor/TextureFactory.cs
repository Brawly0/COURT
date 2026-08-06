using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Procedural PS1-flavor textures, baked to PNG assets and imported with
    /// point filtering (the crunch IS the style). Deterministic, regenerated
    /// on every rebuild, no external art dependencies.
    /// </summary>
    public static class TextureFactory
    {
        private const string Dir = "Assets/Textures";

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u;
                h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private static float Noise(int x, int y, int cell)
        {
            // blocky value noise - PS1 textures were painted coarse
            return Hash(x / cell, y / cell);
        }

        public static Texture2D WoodPlanks() => Bake("wood", 256, 256, (x, y) =>
        {
            int plank = y / 64;
            float baseV = 0.34f + Hash(plank, 7) * 0.07f;
            float grain = Mathf.Sin((x + Hash(plank, 3) * 200f) * 0.06f) * 0.020f;  // long, calm grain
            float seam = (y % 64 < 2) ? -0.10f : 0f;
            float v = baseV + grain + seam + (Noise(x, y, 24) - 0.5f) * 0.018f;
            return new Color(v * 1.22f, v * 0.86f, v * 0.60f);
        });

        public static Texture2D CheckerFloor() => Bake("checker", 256, 256, (x, y) =>
        {
            bool a = ((x / 128) + (y / 128)) % 2 == 0;
            Color c = a ? new Color(0.63f, 0.59f, 0.48f) : new Color(0.33f, 0.38f, 0.33f);
            if (x % 128 < 3 || y % 128 < 3) c *= 0.55f;                    // grout
            float n = (Noise(x, y, 16) - 0.5f) * 0.05f;
            return new Color(c.r + n, c.g + n, c.b + n);
        });

        public static Texture2D TileFloor() => Bake("tile", 256, 256, (x, y) =>
        {
            Color c = new Color(0.40f, 0.42f, 0.38f);
            if (x % 64 < 2 || y % 64 < 2) c *= 0.6f;
            float n = (Noise(x, y, 12) - 0.5f) * 0.05f;
            return new Color(c.r + n, c.g + n, c.b + n);
        });

        public static Texture2D Brick() => Bake("brick", 256, 256, (x, y) =>
        {
            int row = y / 32;
            int bx = (x + (row % 2) * 32) / 64;
            bool mortar = (y % 32 < 3) || ((x + (row % 2) * 32) % 64 < 3);
            if (mortar) return new Color(0.55f, 0.52f, 0.48f);
            float v = 0.36f + Hash(bx, row) * 0.14f;
            return new Color(v * 1.15f, v * 0.52f, v * 0.40f);
        });

        public static Texture2D Carpet() => Bake("carpet", 128, 128, (x, y) =>
        {
            float v = 0.48f + (Noise(x, y, 3) - 0.5f) * 0.18f + (Noise(x, y, 16) - 0.5f) * 0.08f;
            return new Color(v, v, v);                                     // neutral - tinted per material
        });

        // Wall noise stays LOW frequency and LOW amplitude: high-frequency
        // speckle on big walls shimmers under dither and reads as visual static.
        public static Texture2D Plaster() => Bake("plaster", 128, 128, (x, y) =>
        {
            float v = 0.62f + (Noise(x, y, 32) - 0.5f) * 0.030f + (Noise(x, y, 64) - 0.5f) * 0.020f;
            return new Color(v, v * 0.95f, v * 0.83f);
        });

        public static Texture2D CeilingTiles() => Bake("ceiling", 256, 256, (x, y) =>
        {
            Color c = new Color(0.70f, 0.69f, 0.63f);
            int dx = Math.Min(x % 128, 127 - x % 128);
            int dy = Math.Min(y % 128, 127 - y % 128);
            if (x % 128 < 3 || y % 128 < 3) c *= 0.45f;                    // T-bar grid
            else if (Math.Min(dx, dy) < 8) c *= 0.88f;                     // edge shading
            float n = (Noise(x, y, 10) - 0.5f) * 0.04f;
            return new Color(c.r + n, c.g + n, c.b + n);
        });

        public static Texture2D Concrete() => Bake("concrete", 256, 256, (x, y) =>
        {
            float v = 0.40f + (Noise(x, y, 48) - 0.5f) * 0.055f + (Noise(x, y, 20) - 0.5f) * 0.030f;
            return new Color(v, v, v * 1.02f);
        });

        private static Texture2D Bake(string name, int w, int h, Func<int, int, Color> gen)
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "Textures");
            string path = $"{Dir}/{name}.png";

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, gen(x, y));
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;                        // the PS1 crunch
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
