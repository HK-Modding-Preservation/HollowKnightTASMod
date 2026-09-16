namespace HollowKnightTAS.Core.ReplaySave
{
    public enum ReplaySaveStatus : byte
    {
        Pending = 1,
        Ready = 2,
        Restoring = 3,
        Corrupt = 4,
        Incompatible = 5,
        JournalGap = 6,
        RestoreDesync = 7,
        Cancelled = 8,
        Failed = 9
    }
}
