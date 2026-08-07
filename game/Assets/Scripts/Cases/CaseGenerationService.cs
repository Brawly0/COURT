using System;
using System.Diagnostics;
using CaseClosed.TruthEngine;
using Debug = UnityEngine.Debug;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// The ONLY place Unity calls the generator. One method, one direction.
    ///
    /// Not a MonoBehaviour and not networked, so it can be called from a test, an
    /// editor menu, or the host at runtime without a scene existing. Keeping the
    /// boundary this thin is what stops Unity concepts leaking into the pure C#
    /// engine over time — there is exactly one door, and it is three lines wide.
    /// </summary>
    public static class CaseGenerationService
    {
        /// <summary>
        /// Runs the deterministic generator. Same seed in, same case out, on any
        /// machine — the engine owns its own PCG32 PRNG precisely so this holds
        /// across platforms and Unity versions.
        ///
        /// Synchronous on purpose: generation measures in single-digit milliseconds,
        /// and a coroutine would add complexity to hide a cost that is not there.
        /// </summary>
        public static CompleteCaseTruth Generate(ulong seed)
        {
            var stopwatch = Stopwatch.StartNew();

            CaseFile file;
            try
            {
                file = CaseGenerator.Generate(seed);
            }
            catch (Exception e)
            {
                // A throwing generator is a bug in the engine, not a recoverable
                // condition. Surface it loudly rather than handing back a half-case.
                Debug.LogError($"[Case] Generator threw for seed {seed}: {e}");
                return null;
            }

            stopwatch.Stop();

            if (file == null)
            {
                Debug.LogError($"[Case] Generator returned null for seed {seed}.");
                return null;
            }

            var truth = new CompleteCaseTruth(file, seed, stopwatch.Elapsed.TotalMilliseconds);

            if (!truth.IsSolvable)
            {
                // The engine rerolls until the case is pooled-solvable and only gives
                // up after MaxRerolls. Reaching here means this seed is pathological:
                // playable, but nobody could reason their way to the answer.
                Debug.LogWarning($"[Case] Seed {seed} produced an UNSOLVABLE case " +
                                 $"({truth.InferenceDepth} proof facts, need {World.MinProofFacts}). " +
                                 "Pick another seed.");
            }

            Debug.Log($"[Case] Generated seed {seed}: \"{file.Title}\" — " +
                      $"{truth.GenerationAttempts} attempt(s), {truth.InferenceDepth} proof facts, " +
                      $"{truth.GenerationMilliseconds:0.0} ms");

            return truth;
        }

        /// <summary>A fresh unpredictable seed for the RANDOM SEED button.</summary>
        public static ulong RandomSeed()
        {
            var bytes = new byte[8];
            new Random().NextBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }
    }
}
