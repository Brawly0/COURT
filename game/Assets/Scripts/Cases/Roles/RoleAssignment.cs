using System.Collections.Generic;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game.Cases.Roles
{
    /// <summary>
    /// WHY THIS EXISTS: deciding who plays what is a rule, not a behaviour. Keeping
    /// it as plain static functions over plain data means it can be reasoned about
    /// and tested without a NetworkManager, a scene, or a second process running.
    ///
    /// THE DEAL (docs/MAP_DESIGN.md §1):
    ///   exactly one Defendant, who learns the truth alone,
    ///   the rest split between Prosecution and Defense.
    /// The judge is the deterministic AI, never a player.
    ///
    /// Seeded from the CASE seed via the project's own Pcg32 — never System.Random.
    /// Same seed plus same lobby always deals the same table, which is what makes a
    /// case reproducible for replays and bug reports rather than merely re-generated.
    ///
    /// Balance: the extra investigator goes to Prosecution, because Defense has the
    /// Defendant as an extra body and a source the other side cannot question freely.
    ///   4 players -> 1 Def, 2 Pros, 1 Defe
    ///   6 players -> 1 Def, 3 Pros, 2 Defe
    ///   8 players -> 1 Def, 4 Pros, 3 Defe
    /// </summary>
    public static class RoleAssignment
    {
        /// <summary>
        /// Deals the whole table from scratch. Client ids are sorted first so the
        /// result depends on WHO is present, not on the order NGO happened to
        /// report them — connection order varies run to run and would destroy
        /// reproducibility.
        /// </summary>
        /// <summary>
        /// The order seats are handed out as the lobby grows.
        ///
        /// Defendant first because there is no case without one. Then one of each
        /// unique seat, alternating sides so a 3-player game is not one-sided. Any
        /// player beyond the fourth becomes an Investigator, which is the only
        /// repeatable seat until Paralegal and Forensic Tech exist.
        /// </summary>
        private static readonly PlayerRole[] FillOrder =
        {
            PlayerRole.Defendant,        // 1st: the case needs someone on trial
            PlayerRole.Prosecutor,       // 2nd: someone has to bring the charge
            PlayerRole.DefenseAttorney,  // 3rd: someone has to answer it
            PlayerRole.Investigator,     // 4th: the target four-player table
        };

        public static Dictionary<ulong, PlayerRole> Deal(ulong caseSeed, IEnumerable<ulong> clientIds)
        {
            var ids = new List<ulong>(clientIds);
            ids.Sort();

            var result = new Dictionary<ulong, PlayerRole>();
            if (ids.Count == 0) return result;

            // A separate stream from the case generator's, so changing how roles are
            // dealt can never alter the case itself for the same seed.
            var rng = new Pcg32(caseSeed, sequence: 1337u);

            var shuffled = new List<ulong>(ids);
            rng.Shuffle(shuffled);

            for (int i = 0; i < shuffled.Count; i++)
                result[shuffled[i]] = i < FillOrder.Length ? FillOrder[i] : PlayerRole.Investigator;

            return result;
        }

        /// <summary>
        /// Slots ONE late joiner into an existing table without redealing.
        ///
        /// Redealing would be worse than unfair: players already know their role and
        /// the Defendant already knows whether they did it. You cannot take that back.
        /// So a joiner fills the thinnest seat, and never becomes the Defendant — that
        /// seat is dealt once per case and stays dealt (see VacantDefendant).
        /// </summary>
        public static PlayerRole AssignLateJoiner(IEnumerable<PlayerRole> existingRoles)
        {
            var taken = new HashSet<PlayerRole>(existingRoles);

            // Fill an empty unique seat first — a table with no Prosecutor is worse
            // than an unbalanced one. Defendant is skipped deliberately: that seat is
            // dealt once per case and never re-dealt, because whoever held it already
            // learned the answer.
            foreach (var role in FillOrder)
            {
                if (role == PlayerRole.Defendant) continue;
                if (RoleInfo.IsUnique(role) && !taken.Contains(role)) return role;
            }

            return PlayerRole.Investigator;
        }

        /// <summary>
        /// True when the table has nobody on trial — the Defendant disconnected.
        ///
        /// This is NOT auto-repaired. Promoting somebody mid-case would hand a
        /// stranger the answer to the mystery, and the design already has a name for
        /// the alternative: tried in absentia. The decision belongs to the trial
        /// system; this just reports the fact.
        /// </summary>
        public static bool VacantDefendant(IEnumerable<PlayerRole> roles)
        {
            foreach (var role in roles)
                if (role == PlayerRole.Defendant) return false;
            return true;
        }

        /// <summary>
        /// True when every unique seat is filled exactly once. Duplicates would mean
        /// two prosecutors arguing the same case, which the deal must never produce.
        /// </summary>
        public static bool UniqueRolesAreUnique(IEnumerable<PlayerRole> roles)
        {
            var seen = new HashSet<PlayerRole>();
            foreach (var role in roles)
            {
                if (!RoleInfo.IsUnique(role)) continue;
                if (!seen.Add(role)) return false;
            }
            return true;
        }

        /// <summary>Human-readable summary for the debug panel and logs.</summary>
        public static string Describe(IReadOnlyDictionary<ulong, PlayerRole> table)
        {
            var counts = new Dictionary<PlayerRole, int>();
            foreach (var role in table.Values)
            {
                counts.TryGetValue(role, out int n);
                counts[role] = n + 1;
            }

            var parts = new List<string>();
            foreach (var role in new[] { PlayerRole.Defendant, PlayerRole.DefenseAttorney,
                                         PlayerRole.Prosecutor, PlayerRole.Investigator })
            {
                counts.TryGetValue(role, out int n);
                if (n > 0) parts.Add($"{n} {RoleInfo.DisplayName(role)}");
            }

            string body = parts.Count > 0 ? string.Join(", ", parts) : "empty";
            bool hasDefendant = counts.ContainsKey(PlayerRole.Defendant);

            return $"{table.Count} player(s): {body}{(hasDefendant ? "" : "  [NO DEFENDANT]")}";
        }
    }
}
