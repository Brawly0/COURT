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
    /// Runs to CourtroomReady. The trial itself is a later milestone — this enum
    /// deliberately stops at "everyone is standing in the courtroom", because what
    /// happens next is a different system's question.
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

        /// <summary>"CASE BEGINS IN 3…" Movement and interaction still held.</summary>
        InvestigationCountdown = 6,

        /// <summary>
        /// The phase the whole game exists for. The server's end timestamp is
        /// running; every investigation interaction is legal only here.
        /// </summary>
        Investigation = 7,

        /// <summary>
        /// Time is up. New actions are refused and held ones are cancelled, but the
        /// world is still standing and players can walk. A distinct state rather than
        /// jumping straight to the transition, so "no new searches" and "go to court"
        /// are two separate facts the HUD can explain one at a time.
        /// </summary>
        InvestigationEnding = 8,

        /// <summary>Walking to the courtroom, on a clock. Stragglers get moved.</summary>
        CourtroomTransition = 9,

        /// <summary>Everyone is in position. The trial system takes over from here.</summary>
        CourtroomReady = 10,
    }
}
