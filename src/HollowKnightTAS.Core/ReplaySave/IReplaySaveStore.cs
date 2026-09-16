using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HollowKnightTAS.Core.ReplaySave
{
    public interface IReplayLifecyclePlanStore
    {
        string PublishExecutionPlan(ReplayLifecycleExecutionPlan plan);
        ReplayLifecycleExecutionPlan LoadExecutionPlan(string sha256);
    }

    public interface IReplaySaveStore
    {
        ReplaySaveCommitResult Commit(ReplaySaveCommit commit);
        ReplaySaveLoadResult Load(string replaySaveId);
        ReplaySaveMovieObjectPublishResult PublishMovieObject(
            byte[] canonicalMovieBytes);
        ReplaySaveMovieObjectLoadResult LoadMovieObject(string sha256);
        IReadOnlyList<ReplaySaveDescriptor> List();
        IReadOnlyList<ReplaySaveCatalogEntry> Inspect();
        ReplaySaveRecoveryReport Recover();
        ReplaySaveCommitResult PinAsManual(string replaySaveId);
    }

    public sealed class ReplaySaveMovieObjectPublishResult
    {
        public ReplaySaveMovieObjectPublishResult(
            bool success,
            string sha256,
            string error)
        {
            Success = success;
            Sha256 = sha256 ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Sha256 { get; }
        public string Error { get; }
    }

    public sealed class ReplaySaveMovieObjectLoadResult
    {
        private readonly byte[]? bytes;

        public ReplaySaveMovieObjectLoadResult(
            bool success,
            string sha256,
            byte[]? bytes,
            string error)
        {
            Success = success;
            Sha256 = sha256 ?? string.Empty;
            this.bytes = bytes == null ? null : (byte[])bytes.Clone();
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Sha256 { get; }
        public byte[]? Bytes => bytes == null ? null : (byte[])bytes.Clone();
        public string Error { get; }
    }

    public sealed class ReplaySaveCommitResult
    {
        public ReplaySaveCommitResult(
            bool success,
            ReplaySaveStatus status,
            ReplaySaveDescriptor? descriptor,
            string error)
        {
            Success = success;
            Status = status;
            Descriptor = descriptor;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public ReplaySaveStatus Status { get; }
        public ReplaySaveDescriptor? Descriptor { get; }
        public string Error { get; }
    }

    public sealed class ReplaySaveLoadResult
    {
        public ReplaySaveLoadResult(
            ReplaySaveStatus status,
            ReplaySaveDescriptor? descriptor,
            ReplaySavePackage? package,
            string error)
        {
            Status = status;
            Descriptor = descriptor;
            Package = package;
            Error = error ?? string.Empty;
        }

        public bool Success =>
            Status == ReplaySaveStatus.Ready && Package != null;
        public ReplaySaveStatus Status { get; }
        public ReplaySaveDescriptor? Descriptor { get; }
        public ReplaySavePackage? Package { get; }
        public string Error { get; }
    }

    public sealed class ReplaySaveRecoveryReport
    {
        public ReplaySaveRecoveryReport(
            int visibleEntryCount,
            int readyEntryCount,
            IEnumerable<string> incompleteTransactionIds,
            IEnumerable<string> invalidEntryFiles,
            bool catalogRewritten)
        {
            if (visibleEntryCount < 0 || readyEntryCount < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            VisibleEntryCount = visibleEntryCount;
            ReadyEntryCount = readyEntryCount;
            IncompleteTransactionIds = new ReadOnlyCollection<string>(
                new List<string>(
                    incompleteTransactionIds
                    ?? throw new ArgumentNullException(
                        nameof(incompleteTransactionIds))));
            InvalidEntryFiles = new ReadOnlyCollection<string>(
                new List<string>(
                    invalidEntryFiles
                    ?? throw new ArgumentNullException(
                        nameof(invalidEntryFiles))));
            CatalogRewritten = catalogRewritten;
        }

        public int VisibleEntryCount { get; }
        public int ReadyEntryCount { get; }
        public IReadOnlyList<string> IncompleteTransactionIds { get; }
        public IReadOnlyList<string> InvalidEntryFiles { get; }
        public bool CatalogRewritten { get; }
    }

    public enum ReplaySaveTransactionStage : byte
    {
        TransactionCreated = 1,
        ObjectsPublished = 2,
        DescriptorPrepared = 3,
        EntryPublished = 4,
        RetentionApplied = 5,
        CatalogPublished = 6
    }

    public sealed class ReplaySaveSimulatedCrashException : Exception
    {
        public ReplaySaveSimulatedCrashException(
            ReplaySaveTransactionStage stage)
            : base("Simulated replay-save process termination after " + stage + ".")
        {
            Stage = stage;
        }

        public ReplaySaveTransactionStage Stage { get; }
    }
}
