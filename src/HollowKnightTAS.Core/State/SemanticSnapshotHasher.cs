using System;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.State
{
    public static class SemanticSnapshotHasher
    {
        public static string ComputeSha256(SemanticSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return Sha256Utility.ComputeHex(
                SemanticSnapshotCanonicalizer.Serialize(snapshot));
        }
    }
}
