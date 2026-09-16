using System;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Core.Verification;

namespace HollowKnightTAS.Core.ReplaySave
{
    public static class ReplaySaveSemanticVerifier
    {
        public const string ProjectionId =
            VerificationSnapshotNormalizer.ProjectionId;

        public static string ComputeVerificationSha256(
            SemanticSnapshot snapshot)
        {
            var projected = Project(snapshot);
            return SemanticSnapshotHasher.ComputeSha256(projected);
        }

        public static SemanticSnapshot Project(
            SemanticSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var canonical =
                SemanticSnapshotCanonicalizer.Serialize(snapshot);
            var projected =
                VerificationSnapshotNormalizer.Normalize(canonical);
            return SemanticSnapshotCanonicalizer.Deserialize(projected);
        }

        public static ReplaySaveSemanticComparison Compare(
            SemanticSnapshot expected,
            SemanticSnapshot actual)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            var expectedStrict =
                SemanticSnapshotHasher.ComputeSha256(expected);
            var actualStrict =
                SemanticSnapshotHasher.ComputeSha256(actual);
            var expectedVerification =
                ComputeVerificationSha256(expected);
            var actualVerification =
                ComputeVerificationSha256(actual);
            return new ReplaySaveSemanticComparison(
                ProjectionId,
                expectedStrict,
                actualStrict,
                expectedVerification,
                actualVerification);
        }
    }

    public sealed class ReplaySaveSemanticComparison
    {
        internal ReplaySaveSemanticComparison(
            string projectionId,
            string expectedStrictSha256,
            string actualStrictSha256,
            string expectedVerificationSha256,
            string actualVerificationSha256)
        {
            ProjectionId = projectionId;
            ExpectedStrictSha256 = expectedStrictSha256;
            ActualStrictSha256 = actualStrictSha256;
            ExpectedVerificationSha256 = expectedVerificationSha256;
            ActualVerificationSha256 = actualVerificationSha256;
        }

        public string ProjectionId { get; }
        public string ExpectedStrictSha256 { get; }
        public string ActualStrictSha256 { get; }
        public string ExpectedVerificationSha256 { get; }
        public string ActualVerificationSha256 { get; }
        public bool StrictEquivalent => string.Equals(
            ExpectedStrictSha256,
            ActualStrictSha256,
            StringComparison.Ordinal);
        public bool VerificationEquivalent => string.Equals(
            ExpectedVerificationSha256,
            ActualVerificationSha256,
            StringComparison.Ordinal);
    }
}
