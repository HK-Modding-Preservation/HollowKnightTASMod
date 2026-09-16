using System;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class SourceSlotApprovalRequiredException : InvalidOperationException
    {
        public SourceSlotApprovalRequiredException(string detail) : base(detail) { }
    }
}
