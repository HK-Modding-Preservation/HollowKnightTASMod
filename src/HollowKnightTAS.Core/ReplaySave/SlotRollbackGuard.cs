using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public static class SlotRollbackGuard
    {
        // Null means absent, not a zero-length file. A retry may encounter one
        // file already restored and another still containing installed bytes.
        public static bool CanRestore(byte[]? current, byte[]? original, byte[]? installed)
            => Equal(current, original) || Equal(current, installed);

        private static bool Equal(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }
    }
}
