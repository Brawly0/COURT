namespace CaseClosed.Game.Match
{
    /// <summary>
    /// Where the MATCH is, which is a different question from where the CASE is.
    ///
    /// CaseLifecycleState answers "does a generated case exist and has it been
    /// distributed" — a data question. This answers "what are the humans doing" — a
    /// session question. Keeping them apart means the case can be regenerated
    /// without rewinding the lobby, and the lobby can advance without the case layer
    /// needing to know what a briefing screen is.
    ///
    /// Deliberately stops at PreInvestigationReady. The investigation phase, its
    /// timer and the trial are later milestones.
    /// </summary>
    public enum MatchPhase : byte
    {
        /// <summary>Players connected, nothing dealt. The host may start.</summary>
        LobbyReady = 0,

        /// <summary>Seats being dealt from the case seed.</summary>
        AssigningRoles = 1,

        /// <summary>The truth engine is running.</summary>
        GeneratingCase = 2,

        /// <summary>Private briefings going out, one targeted message per player.</summary>
        DistributingBriefings = 3,

        /// <summary>Briefings delivered; waiting on every player to press Ready.</summary>
        WaitingForPlayers = 4,

        /// <summary>Everyone ready. The investigation has NOT started.</summary>
        PreInvestigationReady = 5,
    }
}
