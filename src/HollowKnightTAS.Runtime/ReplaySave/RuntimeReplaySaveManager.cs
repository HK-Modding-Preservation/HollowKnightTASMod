using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GlobalEnums;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;
using HollowKnightTAS.Runtime.Control;
using HollowKnightTAS.Runtime.Companion;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class ReplaySaveOperationResult
    {
        public ReplaySaveOperationResult(
            string requestId,
            ReplaySaveStatus status,
            string replaySaveId,
            long effectiveMovieTick,
            string error)
        {
            RequestId = requestId ?? string.Empty;
            Status = status;
            ReplaySaveId = replaySaveId ?? string.Empty;
            EffectiveMovieTick = effectiveMovieTick;
            Error = error ?? string.Empty;
        }

        public string RequestId { get; }
        public ReplaySaveStatus Status { get; }
        public string ReplaySaveId { get; }
        public long EffectiveMovieTick { get; }
        public string Error { get; }
    }

    public sealed class ReplaySaveSemanticInspection
    {
        public ReplaySaveSemanticInspection(
            ReplaySaveStatus status,
            SemanticSnapshot? snapshot,
            string detail)
        {
            Status = status;
            Snapshot = snapshot;
            Detail = detail ?? string.Empty;
        }

        public bool Success =>
            Status == ReplaySaveStatus.Ready && Snapshot != null;
        public ReplaySaveStatus Status { get; }
        public SemanticSnapshot? Snapshot { get; }
        public string Detail { get; }
    }

    public sealed class ReplaySaveSeekSelectionResult
    {
        public ReplaySaveSeekSelectionResult(
            bool success,
            string code,
            string detail,
            ReplaySaveDescriptor? descriptor,
            long targetMovieTick)
        {
            Success = success;
            Code = code;
            Detail = detail;
            Descriptor = descriptor;
            TargetMovieTick = targetMovieTick;
        }

        public bool Success { get; }
        public string Code { get; }
        public string Detail { get; }
        public ReplaySaveDescriptor? Descriptor { get; }
        public long TargetMovieTick { get; }
        public long EstimatedTailTicks => Descriptor == null
            ? -1
            : TargetMovieTick - Descriptor.EffectiveMovieTick;
    }

    public sealed class RuntimeReplaySaveManager : IDisposable
    {
        private readonly RuntimeReplayJournal journal;
        private readonly IReplaySaveStore store;
        private readonly RuntimeReplaySaveCapture captureBuilder;
        private readonly RuntimeSnapshotCapture snapshotCapture =
            ReplaySaveSnapshotCapture.CreateTarget();
        private readonly ReplaySaveScheduler scheduler;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly RuntimeReplayRestoreCoordinator restoreCoordinator;
        private readonly List<ReplaySaveOperationResult> operations =
            new List<ReplaySaveOperationResult>();
        private readonly Queue<PreparedReplaySaveCommit> preparedCommits =
            new Queue<PreparedReplaySaveCommit>();
        private RuntimeReplaySaveRunner? runner;
        private Task<ReplaySaveCommitResult>? commitTask;
        private PreparedReplaySaveCommit? inFlightCommit;
        private bool started;
        private bool disposed;

        public RuntimeReplaySaveManager(
            RuntimeReplayJournal journal,
            IReplaySaveStore store,
            RuntimeReplaySaveCapture captureBuilder,
            AutoSavePolicy initialPolicy,
            RuntimeReplayRestoreCoordinator restoreCoordinator,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError)
        {
            this.journal = journal
                           ?? throw new ArgumentNullException(nameof(journal));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.captureBuilder = captureBuilder
                                  ?? throw new ArgumentNullException(
                                      nameof(captureBuilder));
            scheduler = new ReplaySaveScheduler(
                initialPolicy
                ?? throw new ArgumentNullException(nameof(initialPolicy)));
            this.restoreCoordinator = restoreCoordinator
                                      ?? throw new ArgumentNullException(
                                          nameof(restoreCoordinator));
            this.logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
            this.logWarning = logWarning
                              ?? throw new ArgumentNullException(nameof(logWarning));
            this.logError = logError ?? throw new ArgumentNullException(nameof(logError));
        }

        public AutoSavePolicy AutoSavePolicy => scheduler.Policy;
        public string RecordingOriginStatus => journal.RecordingOriginStatus;
        public string RecordingOriginDetail => journal.RecordingOriginDetail;
        public bool IsRestoreActive => restoreCoordinator.IsActive;

        public int PendingCount =>
            scheduler.PendingCount
            + preparedCommits.Count
            + (commitTask == null ? 0 : 1);
        public ReplaySaveOperationResult? LastOperation =>
            operations.Count == 0 ? null : operations[operations.Count - 1];
        public IReadOnlyList<ReplaySaveOperationResult> Operations =>
            new ReadOnlyCollection<ReplaySaveOperationResult>(
                new List<ReplaySaveOperationResult>(operations));
        public bool RuntimePumpActive => runner != null;
        public bool RestoreRuntimePumpActive =>
            restoreCoordinator.RuntimePumpActive;

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            store.Recover();
            journal.MovieTickCommitted += OnMovieTickCommitted;
            journal.BaselineEstablished += OnBaselineEstablished;
        }

        public ReplaySaveRequestResult RequestManualSave(string label)
        {
            ThrowIfDisposed();
            if (restoreCoordinator.IsActive)
            {
                return new ReplaySaveRequestResult(
                    false,
                    string.Empty,
                    ReplaySaveStatus.Failed,
                    "Cannot create a replay save while a restore is active.");
            }

            var result = scheduler.Request(
                string.IsNullOrWhiteSpace(label)
                    ? "Manual "
                      + DateTimeOffset.Now.ToString(
                          "yyyy-MM-dd HH:mm:ss",
                          CultureInfo.InvariantCulture)
                    : label,
                ReplaySaveReason.Manual,
                DateTimeOffset.UtcNow,
                requireSubsequentCommittedTick:
                    !IsSafeGameplayTick());
            if (result.Accepted)
            {
                EnsureRuntimePump();
            }

            return result;
        }

        public ReplaySaveRequestResult RequestMovieCheckpointSave(
            string label)
        {
            ThrowIfDisposed();
            if (restoreCoordinator.IsActive)
            {
                return new ReplaySaveRequestResult(
                    false,
                    string.Empty,
                    ReplaySaveStatus.Failed,
                    "Cannot create a replay save while a restore is active.");
            }

            var result = scheduler.Request(
                label,
                ReplaySaveReason.MovieCheckpointCommand,
                DateTimeOffset.UtcNow,
                requireSubsequentCommittedTick:
                    !IsSafeGameplayTick());
            if (result.Accepted)
            {
                EnsureRuntimePump();
            }

            return result;
        }

        internal event Action<AutoSavePolicy>? AutoSavePolicyChanged;

        public AutoSavePolicyResult SetAutoSavePolicy(AutoSavePolicy policy)
        {
            ThrowIfDisposed();
            var result = scheduler.SetPolicy(policy, journal.LastCommittedMovieTick);
            if (result.Success)
                AutoSavePolicyChanged?.Invoke(result.Policy);
            return result;
        }

        public IReadOnlyList<ReplaySaveDescriptor> List()
        {
            ThrowIfDisposed();
            return store.List();
        }

        public IReadOnlyList<ReplaySaveCatalogEntry> Inspect()
        {
            ThrowIfDisposed();
            return store.Inspect();
        }

        public ReplaySaveSeekSelectionResult FindNearestCompatible(
            MovieDocument targetMovie,
            long targetMovieTick,
            bool baselineOnly = false,
            string? exactBaselineObjectSha256 = null)
        {
            ThrowIfDisposed();
            if (targetMovie == null)
            {
                throw new ArgumentNullException(nameof(targetMovie));
            }
            if (exactBaselineObjectSha256 != null && !MovieProtocolV1.IsLowerSha256(exactBaselineObjectSha256))
                throw new ArgumentException("Expected an exact baseline object SHA-256.", nameof(exactBaselineObjectSha256));

            var expandedTicks = targetMovie.Commands
                .OfType<FrameRunCommand>()
                .Aggregate(
                    0L,
                    (total, command) => checked(
                        total + command.FrameCount));
            if (targetMovieTick < 0
                || targetMovieTick >= expandedTicks)
            {
                return new ReplaySaveSeekSelectionResult(
                    false,
                    "TargetOutOfRange",
                    "Seek target must identify an existing input tick in the loaded movie.",
                    null,
                    targetMovieTick);
            }

            foreach (var entry in store.Inspect()
                         .Where(
                             value => value.Status
                                      == ReplaySaveStatus.Ready
                                      && (exactBaselineObjectSha256 == null
                                          || value.Descriptor.BaselineObjectSha256 == exactBaselineObjectSha256)
                                      && (baselineOnly || value.Descriptor.EffectiveMovieTick
                                      <= targetMovieTick))
                         .OrderByDescending(
                             value => value.Descriptor.EffectiveMovieTick)
                         .ThenByDescending(
                             value => value.Descriptor.CreatedAtUtc))
            {
                var load = store.Load(entry.Descriptor.ReplaySaveId);
                if (!load.Success || load.Package == null)
                {
                    continue;
                }

                var prefixTicks = baselineOnly ? 0 : checked(
                    entry.Descriptor.EffectiveMovieTick + 1);
                try
                {
                    if (!MoviePrefixIdentity.IsCompatible(
                            load.Package.Movie,
                            targetMovie,
                            prefixTicks))
                    {
                        continue;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                return new ReplaySaveSeekSelectionResult(
                    true,
                    baselineOnly ? "CompatibleBaselineSelected" : "CompatibleSaveSelected",
                    baselineOnly
                        ? "Selected a matching persisted baseline; the edited input timeline will replay from its root."
                        : "Selected the nearest ready replay checkpoint with an exact canonical movie prefix.",
                    entry.Descriptor,
                    targetMovieTick);
            }

            return new ReplaySaveSeekSelectionResult(
                false,
                baselineOnly ? "NoCompatibleBaseline" : "NoCompatibleReplaySave",
                baselineOnly
                    ? "No ready replay save carries this movie's exact baseline and environment. Save the source baseline before seeking."
                    : "No ready replay checkpoint at or before the target has an exact canonical movie prefix.",
                null,
                targetMovieTick);
        }

        public ReplaySaveSemanticInspection InspectSemanticState(
            string replaySaveId)
        {
            ThrowIfDisposed();
            var load = store.Load(replaySaveId);
            return new ReplaySaveSemanticInspection(
                load.Status,
                load.Package?.TargetSnapshot,
                load.Error);
        }

        public ReplaySaveCommitResult PinAsManual(string replaySaveId)
        {
            ThrowIfDisposed();
            return store.PinAsManual(replaySaveId);
        }

        public bool HasPendingSourceSlotApproval => restoreCoordinator.HasPendingSourceSlotApproval;
        public string SourceSlotApprovalDetail => restoreCoordinator.SourceSlotApprovalDetail;
        public string ResolveSourceSlotApproval(bool approved) => restoreCoordinator.ResolveSourceSlotApproval(approved);

        public ColdRestoreIntent PrepareColdRestoreIntent(
            ColdRestoreOperationKind operationKind,
            string replaySaveId,
            MovieDocument? requestedTargetMovie,
            long requestedTargetMovieTick,
            string intentId,
            string operationId,
            string sourceSessionId,
            int sourceProcessId,
            DateTimeOffset sourceProcessStartedAtUtc,
            string requesterSurface,
            ColdRestoreBuildFingerprint buildFingerprint,
            DateTimeOffset createdAtUtc,
            DateTimeOffset expiresAtUtc,
            bool menuSource = false,
            ReplayLifecycleExecutionPlan? requestedLifecyclePlan = null)
        {
            ThrowIfDisposed();
            if (restoreCoordinator.IsActive || PendingCount != 0)
            {
                throw new InvalidOperationException(
                    "Cold-restore intent requires idle save/restore storage.");
            }

            if (menuSource && (operationKind != ColdRestoreOperationKind.RestoreReplaySave
                || journal.LastCommittedMovieTick >= 0))
                throw new InvalidOperationException("Menu restore must precede any source gameplay recording.");

            if (!menuSource && (!journal.IsAvailable
                || journal.LastCommittedMovieTick < 0))
            {
                throw new InvalidOperationException(
                    "Cold-restore intent requires a complete committed input journal.");
            }

            if (!Enum.IsDefined(
                    typeof(ColdRestoreOperationKind),
                    operationKind))
            {
                throw new ArgumentOutOfRangeException(nameof(operationKind));
            }

            ReplaySaveLoadResult? load = null;
            requestedLifecyclePlan?.ValidateSeek(buildFingerprint.EnvironmentManifestSha256, requestedTargetMovieTick);
            MovieDocument targetMovie;
            long targetMovieTick;
            if (operationKind
                == ColdRestoreOperationKind.RestoreReplaySave)
            {
                load = store.Load(replaySaveId);
                if (!load.Success || load.Package == null)
                {
                    throw new InvalidOperationException(
                        "Replay-save validation failed before cold restore: "
                        + load.Error);
                }

                targetMovie = load.Package.Movie;
                targetMovieTick =
                    load.Package.Descriptor.EffectiveMovieTick;
            }
            else if (requestedLifecyclePlan != null)
            {
                targetMovie = requestedLifecyclePlan.Movie;
                targetMovieTick = requestedTargetMovieTick;
            }
            else
            {
                targetMovie = requestedTargetMovie
                              ?? throw new ArgumentNullException(
                                  nameof(requestedTargetMovie));
                var selection = FindNearestCompatible(
                    targetMovie,
                    requestedTargetMovieTick,
                    baselineOnly: true,
                    exactBaselineObjectSha256: requestedLifecyclePlan?.BaselineObjectSha256);
                if (!selection.Success || selection.Descriptor == null)
                {
                    throw new InvalidOperationException(
                        selection.Code + ": " + selection.Detail);
                }

                replaySaveId = selection.Descriptor.ReplaySaveId;
                load = store.Load(replaySaveId);
                if (!load.Success || load.Package == null)
                {
                    throw new InvalidOperationException(
                        "Selected replay-save validation failed before cold seek: "
                        + load.Error);
                }

                targetMovieTick = requestedTargetMovieTick;
            }

            var target = load?.Package;
            var descriptor = target?.Descriptor;
            var rootBaseline = requestedLifecyclePlan?.ValidateSeek(buildFingerprint.EnvironmentManifestSha256, targetMovieTick)
                ?? target!.Baseline;
            var rootHash = requestedLifecyclePlan?.BaselineObjectSha256 ?? descriptor!.BaselineObjectSha256;
            string lifecyclePlanHash = string.Empty;
            if (requestedLifecyclePlan != null)
            {
                if (operationKind != ColdRestoreOperationKind.ApplyBranchAndSeek
                    || new MovieCanonicalWriter().ComputeMovieId(requestedLifecyclePlan.Movie)
                        != new MovieCanonicalWriter().ComputeMovieId(targetMovie))
                    throw new InvalidOperationException("Lifecycle plan does not match the selected branch movie and exact root baseline.");
                if (!(store is IReplayLifecyclePlanStore planStore))
                    throw new InvalidOperationException("Lifecycle plan storage is unavailable.");
                lifecyclePlanHash = planStore.PublishExecutionPlan(requestedLifecyclePlan);
            }
            if (!string.Equals(targetMovie.Header.ManifestSha256,
                buildFingerprint.EnvironmentManifestSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The selected replay save belongs to a different execution build.");
            var movieWriter = new MovieCanonicalWriter();
            restoreCoordinator.ValidateColdBaselineBeforeHandoff(rootBaseline);
            var lifecycleConsent = string.Empty;
            var lifecycleRecords = requestedLifecyclePlan?.Requests ?? target?.Lifecycle?.Records;
            if (lifecycleRecords != null)
                lifecycleConsent = restoreCoordinator.ValidateColdLifecycleBeforeHandoff(rootBaseline, rootHash,
                    movieWriter.ComputeMovieId(targetMovie), lifecyclePlanHash.Length != 0 ? lifecyclePlanHash : descriptor!.LifecycleObjectSha256!,
                    targetMovieTick, lifecycleRecords, requestedLifecyclePlan?.CopySlotObjects() ?? target!.Objects);
            var sourceTick = -1L;
            var sourceSha256 = string.Empty;
            if (!menuSource)
            {
                var frozen = journal.FreezeForReplaySave(journal.LastCommittedMovieTick);
                if (!frozen.Success)
                    throw new InvalidOperationException("Source journal could not be sealed: " + frozen.Error);
                if (frozen.Lifecycle != null)
                {
                    // The intent's source movie is an identity/audit input,
                    // not the source restoration package. Preserve the full
                    // source checkpoint before allowing its process to exit.
                    var stamp = journal.LastCommittedStamp
                        ?? throw new InvalidOperationException("Source committed stamp is unavailable.");
                    var sourceCapture = snapshotCapture.Capture(new TickStamp(stamp.InputTick,
                        journal.CurrentVisualTick, journal.CurrentFixedTick, journal.CurrentSceneEpoch,
                        TickPhase.LateUpdateEnd));
                    if (!sourceCapture.Success)
                        throw new InvalidOperationException("Source checkpoint capture failed: " + sourceCapture.Error);
                    var sourceRequest = new ReplaySaveRequest("request-" + operationId + "-source",
                        "Before cold restore: " + operationId, ReplaySaveReason.MovieCheckpointCommand,
                        createdAtUtc, frozen.EffectiveMovieTick);
                    var preserved = store.Commit(captureBuilder.BuildCommit(sourceRequest, frozen,
                        sourceCapture, 0, createdAtUtc));
                    if (!preserved.Success)
                        throw new InvalidOperationException("Full source checkpoint could not be preserved: " + preserved.Error);
                }
                var sourceMovie = captureBuilder.BuildMovie(frozen, operationId + "-source.hktas");
                var sourceObject = store.PublishMovieObject(movieWriter.WriteUtf8(sourceMovie));
                if (!sourceObject.Success)
                    throw new InvalidOperationException("Source movie object could not be persisted: " + sourceObject.Error);
                sourceTick = frozen.EffectiveMovieTick;
                sourceSha256 = sourceObject.Sha256;
            }

            var targetObject = store.PublishMovieObject(
                movieWriter.WriteUtf8(targetMovie));
            if (!targetObject.Success)
            {
                throw new InvalidOperationException(
                    "Target movie object could not be persisted: "
                    + targetObject.Error);
            }

            return new ColdRestoreIntent(
                ColdRestoreIntent.CurrentSchemaVersion,
                intentId,
                operationId,
                operationKind,
                descriptor?.ReplaySaveId ?? string.Empty,
                sourceSessionId,
                sourceProcessId,
                sourceProcessStartedAtUtc,
                createdAtUtc,
                expiresAtUtc,
                sourceTick,
                journal.CurrentSceneEpoch,
                rootHash,
                sourceSha256,
                targetObject.Sha256,
                MoviePrefixIdentity.ComputeSha256(
                    targetMovie,
                    requestedLifecyclePlan != null ? 0 : ColdReplayPrefixPolicy.RequiredTicks(operationKind, descriptor!.EffectiveMovieTick)),
                descriptor?.JournalHeadSha256 ?? string.Empty,
                targetMovieTick,
                operationKind
                == ColdRestoreOperationKind.RestoreReplaySave
                    ? descriptor!.SemanticSnapshotSha256
                    : string.Empty,
                string.Empty,
                requesterSurface,
                buildFingerprint, lifecyclePlanHash, lifecycleConsent);
        }

        public ReplayRestoreHandle BeginRestore(string replaySaveId)
        {
            ThrowIfDisposed();
            if (PendingCount != 0)
            {
                throw new InvalidOperationException(
                    "Wait for pending replay-save commits before restoring.");
            }

            return restoreCoordinator.Begin(replaySaveId);
        }

        public ReplayRestoreHandle BeginColdRestore(
            ColdRestoreIntent intent,
            ColdRestoreBuildFingerprint actualBuild,
            DateTimeOffset nowUtc)
        {
            ThrowIfDisposed();
            if (PendingCount != 0)
            {
                throw new InvalidOperationException(
                    "Wait for pending replay-save commits before restoring.");
            }

            return restoreCoordinator.BeginCold(
                intent,
                actualBuild,
                nowUtc);
        }

        public void ConfigureColdBoundaryCommandPump(Action commandPump)
        {
            ThrowIfDisposed();
            restoreCoordinator.ConfigureColdBoundaryCommandPump(commandPump);
        }

        public void ConfigureStartupProfileAttestor(
            RuntimeStartupProfileAttestor attestor)
        {
            ThrowIfDisposed();
            restoreCoordinator.ConfigureStartupProfileAttestor(attestor);
        }

        public void ConfigureColdTargetHandoff(
            Func<
                RuntimePauseController,
                RestoreSettingsLease,
                MovieDocument,
                HollowKnightTAS.Core.Control.ControlResult> handoff)
        {
            ThrowIfDisposed();
            restoreCoordinator.ConfigureColdTargetHandoff(handoff);
        }

        public ReplayRestoreProgress Poll(ReplayRestoreHandle handle)
        {
            ThrowIfDisposed();
            return restoreCoordinator.Poll(handle);
        }

        public ReplayRestoreResult ApproveRestoreOverwrite(
            ReplayRestoreHandle handle,
            bool approved)
        {
            ThrowIfDisposed();
            return restoreCoordinator.ApproveOverwrite(handle, approved);
        }

        public ReplayRestoreResult CancelRestore(ReplayRestoreHandle handle)
        {
            ThrowIfDisposed();
            return restoreCoordinator.Cancel(handle);
        }

        public ReplayRestoreResult ResumeRestore(ReplayRestoreHandle handle)
        {
            ThrowIfDisposed();
            return restoreCoordinator.Resume(handle);
        }

        public ReplayRestoreResult ReleaseColdRestoreBaseline(
            ReplayRestoreHandle handle)
        {
            ThrowIfDisposed();
            return restoreCoordinator.ReleaseColdBaseline(handle);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (started)
            {
                journal.MovieTickCommitted -= OnMovieTickCommitted;
                journal.BaselineEstablished -= OnBaselineEstablished;
                started = false;
            }

            foreach (var request in scheduler.FailPendingOnShutdown())
            {
                operations.Add(
                    new ReplaySaveOperationResult(
                        request.RequestId,
                        ReplaySaveStatus.Failed,
                        string.Empty,
                        -1,
                        "Application quit before a safe committed tick."));
            }

            FlushPreparedCommitsOnShutdown(TimeSpan.FromSeconds(2));

            if (runner != null)
            {
                runner.Clear();
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }

            restoreCoordinator.Dispose();
        }

        // The caller is already holding a completed gameplay frame. Service
        // persistence only; never release or simulate a frame to finish a save.
        internal bool PumpPausedPersistence()
        {
            var pendingBefore = PendingCount;
            var operationBefore = LastOperation;
            OnLateUpdate();
            StopRuntimePumpIfIdle();
            return pendingBefore != PendingCount
                || !ReferenceEquals(operationBefore, LastOperation);
        }

        internal void OnLateUpdate()
        {
            if (disposed)
            {
                return;
            }

            if (commitTask != null)
            {
                if (commitTask.IsCompleted)
                {
                    CompleteCommitTask();
                }
            }

            StartNextCommit();
            if (restoreCoordinator.IsActive)
            {
                return;
            }

            var targetTick = journal.LastCommittedMovieTick;
            if (targetTick < 0)
            {
                return;
            }

            if (!journal.IsAvailable)
            {
                while (scheduler.TryDequeueForSafeTick(
                           targetTick,
                           out var unavailableRequest)
                       && unavailableRequest != null)
                {
                    operations.Add(
                        new ReplaySaveOperationResult(
                            unavailableRequest.RequestId,
                            ReplaySaveStatus.JournalGap,
                            string.Empty,
                            targetTick,
                            "Shadow journal is unavailable or contains a gap."));
                }

                return;
            }

            if (!IsSafeGameplayTick())
            {
                return;
            }

            var requests = new List<ReplaySaveRequest>();
            while (scheduler.TryDequeueForSafeTick(
                       targetTick,
                       out var request)
                   && request != null)
            {
                requests.Add(request);
            }

            if (requests.Count == 0)
            {
                return;
            }

            try
            {
                var committedStamp = journal.LastCommittedStamp;
                if (!committedStamp.HasValue)
                {
                    FailPreparedRequests(
                        requests,
                        targetTick,
                        ReplaySaveStatus.Failed,
                        "Committed tick stamp is unavailable.");
                    return;
                }

                var capture = snapshotCapture.Capture(
                    new TickStamp(
                        committedStamp.Value.InputTick,
                        journal.CurrentVisualTick,
                        journal.CurrentFixedTick,
                        journal.CurrentSceneEpoch,
                        TickPhase.LateUpdateEnd));
                if (!capture.Success)
                {
                    FailPreparedRequests(
                        requests,
                        targetTick,
                        ReplaySaveStatus.Failed,
                        "Semantic capture failed at "
                        + capture.FailedProbeId
                        + ": "
                        + capture.Error);
                    return;
                }

                var frozen = journal.FreezeForReplaySave(targetTick);
                if (!frozen.Success)
                {
                    FailPreparedRequests(
                        requests,
                        targetTick,
                        ReplaySaveStatus.JournalGap,
                        frozen.Error);
                    return;
                }

                var retention = scheduler.Policy.RetentionCount;
                var createdAtUtc = DateTimeOffset.UtcNow;
                foreach (var value in requests)
                {
                    preparedCommits.Enqueue(
                        new PreparedReplaySaveCommit(
                            value,
                            frozen,
                            capture,
                            retention,
                            createdAtUtc));
                }

                StartNextCommit();
            }
            catch (Exception exception)
            {
                FailPreparedRequests(
                    requests,
                    targetTick,
                    ReplaySaveStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
                logError("T09 replay-save capture failed: " + exception);
            }
        }

        private void OnMovieTickCommitted(
            long movieTick,
            InputSample sample,
            TickStamp stamp)
        {
            try
            {
                if (restoreCoordinator.IsActive)
                {
                    return;
                }

                var request = scheduler.OnMovieTickCommitted(
                    movieTick,
                    DateTimeOffset.UtcNow);
                if (request != null)
                {
                    EnsureRuntimePump();
                }
            }
            catch (Exception exception)
            {
                logError(
                    "T09 automatic scheduler failed closed: "
                    + exception);
            }
        }

        private void OnBaselineEstablished(
            string baselineId,
            string baselineSha256)
        {
            scheduler.ResetForBaseline();
            logInfo(
                "T09 replay-save baseline ready id="
                + baselineId
                + " hash="
                + baselineSha256);
        }

        private void CompleteCommitTask()
        {
            var task = commitTask;
            var prepared = inFlightCommit;
            commitTask = null;
            inFlightCommit = null;
            if (task == null || prepared == null)
            {
                return;
            }

            try
            {
                var result = task.GetAwaiter().GetResult();
                operations.Add(
                    new ReplaySaveOperationResult(
                        prepared.Request.RequestId,
                        result.Status,
                        result.Descriptor?.ReplaySaveId ?? string.Empty,
                        result.Descriptor?.EffectiveMovieTick ?? -1,
                        result.Error));
                if (result.Success)
                {
                    logInfo(
                        "T09 replay save committed id="
                        + result.Descriptor!.ReplaySaveId
                        + " tick="
                        + result.Descriptor.EffectiveMovieTick);
                }
                else
                {
                    logWarning(
                        "T09 replay save commit failed: "
                        + result.Error);
                }
            }
            catch (Exception exception)
            {
                operations.Add(
                    new ReplaySaveOperationResult(
                        prepared.Request.RequestId,
                        ReplaySaveStatus.Failed,
                        string.Empty,
                        -1,
                        exception.GetType().Name + ": " + exception.Message));
                logError("T09 replay-save commit task failed: " + exception);
            }

            StartNextCommit();
        }

        private void StartNextCommit()
        {
            if (commitTask != null
                || preparedCommits.Count == 0)
            {
                return;
            }

            inFlightCommit = preparedCommits.Dequeue();
            var prepared = inFlightCommit!;
            commitTask = Task.Run(
                () =>
                {
                    var commit = captureBuilder.BuildCommit(
                        prepared.Request,
                        prepared.FrozenJournal,
                        prepared.SemanticCapture,
                        prepared.AutoRetentionCount,
                        prepared.CreatedAtUtc);
                    return store.Commit(commit);
                });
        }

        private void FailPreparedRequests(
            IEnumerable<ReplaySaveRequest> requests,
            long effectiveMovieTick,
            ReplaySaveStatus status,
            string error)
        {
            foreach (var request in requests)
            {
                logWarning("T09 replay-save request failed id=" + request.RequestId
                    + " status=" + status + " tick=" + effectiveMovieTick
                    + " error=" + error);
                operations.Add(
                    new ReplaySaveOperationResult(
                        request.RequestId,
                        status,
                        string.Empty,
                        effectiveMovieTick,
                        error));
            }
        }

        private void FlushPreparedCommitsOnShutdown(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (commitTask != null || preparedCommits.Count != 0)
            {
                StartNextCommit();
                var task = commitTask;
                if (task == null)
                {
                    break;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    FailUnflushedCommits();
                    return;
                }

                try
                {
                    if (!task.Wait(remaining))
                    {
                        FailUnflushedCommits();
                        return;
                    }
                }
                catch
                {
                    // CompleteCommitTask records the original task exception.
                }

                CompleteCommitTask();
            }
        }

        private void FailUnflushedCommits()
        {
            const string error =
                "Application quit before the commit transaction completed.";
            if (inFlightCommit != null)
            {
                operations.Add(
                    new ReplaySaveOperationResult(
                        inFlightCommit.Request.RequestId,
                        ReplaySaveStatus.Failed,
                        string.Empty,
                        inFlightCommit.FrozenJournal.EffectiveMovieTick,
                        error));
            }

            foreach (var prepared in preparedCommits)
            {
                operations.Add(
                    new ReplaySaveOperationResult(
                        prepared.Request.RequestId,
                        ReplaySaveStatus.Failed,
                        string.Empty,
                        prepared.FrozenJournal.EffectiveMovieTick,
                        error));
            }

            commitTask = null;
            inFlightCommit = null;
            preparedCommits.Clear();
        }

        private static bool IsSafeGameplayTick()
        {
            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            return manager != null
                   && manager.gameState == GameState.PLAYING
                   && !manager.IsInSceneTransition
                   && hero != null
                   && hero.gameObject.activeInHierarchy;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(RuntimeReplaySaveManager));
            }
        }

        private void EnsureRuntimePump()
        {
            if (runner != null)
            {
                return;
            }

            if (!started || disposed)
            {
                throw new InvalidOperationException(
                    "Replay-save runtime pump is unavailable.");
            }

            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeReplaySaveManager");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimeReplaySaveRunner>();
            runner.Initialize(this);
        }

        internal void StopRuntimePumpIfIdle()
        {
            if (runner == null
                || PendingCount != 0
                || restoreCoordinator.IsActive)
            {
                return;
            }

            var current = runner;
            runner = null;
            current.Clear();
            UnityEngine.Object.Destroy(current.gameObject);
        }

        private sealed class PreparedReplaySaveCommit
        {
            public PreparedReplaySaveCommit(
                ReplaySaveRequest request,
                RuntimeReplayJournalFreezeResult frozenJournal,
                SnapshotCaptureResult semanticCapture,
                int autoRetentionCount,
                DateTimeOffset createdAtUtc)
            {
                Request = request
                          ?? throw new ArgumentNullException(nameof(request));
                FrozenJournal = frozenJournal
                                ?? throw new ArgumentNullException(
                                    nameof(frozenJournal));
                SemanticCapture = semanticCapture
                                  ?? throw new ArgumentNullException(
                                      nameof(semanticCapture));
                AutoRetentionCount = autoRetentionCount;
                CreatedAtUtc = createdAtUtc;
            }

            public ReplaySaveRequest Request { get; }
            public RuntimeReplayJournalFreezeResult FrozenJournal { get; }
            public SnapshotCaptureResult SemanticCapture { get; }
            public int AutoRetentionCount { get; }
            public DateTimeOffset CreatedAtUtc { get; }
        }
    }

    [DefaultExecutionOrder(-31000)]
    internal sealed class RuntimeReplaySaveRunner : MonoBehaviour
    {
        private RuntimeReplaySaveManager? owner;

        internal void Initialize(RuntimeReplaySaveManager value)
        {
            owner = value;
        }

        internal void Clear()
        {
            owner = null;
        }

        private void LateUpdate()
        {
            var current = owner;
            try
            {
                current?.OnLateUpdate();
            }
            finally
            {
                current?.StopRuntimePumpIfIdle();
            }
        }
    }
}
