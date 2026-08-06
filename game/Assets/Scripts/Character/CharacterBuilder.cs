using System.Collections.Generic;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Builds the PS1-era legal puppet from primitives, at runtime, in code.
    /// Art bible (GDD 11): oversized head, tiny pupils in huge whites (eyes are
    /// the tell channel), cheap suit, blocky limbs, 300-800 tri budget.
    /// One base rig; head/hair/tie/accessory swaps give the cast its variety,
    /// seeded per character so every client sees the same Greg.
    /// </summary>
    public static class CharacterBuilder
    {
        public const float Height = 1.8f;

        public class Rig
        {
            public GameObject Root;
            public Transform Head, HeadPivot, TorsoT, ArmL, ArmR, LegL, LegR;
            public Transform EyeL, EyeR, PupilL, PupilR, Jaw;
        }

        private static readonly Color[] SuitColors =
        {
            new Color(0.11f, 0.11f, 0.14f),   // charcoal
            new Color(0.16f, 0.15f, 0.19f),   // slate
            new Color(0.10f, 0.13f, 0.17f),   // navy
            new Color(0.19f, 0.16f, 0.13f),   // brown
            new Color(0.13f, 0.16f, 0.14f),   // olive
        };
        private static readonly Color[] SkinColors =
        {
            new Color(0.85f, 0.68f, 0.53f), new Color(0.74f, 0.55f, 0.40f),
            new Color(0.62f, 0.44f, 0.31f), new Color(0.45f, 0.31f, 0.22f),
            new Color(0.90f, 0.75f, 0.63f),
        };
        private static readonly Color[] TieColors =
        {
            new Color(0.45f, 0.10f, 0.10f), new Color(0.12f, 0.20f, 0.42f),
            new Color(0.35f, 0.28f, 0.08f), new Color(0.15f, 0.30f, 0.20f),
        };
        private static readonly Color[] HairColors =
        {
            new Color(0.08f, 0.07f, 0.06f), new Color(0.22f, 0.14f, 0.07f),
            new Color(0.45f, 0.34f, 0.18f), new Color(0.55f, 0.55f, 0.55f),
        };

        /// <summary>Deterministic per-character look: same seed = same person everywhere.</summary>
        public static Rig Build(Transform parent, int seed, bool isPlayer)
        {
            var rng = new System.Random(seed);
            var rig = new Rig();

            var suit = Pick(SuitColors, rng);
            var skin = Pick(SkinColors, rng);
            var tie = Pick(TieColors, rng);
            var hair = Pick(HairColors, rng);
            bool balding = rng.NextDouble() < 0.25;
            bool glasses = rng.NextDouble() < 0.30;
            bool stache = rng.NextDouble() < 0.22;
            float bulk = 0.88f + (float)rng.NextDouble() * 0.30f;

            rig.Root = new GameObject(isPlayer ? "Body" : "CharacterBody");
            rig.Root.transform.SetParent(parent, false);

            // ---- torso: tapered suit jacket ----
            var torso = Cube(rig.Root.transform, "Torso",
                new Vector3(0f, 1.12f, 0f), new Vector3(0.46f * bulk, 0.62f, 0.26f * bulk), suit);
            rig.TorsoT = torso;
            Cube(torso, "Hips", new Vector3(0f, -0.55f, 0f), new Vector3(0.92f, 0.30f, 1.0f), suit * 0.85f, true);
            // shirt V + tie
            Cube(torso, "Shirt", new Vector3(0f, 0.28f, -0.52f), new Vector3(0.42f, 0.42f, 0.06f),
                new Color(0.86f, 0.85f, 0.80f), true);
            Cube(torso, "Tie", new Vector3(0f, 0.05f, -0.56f), new Vector3(0.14f, 0.70f, 0.05f), tie, true);

            // ---- head: oversized, the art bible's whole point ----
            rig.HeadPivot = new GameObject("HeadPivot").transform;
            rig.HeadPivot.SetParent(rig.Root.transform, false);
            rig.HeadPivot.localPosition = new Vector3(0f, 1.46f, 0f);

            var head = Cube(rig.HeadPivot, "Head", new Vector3(0f, 0.17f, 0f),
                new Vector3(0.30f, 0.34f, 0.28f), skin);
            rig.Head = head;
            Cube(head, "Neck", new Vector3(0f, -0.62f, 0f), new Vector3(0.45f, 0.28f, 0.5f), skin * 0.9f, true);

            // eyes: huge whites, tiny pupils
            rig.EyeL = Cube(head, "EyeL", new Vector3(-0.24f, 0.10f, -0.51f),
                new Vector3(0.30f, 0.34f, 0.06f), new Color(0.97f, 0.96f, 0.93f), true);
            rig.EyeR = Cube(head, "EyeR", new Vector3(0.24f, 0.10f, -0.51f),
                new Vector3(0.30f, 0.34f, 0.06f), new Color(0.97f, 0.96f, 0.93f), true);
            rig.PupilL = Cube(rig.EyeL, "PupilL", new Vector3(0f, 0f, -0.6f),
                new Vector3(0.34f, 0.34f, 0.5f), new Color(0.04f, 0.04f, 0.05f), true);
            rig.PupilR = Cube(rig.EyeR, "PupilR", new Vector3(0f, 0f, -0.6f),
                new Vector3(0.34f, 0.34f, 0.5f), new Color(0.04f, 0.04f, 0.05f), true);
            // brows sell stress far better than a stat readout
            Cube(head, "BrowL", new Vector3(-0.24f, 0.30f, -0.52f), new Vector3(0.32f, 0.07f, 0.06f), hair, true);
            Cube(head, "BrowR", new Vector3(0.24f, 0.30f, -0.52f), new Vector3(0.32f, 0.07f, 0.06f), hair, true);

            rig.Jaw = Cube(head, "Jaw", new Vector3(0f, -0.30f, -0.30f),
                new Vector3(0.72f, 0.22f, 0.45f), skin * 0.95f, true);
            if (stache)
                Cube(head, "Moustache", new Vector3(0f, -0.14f, -0.53f), new Vector3(0.40f, 0.09f, 0.06f), hair, true);
            if (!balding)
            {
                Cube(head, "Hair", new Vector3(0f, 0.42f, 0.02f), new Vector3(1.06f, 0.28f, 1.06f), hair, true);
                Cube(head, "HairBack", new Vector3(0f, 0.10f, 0.52f), new Vector3(1.02f, 0.75f, 0.10f), hair, true);
            }
            else
            {
                Cube(head, "HairRing", new Vector3(0f, 0.16f, 0.30f), new Vector3(1.04f, 0.30f, 0.55f), hair, true);
            }
            if (glasses)
            {
                Cube(head, "GlassL", new Vector3(-0.24f, 0.10f, -0.56f), new Vector3(0.40f, 0.42f, 0.03f),
                    new Color(0.05f, 0.05f, 0.06f), true);
                Cube(head, "GlassR", new Vector3(0.24f, 0.10f, -0.56f), new Vector3(0.40f, 0.42f, 0.03f),
                    new Color(0.05f, 0.05f, 0.06f), true);
                Cube(head, "GlassBridge", new Vector3(0f, 0.10f, -0.55f), new Vector3(0.16f, 0.05f, 0.03f),
                    new Color(0.05f, 0.05f, 0.06f), true);
            }

            // ---- limbs (pivots at the shoulder/hip so they swing right) ----
            rig.ArmL = Limb(rig.Root.transform, "ArmL", new Vector3(-0.30f * bulk, 1.38f, 0f), suit, skin, 0.52f);
            rig.ArmR = Limb(rig.Root.transform, "ArmR", new Vector3(0.30f * bulk, 1.38f, 0f), suit, skin, 0.52f);
            rig.LegL = Limb(rig.Root.transform, "LegL", new Vector3(-0.12f, 0.80f, 0f), suit * 0.8f, null, 0.78f);
            rig.LegR = Limb(rig.Root.transform, "LegR", new Vector3(0.12f, 0.80f, 0f), suit * 0.8f, null, 0.78f);

            return rig;
        }

        private static Transform Limb(Transform parent, string name, Vector3 pivot,
                                      Color cloth, Color? handSkin, float len)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivot;

            Cube(go.transform, name + "_Sleeve", new Vector3(0f, -len * 0.5f, 0f),
                new Vector3(0.15f, len, 0.16f), cloth);
            if (handSkin.HasValue)
                Cube(go.transform, name + "_Hand", new Vector3(0f, -len - 0.07f, 0f),
                    new Vector3(0.15f, 0.15f, 0.16f), handSkin.Value);
            else
                Cube(go.transform, name + "_Shoe", new Vector3(0f, -len - 0.05f, -0.05f),
                    new Vector3(0.17f, 0.10f, 0.26f), new Color(0.07f, 0.06f, 0.06f));
            return go.transform;
        }

        private static Transform Cube(Transform parent, string name, Vector3 pos, Vector3 scale,
                                      Color color, bool local = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MaterialCache.Get(color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        private static T Pick<T>(T[] arr, System.Random rng) => arr[rng.Next(arr.Length)];
    }

    /// <summary>Shared flat-color materials, so 8 players + 6 NPCs don't spawn 200 materials.</summary>
    public static class MaterialCache
    {
        private static readonly Dictionary<int, Material> _cache = new Dictionary<int, Material>();
        private static Shader _shader;

        public static Material Get(Color c)
        {
            int key = (Mathf.RoundToInt(c.r * 255) << 16) | (Mathf.RoundToInt(c.g * 255) << 8) | Mathf.RoundToInt(c.b * 255);
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            if (_shader == null)
                _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(_shader) { color = c };
            m.SetFloat("_Smoothness", 0.05f);
            _cache[key] = m;
            return m;
        }
    }
}
