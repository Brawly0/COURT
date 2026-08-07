using System;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// THE SECRET. Everything about the case, including who actually did it.
    ///
    /// This type is HOST MEMORY ONLY and is built so that it physically cannot
    /// travel over the network:
    ///
    ///   * it is a plain class, not a struct
    ///   * it does NOT implement INetworkSerializable
    ///   * it contains Dictionary, jagged arrays and List, none of which NGO
    ///     can serialise
    ///
    /// So a careless `SomethingClientRpc(truth)` is a COMPILE ERROR, not a silent
    /// leak that only shows up when a player datamines the packet stream. That is
    /// the whole design: make the unsafe thing impossible rather than discouraged.
    ///
    /// Everything clients receive is derived from this by CaseViewFactory.
    /// </summary>
    public sealed class CompleteCaseTruth
    {
        /// <summary>The generator's output, untouched. Perpetrator, occupancy, corruptions, agendas.</summary>
        public readonly CaseFile File;

        /// <summary>Seed this was generated from. Regenerating with it reproduces the case exactly.</summary>
        public readonly ulong Seed;

        /// <summary>Wall-clock time the host generated it.</summary>
        public readonly DateTime GeneratedAtUtc;

        /// <summary>Milliseconds the generator took. Useful for spotting a pathological seed.</summary>
        public readonly double GenerationMilliseconds;

        public CompleteCaseTruth(CaseFile file, ulong seed, double generationMilliseconds)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            Seed = seed;
            GeneratedAtUtc = DateTime.UtcNow;
            GenerationMilliseconds = generationMilliseconds;
        }

        /// <summary>
        /// How many times the generator rejected a case before this one passed its
        /// invariants. High numbers mean the constraints are close to unsatisfiable.
        /// </summary>
        public int GenerationAttempts => File.Rerolls + 1;

        /// <summary>
        /// Did the case pass the pooled-solvable check — at least MinProofFacts
        /// independent facts implicating the true culprit?
        /// </summary>
        public bool IsSolvable => File.ProofFacts.Count >= World.MinProofFacts;

        /// <summary>How many independent facts point at the perpetrator. The solver's depth.</summary>
        public int InferenceDepth => File.ProofFacts.Count;

        /// <summary>Stable fingerprint. Two runs of the same seed must produce the same string.</summary>
        public string Digest() => File.Digest();
    }
}
