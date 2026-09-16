namespace HollowKnightTAS.Core.Ledger
{
    public enum TickPhase : byte
    {
        InControlCommitted = 1,
        VisualUpdateBegin = 2,
        HeroUpdateBeforeOriginal = 3,
        FixedUpdateBegin = 4,
        LateUpdateEnd = 5,
        SceneLoadRequested = 6,
        ActiveSceneChanged = 7,
        HeroHazardDeathRequested = 8,
        HeroHazardDeathEntered = 9,
        HeroHazardRespawnEntered = 10,
        HeroHazardRespawnCompleted = 11,
        ProfileApplied = 12,
        ProfileRestored = 13,
        LedgerGap = 14,
        RngCallObserved = 15
    }
}
