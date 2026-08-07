namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// Which way the local player is looking at the world.
    ///
    /// PURELY LOCAL. Nothing about this is replicated: the server does not care
    /// whether you are in first or third person, and no other client can tell.
    /// What remote players see — body yaw, movement, animation, carried evidence —
    /// is driven by the same replicated state in both modes.
    /// </summary>
    public enum CameraMode
    {
        ThirdPerson = 0,
        FirstPerson = 1,
    }
}
