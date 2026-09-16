namespace HollowKnightTAS.Core.Control
{
    public enum SimulationControlMode : byte
    {
        Running = 0,
        Pausing = 1,
        Paused = 2,
        Stepping = 3,
        Restoring = 4,
        Faulted = 5
    }

    public enum StepBoundary : byte
    {
        MovieTick = 1,
        VisualUpdate = 2
    }
}
