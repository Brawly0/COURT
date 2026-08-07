namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// What the character is doing right now. PlayerMovement decides this from
    /// real velocity and ground contact; PlayerAnimatorDriver and the debug HUD
    /// only read it. Keeping it an enum (instead of a pile of bools) means there
    /// is exactly one answer to "what is the player doing" at any moment.
    /// </summary>
    public enum MovementState
    {
        Idle,
        Walk,
        Run,
        Sprint,
        Jump,
        Fall,
        Land
    }
}
