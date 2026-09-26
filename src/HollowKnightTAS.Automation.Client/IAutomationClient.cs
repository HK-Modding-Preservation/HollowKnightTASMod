using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Automation;

namespace HollowKnightTAS.Automation.Client
{
    public interface IAutomationClient : IAsyncDisposable
    {
        Task<AutomationHandshake> ConnectAsync(
            AutomationConnectOptions? options = null,
            CancellationToken cancellationToken = default);

        Task<AutomationStateEnvelope> GetStateAsync(
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetOperationalStatusAsync(
            CancellationToken cancellationToken = default);

        Task<AutomationSemanticState> GetSemanticStateAsync(
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetCombatStateAsync(
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetWorldSnapshotAsync(
            string? snapshotId = null,
            string view = "world",
            bool includeInactive = false,
            int offset = 0,
            int limit = 64,
            CancellationToken cancellationToken = default);

        Task<string> GetWorldSnapshotJsonAsync(
            string? snapshotId = null,
            string view = "world",
            bool includeInactive = false,
            int limit = 64,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetObjectDetailsAsync(
            string objectId,
            long? expectedNativeFrame = null,
            string? detailsId = null,
            int cursor = 0,
            int maxCharacters = 100000,
            CancellationToken cancellationToken = default);

        Task<string> GetObjectDetailsJsonAsync(
            string objectId,
            long? expectedNativeFrame = null,
            int maxCharacters = 100000,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetReplaySavesAsync(
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetMovieAsync(
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> GetLifecycleMovieAsync(CancellationToken cancellationToken = default);
        Task<AutomationResultEnvelope> GetLifecycleMovieChunkAsync(string exportId, int chunkIndex,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> EditLifecycleInputRangeAsync(
            HollowKnightTAS.Core.Movie.TimelineEditKind kind, string baseMovieId, long startTick,
            long deleteCount, byte[]? canonicalReplacementMovieUtf8, CancellationToken cancellationToken = default);

        AutomationCommandEnvelope CreateCommand(
            string commandId,
            string requiredScope,
            IReadOnlyDictionary<string, string>? arguments = null,
            string? leaseId = null,
            string? expectedRuntimeMode = null,
            long? expectedMovieTick = null,
            string? requestId = null,
            string? idempotencyKey = null);

        Task<AutomationResultEnvelope> StepWithInputAsync(
            byte[] canonicalOneTickMovieUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> QueueInputBatchAsync(
            byte[] canonicalInputMovieUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> BeginInputBatchAsync(
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> AppendInputBatchAsync(
            string transactionId,
            int chunkIndex,
            byte[] canonicalInputMovieChunkUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> CommitInputBatchAsync(
            string transactionId,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> CancelInputBatchAsync(
            string transactionId,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> StepAsync(
            int count,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> PauseAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> QuitGameAsync(
            string leaseId, string expectedRuntimeMode, long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> LoadGameSlotAsync(
            int slot, string leaseId, CancellationToken cancellationToken = default);
        Task<AutomationResultEnvelope> ReloadGameSlotAsync(int slot, string leaseId, long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> RestartRecordingSessionAsync(int slot, string leaseId,
            string expectedRuntimeMode, long? expectedMovieTick = null, CancellationToken cancellationToken = default);
        Task<AutomationResultEnvelope> CancelRecordingRestartAsync(string operationId, string leaseId,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ResumeAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> RunUntilAsync(
            long targetMovieTick,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> StartRecordingAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> StopRecordingAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> StartVideoExportAsync(
            string ffmpegPath,
            string outputPath,
            int maximumFrames,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            bool replayLoadedMovie = false,
            CancellationToken cancellationToken = default,
            long? endMovieFrame = null);

        Task<AutomationResultEnvelope> FinishVideoExportAsync(
            string operationId,
            string leaseId,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> CancelVideoExportAsync(
            string operationId,
            string leaseId,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> CreateReplaySaveAsync(
            string label,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> SetAutoSavePolicyAsync(
            bool enabled, long intervalMovieTicks, int retentionCount,
            string leaseId, string expectedRuntimeMode, long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> RestoreReplaySaveAsync(
            string replaySaveId,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> SeekMovieTickAsync(
            long targetMovieTick,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ApplyBranchAndSeekAsync(
            string branchMovieId,
            long targetMovieTick,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ApplyMovieBranchAsync(
            string branchMovieId,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ValidateMoviePatchAsync(
            byte[] canonicalCandidateMovieUtf8,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ProposeMoviePatchAsync(
            string baseMovieId,
            byte[] canonicalCandidateMovieUtf8,
            string expectedMilestone,
            string reason,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ReplaceInputRangeAsync(
            string baseMovieId,
            long startTick,
            long deleteCount,
            byte[] canonicalReplacementMovieUtf8,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> InsertInputRangeAsync(
            string baseMovieId,
            long startTick,
            byte[] canonicalReplacementMovieUtf8,
            CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> DeleteInputRangeAsync(
            string baseMovieId,
            long startTick,
            long count,
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<AutomationResultEnvelope>
            SubscribeTimelineAsync(
                long fromMovieTick = 0,
                int count = 100,
                TimeSpan? pollingInterval = null,
                CancellationToken cancellationToken = default);

        Task<AutomationResultEnvelope> ExecuteAsync(
            AutomationCommandEnvelope command,
            CancellationToken cancellationToken = default);
    }
}
