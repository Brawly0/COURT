namespace CaseClosed.Game.Cases
{
    /// <summary>
    /// Where the match is in loading a case. Replicated to everyone — knowing a
    /// case exists is public; knowing what is IN it is not.
    ///
    /// Investigation and Trial deliberately absent: this milestone stops at "a case
    /// is loaded and held", and inventing states we cannot yet enter would be
    /// guessing at systems that do not exist.
    /// </summary>
    public enum CaseLifecycleState : byte
    {
        /// <summary>Nothing generated. Fresh session.</summary>
        NoCase = 0,

        /// <summary>The host is running the generator. Brief — generation is milliseconds.</summary>
        Generating = 1,

        /// <summary>Truth exists on the host and public info has been published.</summary>
        Loaded = 2,

        /// <summary>Every connected client has acknowledged its PlayerCaseView.</summary>
        Ready = 3,
    }
}
