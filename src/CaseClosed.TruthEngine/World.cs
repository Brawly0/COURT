using System.Collections.Generic;

namespace CaseClosed.TruthEngine
{
    /// <summary>
    /// Static world tables for the crime site (off-screen; represented only
    /// through evidence — the courthouse is the theater, GDD 06). Content
    /// tables grow per archetype; the pipeline never changes.
    /// </summary>
    public static class World
    {
        public static readonly string[] Locations =
            { "Wedding Hall", "Kitchen", "Parking Lot", "Back Office", "Storage Room" };

        /// <summary>Adjacency = who can overhear/glimpse whom (GDD 06 Stage 2).</summary>
        public static readonly int[][] Adjacency =
        {
            new[] { 1, 2, 3 }, // Wedding Hall
            new[] { 0, 4 },    // Kitchen
            new[] { 0 },       // Parking Lot
            new[] { 0, 4 },    // Back Office
            new[] { 1, 3 },    // Storage Room
        };

        public static readonly string[] Slots = { "8:00", "8:30", "9:00", "9:30", "10:00" };

        /// <summary>Index 0 is always the defendant.</summary>
        public static readonly string[] Cast =
        {
            "Nadia", "Greg the Janitor", "Officer Dowd",
            "Marisol the Secretary", "Sam the Caterer", "Victor the Cousin",
        };

        public static readonly (string Object, string Title)[] CrimeObjects =
        {
            ("the hummus vat", "THE HUMMUS HEIST"),
            ("the prize goat", "THE GOAT AFFAIR"),
            ("the wedding gift box", "THE GIFT BOX JOB"),
            ("the backup generator key", "THE BLACKOUT WEDDING"),
        };

        public static readonly string[] EmbarrassingReasons =
        {
            "crying alone about the seating chart",
            "eating cake straight from the tray",
            "practicing a toast in the mirror",
            "hiding from the groom's mother",
        };

        // ---- tuning constants (GDD canon; TUNE tags live in the GDD) ----
        public const double GuiltPrior = 0.60;
        public const double ClarityLucid = 0.60;
        public const double ClarityHazy = 0.25;  // fractured = remainder (0.15)
        public const double ClearStampRate = 0.60;
        public const double PerpSelfLieChance = 0.60;
        public const int MinProofFacts = 3;      // pooled-solvable floor (Stage 8)
        public const int MaxRerolls = 60;
    }
}
