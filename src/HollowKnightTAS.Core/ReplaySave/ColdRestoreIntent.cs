using System;

namespace HollowKnightTAS.Core.ReplaySave
{
    public enum ColdRestoreOperationKind : byte
    {
        RestoreReplaySave = 1,
        SeekMovieTick = 2,
        ApplyBranchAndSeek = 3
    }

    public sealed class ColdRestoreBuildFingerprint :
        IEquatable<ColdRestoreBuildFingerprint>
    {
        public ColdRestoreBuildFingerprint(
            string environmentManifestSha256,
            string gameExecutableSha256,
            string unityPlayerSha256,
            string assemblyCSharpSha256,
            string runtimeAssemblySha256,
            string companionAssemblySha256,
            string startupProfileSha256,
            string observerAssemblySha256)
        {
            EnvironmentManifestSha256 = RequireSha256(
                environmentManifestSha256,
                nameof(environmentManifestSha256));
            GameExecutableSha256 = RequireSha256(
                gameExecutableSha256,
                nameof(gameExecutableSha256));
            UnityPlayerSha256 = RequireSha256(
                unityPlayerSha256,
                nameof(unityPlayerSha256));
            AssemblyCSharpSha256 = RequireSha256(
                assemblyCSharpSha256,
                nameof(assemblyCSharpSha256));
            RuntimeAssemblySha256 = RequireSha256(
                runtimeAssemblySha256,
                nameof(runtimeAssemblySha256));
            CompanionAssemblySha256 = RequireSha256(
                companionAssemblySha256,
                nameof(companionAssemblySha256));
            StartupProfileSha256 = RequireSha256(
                startupProfileSha256,
                nameof(startupProfileSha256));
            ObserverAssemblySha256 = RequireOptionalSha256(
                observerAssemblySha256,
                nameof(observerAssemblySha256));
        }

        public string EnvironmentManifestSha256 { get; }
        public string GameExecutableSha256 { get; }
        public string UnityPlayerSha256 { get; }
        public string AssemblyCSharpSha256 { get; }
        public string RuntimeAssemblySha256 { get; }
        public string CompanionAssemblySha256 { get; }
        public string StartupProfileSha256 { get; }
        public string ObserverAssemblySha256 { get; }

        public bool Equals(ColdRestoreBuildFingerprint? other)
        {
            return other != null
                   && string.Equals(
                       EnvironmentManifestSha256,
                       other.EnvironmentManifestSha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       GameExecutableSha256,
                       other.GameExecutableSha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       UnityPlayerSha256,
                       other.UnityPlayerSha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       AssemblyCSharpSha256,
                       other.AssemblyCSharpSha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       RuntimeAssemblySha256,
                       other.RuntimeAssemblySha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       CompanionAssemblySha256,
                       other.CompanionAssemblySha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       StartupProfileSha256,
                       other.StartupProfileSha256,
                       StringComparison.Ordinal)
                   && string.Equals(
                       ObserverAssemblySha256,
                       other.ObserverAssemblySha256,
                       StringComparison.Ordinal);
        }

        public override bool Equals(object? value)
        {
            return Equals(value as ColdRestoreBuildFingerprint);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    EnvironmentManifestSha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    GameExecutableSha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    UnityPlayerSha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    AssemblyCSharpSha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    RuntimeAssemblySha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    CompanionAssemblySha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    StartupProfileSha256);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    ObserverAssemblySha256);
                return hash;
            }
        }

        private static string RequireSha256(string value, string name)
        {
            return ReplaySaveDescriptor.RequireSha256(value, name);
        }

        private static string RequireOptionalSha256(
            string value,
            string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return RequireSha256(value, name);
        }
    }

    public sealed class ColdRestoreIntent
    {
        public const int CurrentSchemaVersion = 5;
        public const int MaximumIdentifierLength = 128;

        public ColdRestoreIntent(
            int schemaVersion,
            string intentId,
            string operationId,
            ColdRestoreOperationKind operationKind,
            string replaySaveId,
            string sourceSessionId,
            int sourceProcessId,
            DateTimeOffset sourceProcessStartedAtUtc,
            DateTimeOffset createdAtUtc,
            DateTimeOffset expiresAtUtc,
            long sourceCommittedMovieTick,
            int sourceSceneEpoch,
            string baselineObjectSha256,
            string sourceMovieObjectSha256,
            string targetMovieObjectSha256,
            string prefixSha256,
            string journalHeadSha256,
            long targetMovieTick,
            string targetSemanticSha256,
            string parentOperationId,
            string requesterSurface,
            ColdRestoreBuildFingerprint buildFingerprint,
            string lifecyclePlanObjectSha256 = "", string slotOverwriteAuthorization = "")
        {
            if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (!Enum.IsDefined(typeof(ColdRestoreOperationKind), operationKind))
            {
                throw new ArgumentOutOfRangeException(nameof(operationKind));
            }

            SchemaVersion = schemaVersion;
            IntentId = RequireIdentifier(intentId, nameof(intentId));
            OperationId = RequireIdentifier(operationId, nameof(operationId));
            OperationKind = operationKind;
            if (!string.IsNullOrEmpty(lifecyclePlanObjectSha256)
                && (schemaVersion < 3 || operationKind != ColdRestoreOperationKind.ApplyBranchAndSeek))
                throw new ArgumentException("Lifecycle plans require a version 3 branch seek intent.", nameof(lifecyclePlanObjectSha256));
            LifecyclePlanObjectSha256 = string.IsNullOrEmpty(lifecyclePlanObjectSha256) ? string.Empty
                : RequireSha256(lifecyclePlanObjectSha256, nameof(lifecyclePlanObjectSha256));
            IsRootPlanSource = schemaVersion >= 4 && replaySaveId == string.Empty;
            if (IsRootPlanSource && (operationKind != ColdRestoreOperationKind.ApplyBranchAndSeek
                || LifecyclePlanObjectSha256.Length == 0 || journalHeadSha256 != string.Empty))
                throw new ArgumentException("A root-plan source requires a branch plan and no checkpoint journal.");
            ReplaySaveId = IsRootPlanSource ? string.Empty : RequireIdentifier(replaySaveId, nameof(replaySaveId));
            SourceSessionId = RequireIdentifier(
                sourceSessionId,
                nameof(sourceSessionId));
            if (sourceProcessId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceProcessId));
            }

            SourceProcessId = sourceProcessId;
            SourceProcessStartedAtUtc = sourceProcessStartedAtUtc
                .ToUniversalTime();
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
            ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
            if (SourceProcessStartedAtUtc > CreatedAtUtc)
            {
                throw new ArgumentException(
                    "Source process start cannot follow intent creation.",
                    nameof(sourceProcessStartedAtUtc));
            }

            if (ExpiresAtUtc <= CreatedAtUtc)
            {
                throw new ArgumentException(
                    "Intent expiry must follow creation.",
                    nameof(expiresAtUtc));
            }

            if (sourceCommittedMovieTick < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceCommittedMovieTick));
            }

            if (sourceSceneEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceSceneEpoch));
            }

            if (targetMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetMovieTick));
            }

            SourceCommittedMovieTick = sourceCommittedMovieTick;
            SourceSceneEpoch = sourceSceneEpoch;
            BaselineObjectSha256 = RequireSha256(
                baselineObjectSha256,
                nameof(baselineObjectSha256));
            // Schema 2 explicitly represents a title-menu source with no journal.
            // Never substitute the target movie for a nonexistent source recording.
            if (schemaVersion >= 2 && sourceMovieObjectSha256 == string.Empty)
            {
                if (operationKind != ColdRestoreOperationKind.RestoreReplaySave
                    || sourceCommittedMovieTick != -1)
                    throw new ArgumentException("A menu source is only valid for saved restore before any source movie tick.");
                SourceMovieObjectSha256 = string.Empty;
            }
            else
            {
                SourceMovieObjectSha256 = RequireSha256(
                    sourceMovieObjectSha256, nameof(sourceMovieObjectSha256));
            }
            TargetMovieObjectSha256 = RequireSha256(
                targetMovieObjectSha256,
                nameof(targetMovieObjectSha256));
            PrefixSha256 = RequireSha256(prefixSha256, nameof(prefixSha256));
            JournalHeadSha256 = IsRootPlanSource ? string.Empty : RequireSha256(journalHeadSha256, nameof(journalHeadSha256));
            TargetMovieTick = targetMovieTick;
            if (operationKind == ColdRestoreOperationKind.RestoreReplaySave)
            {
                TargetSemanticSha256 = RequireSha256(
                    targetSemanticSha256,
                    nameof(targetSemanticSha256));
            }
            else
            {
                if (!string.IsNullOrEmpty(targetSemanticSha256))
                {
                    throw new ArgumentException(
                        "Seek and branch intents derive a new target state and must not claim a pre-existing semantic hash.",
                        nameof(targetSemanticSha256));
                }

                TargetSemanticSha256 = string.Empty;
            }
            ParentOperationId = RequireOptionalIdentifier(
                parentOperationId,
                nameof(parentOperationId));
            RequesterSurface = RequireIdentifier(
                requesterSurface,
                nameof(requesterSurface));
            BuildFingerprint = buildFingerprint
                               ?? throw new ArgumentNullException(
                                   nameof(buildFingerprint));
            SlotOverwriteAuthorization = slotOverwriteAuthorization ?? throw new ArgumentNullException(nameof(slotOverwriteAuthorization));
            if (SlotOverwriteAuthorization.Length != 0)
            {
                if (schemaVersion < 5) throw new ArgumentException("Slot consent requires intent version 5.");
                var authorization = ReplaySlotOverwriteAuthorization.Deserialize(SlotOverwriteAuthorization);
                if (authorization.BaselineSha256 != BaselineObjectSha256
                    || authorization.MovieSha256 != TargetMovieObjectSha256 || authorization.TargetTick != TargetMovieTick
                    || (LifecyclePlanObjectSha256.Length != 0 && authorization.LifecycleSha256 != LifecyclePlanObjectSha256))
                    throw new ArgumentException("Slot consent is not bound to the requested execution.");
            }
        }

        public int SchemaVersion { get; }
        public string IntentId { get; }
        public string OperationId { get; }
        public ColdRestoreOperationKind OperationKind { get; }
        public string ReplaySaveId { get; }
        public bool IsRootPlanSource { get; }
        public string SourceSessionId { get; }
        public int SourceProcessId { get; }
        public DateTimeOffset SourceProcessStartedAtUtc { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public long SourceCommittedMovieTick { get; }
        public int SourceSceneEpoch { get; }
        public string BaselineObjectSha256 { get; }
        public string SourceMovieObjectSha256 { get; }
        public bool IsMenuSource => SchemaVersion >= 2 && SourceMovieObjectSha256.Length == 0;
        public string TargetMovieObjectSha256 { get; }
        public string LifecyclePlanObjectSha256 { get; }
        public string SlotOverwriteAuthorization { get; }
        public string PrefixSha256 { get; }
        public string JournalHeadSha256 { get; }
        public long TargetMovieTick { get; }
        public string TargetSemanticSha256 { get; }
        public bool RequiresExactTargetSemantic =>
            OperationKind == ColdRestoreOperationKind.RestoreReplaySave;
        public string ParentOperationId { get; }
        public string RequesterSurface { get; }
        public ColdRestoreBuildFingerprint BuildFingerprint { get; }

        public bool IsExpired(DateTimeOffset nowUtc)
        {
            return nowUtc.ToUniversalTime() >= ExpiresAtUtc;
        }

        public static string RequireIdentifier(string value, string name)
        {
            if (value == null
                || value.Length > MaximumIdentifierLength)
            {
                throw new ArgumentException(
                    "A bounded ASCII identifier is required.",
                    name);
            }

            return ReplaySaveDescriptor.RequireIdentifier(value, name);
        }

        public static string RequireOptionalIdentifier(
            string value,
            string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return RequireIdentifier(value, name);
        }

        private static string RequireSha256(string value, string name)
        {
            return ReplaySaveDescriptor.RequireSha256(value, name);
        }
    }
}
