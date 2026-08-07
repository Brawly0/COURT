namespace CaseClosed.Game.Cases.Roles
{
    /// <summary>
    /// A seat at the table. Distinct from the TEAM that seat plays for — the
    /// previous version conflated the two, which made "Prosecution" mean both a
    /// side and a job.
    ///
    /// Values are explicit and never reordered: they cross the wire as bytes, and
    /// renumbering would silently reassign everyone mid-version.
    ///
    /// The commented entries are the agreed extension points. They are NOT
    /// implemented; listing them fixes their numbers now so adding one later cannot
    /// shift the existing ones.
    /// </summary>
    public enum PlayerRole : byte
    {
        Unassigned = 0,

        // Defense side
        Defendant = 1,
        DefenseAttorney = 2,

        // Prosecution side
        Prosecutor = 10,
        Investigator = 11,

        // Reserved, not implemented:
        // Paralegal = 3, ForensicTech = 12, PlayerJudge = 20, Observer = 30
    }

    /// <summary>Which side a seat plays for. Neutral is reserved for a player judge.</summary>
    public enum PlayerTeam : byte
    {
        None = 0,
        Defense = 1,
        Prosecution = 2,
        // Neutral = 3 — reserved for PlayerJudge
    }

    /// <summary>
    /// Static facts about each seat: which side it plays for, what it is trying to
    /// do, and what it can do that others cannot.
    ///
    /// Kept as pure lookups rather than data on the roster, because these never vary
    /// per match — sending them over the network every case would be waste, and
    /// having two copies would let them disagree.
    /// </summary>
    public static class RoleInfo
    {
        public static PlayerTeam TeamOf(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Defendant:
                case PlayerRole.DefenseAttorney:
                    return PlayerTeam.Defense;

                case PlayerRole.Prosecutor:
                case PlayerRole.Investigator:
                    return PlayerTeam.Prosecution;

                default:
                    return PlayerTeam.None;
            }
        }

        /// <summary>Roles there can only ever be one of. Investigator is repeatable.</summary>
        public static bool IsUnique(PlayerRole role) =>
            role == PlayerRole.Defendant ||
            role == PlayerRole.DefenseAttorney ||
            role == PlayerRole.Prosecutor;

        public static string DisplayName(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Defendant: return "Defendant";
                case PlayerRole.DefenseAttorney: return "Defense Attorney";
                case PlayerRole.Prosecutor: return "Prosecutor";
                case PlayerRole.Investigator: return "Investigator";
                default: return "Unassigned";
            }
        }

        /// <summary>What this seat is trying to achieve. Shown on the briefing screen.</summary>
        public static string Objective(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Defendant:
                    return "Survive the trial. You alone know what really happened - " +
                           "telling the truth is optional.";
                case PlayerRole.DefenseAttorney:
                    return "Create reasonable doubt. Your client knows more than they are telling you.";
                case PlayerRole.Prosecutor:
                    return "Prove the defendant did it. Register admissible evidence before the bell.";
                case PlayerRole.Investigator:
                    return "Feed the prosecution. Find facts, process them, get them registered in time.";
                default:
                    return "Wait for the host to deal a case.";
            }
        }

        /// <summary>
        /// What this seat can do that others cannot.
        ///
        /// These describe intent only — none of the abilities are implemented yet,
        /// and the text says so rather than implying a system that does not exist.
        /// </summary>
        public static string Ability(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Defendant:
                    return "You hold your own memory of the day. (Clarity system: not yet implemented.)";
                case PlayerRole.DefenseAttorney:
                    return "You may question your client privately. (Interview: not yet implemented.)";
                case PlayerRole.Prosecutor:
                    return "You register evidence for trial. (Registration: not yet implemented.)";
                case PlayerRole.Investigator:
                    return "You search and process the scene. (Searching: not yet implemented.)";
                default:
                    return "-";
            }
        }

        public static string TeamName(PlayerTeam team)
        {
            switch (team)
            {
                case PlayerTeam.Defense: return "Defense";
                case PlayerTeam.Prosecution: return "Prosecution";
                default: return "None";
            }
        }
    }
}
