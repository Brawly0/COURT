using System;
using System.Collections.Generic;

namespace CaseClosed.TruthEngine
{
    /// <summary>
    /// PCG32 deterministic PRNG. We own the implementation so the same seed
    /// produces the same case on every platform, runtime, and engine version —
    /// the Daily Case, replays, and Resume Case all depend on this (GDD 12).
    /// Never swap for System.Random.
    /// </summary>
    public sealed class Pcg32
    {
        private ulong _state;
        private readonly ulong _inc;

        public Pcg32(ulong seed, ulong sequence = 54u)
        {
            _state = 0;
            _inc = (sequence << 1) | 1u;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * 6364136223846793005ul + _inc;
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

        /// <summary>Uniform int in [0, maxExclusive). Unbiased via rejection.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint bound = (uint)maxExclusive;
            uint threshold = (uint)(-bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Next(int minInclusive, int maxExclusive)
            => minInclusive + Next(maxExclusive - minInclusive);

        public bool Chance(double p) => NextDouble() < p;

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

        public void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
