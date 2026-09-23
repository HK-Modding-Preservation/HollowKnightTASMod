using System;
using System.Collections.Generic;
using System.Globalization;
using HollowKnightTAS.Core.Control;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Runtime.Control;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.ReplaySave;

namespace HollowKnightTAS.Runtime.Companion
{
    public sealed class RuntimeControlService : IDisposable
    {
        public const string DeterministicRngProfile =
            "disabled-by-vanilla-equivalence-v1";

        private readonly string sessionId;
        private readonly string manifestSha256;
        private readonly Action<string, IReadOnlyDictionary<string, string>>
            publish;
        private readonly RuntimeReplayJournal journal;
        private readonly bool replayDeterministicRngRequested;
        private readonly int replayDeterministicRngSeed;
        private readonly Action pauseBoundaryCommandPump;
        private readonly T24DeferredRecordingArmGate?
            deferredRecordingArmGate;
        private RuntimePauseController? pause;
        private RuntimePlaybackController? playback;
        private RestoreSettingsLease? adoptedRestoreSettingsLease;
        private MovieDocument? movie;
        private bool inputBatchActive;
        private long inputBatchBaseMovieTick = -1;
        private bool journalPlaybackCaptureActive;
        private bool deferredPauseArmed;
        private bool deferredReplayArmed;
        private int deferredNeutralPreRollCompletedFrameCount;
        private bool deferredActivationAttempted;
        private bool deferredActivationSucceeded;
        private int deferredActivationCount;
        private string deferredActivationError = string.Empty;
        private bool disposed;

        public RuntimeControlService(
            string sessionId,
            string manifestSha256,
            RuntimeReplayJournal journal,
            bool replayDeterministicRngEnabled,
            int replayDeterministicRngSeed,
            Action pauseBoundaryCommandPump,
            Action<string, IReadOnlyDictionary<string, string>> publish)
        {
            this.sessionId = sessionId;
            this.manifestSha256 = manifestSha256;
            this.journal = journal
                           ?? throw new ArgumentNullException(
                               nameof(journal));
            replayDeterministicRngRequested =
                replayDeterministicRngEnabled;
            this.replayDeterministicRngSeed =
                replayDeterministicRngSeed;
            this.pauseBoundaryCommandPump =
                pauseBoundaryCommandPump
                ?? throw new ArgumentNullException(
                    nameof(pauseBoundaryCommandPump));
            this.publish = publish;
            this.journal.MovieTickCommitted +=
                OnJournalMovieTickCommitted;
            if (!T24DeferredRecordingArmGate.TryParseRunId(
                    Environment.GetCommandLineArgs(),
                    out var deferredRunId,
                    out var deferredError))
            {
                throw new InvalidOperationException(deferredError);
            }
            if (!string.IsNullOrEmpty(deferredRunId))
            {
                deferredRecordingArmGate =
                    new T24DeferredRecordingArmGate(deferredRunId);
            }
        }

        public MovieDocument? Movie => movie;
        public PlaybackMode PlaybackMode =>
            playback?.Mode ?? PlaybackMode.Idle;
        public long AuthoritativeMovieTick =>
            inputBatchActive && playback?.ReplayTimelineActive == true
                ? checked(
                    inputBatchBaseMovieTick
                    + playback.ReplayMovieTick
                    + 1)
                : playback?.ReplayTimelineActive == true
                ? playback.ReplayMovieTick
                : journal.LastCommittedMovieTick;
        public string MovieTickSource =>
            inputBatchActive && playback?.ReplayTimelineActive == true
                ? "adaptive-input-batch"
                : playback?.ReplayTimelineActive == true
                ? "replay"
                : "journal";
        public bool RuntimePumpRequired =>
            pause != null || playback != null;
        public bool DeferredRecordingArmEnabled =>
            deferredRecordingArmGate != null;
        public string DeferredRecordingArmRunId =>
            deferredRecordingArmGate?.RunId ?? string.Empty;
        public bool DeferredRecordingArmReleaseConsumed =>
            deferredRecordingArmGate?.ReleaseConsumed == true;
        public bool DeferredPauseArmed => deferredPauseArmed;
        public bool DeferredReplayArmed => deferredReplayArmed;
        public int DeferredNeutralPreRollCompletedFrameCount =>
            deferredNeutralPreRollCompletedFrameCount;
        public bool DeferredActivationAttempted =>
            deferredActivationAttempted;
        public bool DeferredActivationSucceeded =>
            deferredActivationSucceeded;
        public int DeferredActivationCount => deferredActivationCount;
        public string DeferredActivationError => deferredActivationError;
        public bool InputBatchActive => inputBatchActive;
        public long PauseLeaseReassertionCount =>
            pause?.PauseLeaseReassertionCount ?? 0;
        public long FrozenHeroActionUpdateCount =>
            pause?.FrozenHeroActionUpdateCount ?? 0;
        public long AdvancedHeroActionUpdateCount =>
            pause?.AdvancedHeroActionUpdateCount ?? 0;
        public long HeroActionPhaseRejectionCount =>
            pause?.HeroActionPhaseRejectionCount ?? 0;
        public bool HeroActionUpdateObserved =>
            pause?.HeroActionUpdateObserved == true;
        public bool LastHeroActionUpdateAdvanced =>
            pause?.LastHeroActionUpdateAdvanced == true;
        public ulong LastHeroActionUpdateTick =>
            pause?.LastHeroActionUpdateTick ?? 0;
        public long IgnoredFocusLossCount =>
            pause?.IgnoredFocusLossCount ?? 0;
        public long PausedBoundaryWaitCount =>
            pause?.PausedBoundaryWaitCount ?? 0;
        public long PausedBoundaryPumpCount =>
            pause?.PausedBoundaryPumpCount ?? 0;
        public long CompletedFrameSceneTransitionPassThroughCount =>
            pause?.CompletedFrameSceneTransitionPassThroughCount ?? 0;
        public bool CompletedFrameSceneTransitionPassThroughActive =>
            pause?.CompletedFrameSceneTransitionPassThroughActive == true;
        public string ControlGateStrategyId =>
            pause?.GateStrategyId ?? string.Empty;
        public bool UsesCompletedFrameBoundaryGate =>
            pause?.UsesCompletedFrameBoundaryGate == true;
        public string ControlFault => pause?.FaultMessage ?? string.Empty;
        public string LastControlAbortReason =>
            pause?.LastAbortReason ?? string.Empty;
        public string LastStepInterruptionReason => pause?.LastStepInterruptionReason ?? string.Empty;
        public int LastInterruptedStepRequestedTicks => pause?.LastInterruptedStepRequestedTicks ?? 0;
        public int LastInterruptedStepCommittedTicks => pause?.LastInterruptedStepCommittedTicks ?? 0;
        private bool disconnectCleanupPending;
        public bool DisconnectCleanupPending => disconnectCleanupPending;
        public long LastReplayMovieTick { get; private set; } = -1;
        public long ReplayObservationCount { get; private set; }
        public long ReplaySuspendedRawInputTickCount =>
            playback?.ReplayTimelineActive == true
                ? playback.ReplaySuspendedRawInputTickCount
                : lastReplaySuspendedRawInputTickCount;
        public long ReplayMismatchCount { get; private set; }
        public long FirstReplayMismatchMovieTick { get; private set; } = -1;
        public string FirstReplayMismatchExpected { get; private set; } =
            string.Empty;
        public string FirstReplayMismatchActual { get; private set; } =
            string.Empty;
        public long LastReplayMismatchMovieTick { get; private set; } = -1;
        public string LastReplayMismatchExpected { get; private set; } =
            string.Empty;
        public string LastReplayMismatchActual { get; private set; } =
            string.Empty;
        public bool ReplayPhysicalNoiseDetected { get; private set; }
        public bool JournalPlaybackCaptureActive =>
            journal.PlaybackCaptureActive;
        public string JournalPlaybackCaptureError =>
            journal.PlaybackCaptureError;
        public PlaybackStopReason? LastPlaybackStopReason { get; private set; }
        public bool? LastBindingRestoreEquivalent { get; private set; }
        public string LastPlaybackFault { get; private set; } = string.Empty;
        public bool ReplayDeterministicRngEnabled =>
            false;
        public bool ReplayDeterministicRngRequested =>
            replayDeterministicRngRequested;
        public int ReplayDeterministicRngSeed =>
            replayDeterministicRngSeed;
        public string ReplayDeterministicRngStatus =>
            replayDeterministicRngRequested
                ? "blocked-by-vanilla-equivalence"
                : "disabled";
        public long ReplayDeterministicRngResetCount { get; private set; }
        public string LastReplayRngBeforeSha256 { get; private set; } =
            string.Empty;
        public string LastReplayRngStateSha256 { get; private set; } =
            string.Empty;
        public int LastReplayRngAppliedSeed { get; private set; }
        public string LastReplayRngBoundary { get; private set; } =
            string.Empty;
        public string LastReplayRngScene { get; private set; } =
            string.Empty;
        private long lastReplaySuspendedRawInputTickCount;
        public SimulationControlMode ControlMode =>
            pause?.Mode ?? SimulationControlMode.Running;

        internal void ReleaseBoundaryForApplicationQuit()
        {
            ThrowIfDisposed();
            // Startup handoff exits from the title before any pause controller
            // exists. A paused game still needs its completed-frame gate
            // released so Unity can finish the quit request.
            pause?.ReleaseBoundaryForApplicationQuit();
        }

        public void SetMovie(MovieDocument value)
        {
            ThrowIfDisposed();
            if (PlaybackMode == PlaybackMode.Replaying)
            {
                throw new InvalidOperationException(
                    "Stop replay before replacing the movie.");
            }

            movie = value
                    ?? throw new ArgumentNullException(nameof(value));
        }

        public PlaybackStartResult StartReplay()
        {
            ThrowIfDisposed();
            RequireRecordingOriginReady();
            if (deferredRecordingArmGate != null
                && !deferredActivationAttempted)
            {
                return ArmDeferredReplay();
            }

            return StartReplayNow();
        }

        public int LoadedMovieFrameCount
        {
            get
            {
                if (movie == null) throw new InvalidOperationException("Upload a validated movie first.");
                long count = 0;
                foreach (var command in movie.Commands)
                    if (command is FrameRunCommand run) count = checked(count + run.FrameCount);
                if (count < 1 || count > MovieProtocolV1.DefaultMaxExpandedTicks)
                    throw new InvalidOperationException("Movie length is outside the supported replay range.");
                return checked((int)count);
            }
        }

        private PlaybackStartResult StartReplayNow()
        {
            ThrowIfDisposed();
            EnsureControllers();
            var activeMovie = movie
                              ?? throw new InvalidOperationException(
                                  "Upload a validated movie first.");
            var activePlayback = playback
                                 ?? throw new InvalidOperationException(
                                     "Playback controller is unavailable.");
            LastReplayMovieTick = -1;
            ReplayObservationCount = 0;
            lastReplaySuspendedRawInputTickCount = 0;
            ReplayMismatchCount = 0;
            FirstReplayMismatchMovieTick = -1;
            FirstReplayMismatchExpected = string.Empty;
            FirstReplayMismatchActual = string.Empty;
            LastReplayMismatchMovieTick = -1;
            LastReplayMismatchExpected = string.Empty;
            LastReplayMismatchActual = string.Empty;
            ReplayPhysicalNoiseDetected = false;
            LastPlaybackStopReason = null;
            LastBindingRestoreEquivalent = null;
            LastPlaybackFault = string.Empty;
            if (journal.IsAvailable)
            {
                if (!journal.BeginPlaybackCapture(out var captureError))
                {
                    var rejected = new PlaybackStartResult(
                        false,
                        "Replay journal capture could not start: "
                        + captureError,
                        null,
                        Array.Empty<PlaybackEvent>());
                    PublishModes("journalCaptureRejected");
                    return rejected;
                }

                journalPlaybackCaptureActive = true;
            }

            PlaybackStartResult result;
            try
            {
                result = activePlayback.StartReplay(
                    activeMovie,
                    new PlaybackContext(
                        manifestSha256,
                        activeMovie.Header.BaselineId,
                        activeMovie.Header.BaselineSha256,
                        activePlayback.SceneEpoch,
                        allowSceneTransitions: true,
                        // Batches append to one continuous input timeline.
                        // Starting a new request must not synthesize a press
                        // for a button held at the preceding completed frame.
                        initialHeld: inputBatchActive
                            ? journal.LastCommittedSample?.Held ?? TasAction.None
                            : TasAction.None));
            }
            catch
            {
                EndJournalPlaybackCapture();
                throw;
            }

            if (!result.Success)
            {
                EndJournalPlaybackCapture();
            }

            PublishModes(result.Success ? "startReplay" : "startRejected");
            return result;
        }

        public PlaybackStopResult StopReplay()
        {
            ThrowIfDisposed();
            var interruptInputBatch = inputBatchActive
                && pause?.Mode == SimulationControlMode.Stepping;
            var result = (playback
                          ?? throw new InvalidOperationException(
                              "Playback controller is unavailable."))
                .Stop(PlaybackStopReason.Manual);
            if (result.Success && interruptInputBatch)
            {
                // Stopping the replay source alone releases input but leaves
                // the step quota running. Close it at the completed boundary,
                // using the same frame accounting as disconnect interruption.
                pause!.InterruptStepAfterCurrentFrame("manual-stop");
            }
            PublishModes("stopReplay");
            return result;
        }

        public ControlResult Pause()
        {
            ThrowIfDisposed();
            RequireRecordingOriginReady();
            if (deferredRecordingArmGate != null
                && !deferredActivationAttempted)
            {
                return ArmDeferredPause();
            }

            var result = RequirePause().Pause();
            PublishModes("pause");
            return result;
        }

        internal ControlResult PauseForMenuRestore()
        {
            ThrowIfDisposed();
            if (!RuntimePauseController.IsStableTitleMenu()
                || journal.LastCommittedMovieTick >= 0
                || PlaybackMode != PlaybackMode.Idle || inputBatchActive)
                throw new InvalidOperationException("Menu restore requires an unloaded, idle source session.");
            var result = RequirePause().PauseForMenuRestore();
            PublishModes("pauseForMenuRestore");
            return result;
        }

        internal ControlResult PauseForSourceLifecycle(int sequence)
        {
            ThrowIfDisposed();
            // Lifecycle capture intentionally makes the ordinary recording
            // origin unavailable. Only its owning, still-active operation may
            // use this path; public pause retains its normal origin checks.
            if (journal.ActiveLifecycleSequence != sequence
                || PlaybackMode != PlaybackMode.Idle || inputBatchActive)
                throw new InvalidOperationException("Lifecycle pause requires the matching active, idle source operation.");
            var result = RuntimePauseController.IsStableTitleMenu()
                ? RequirePause().PauseForMenuRestore()
                : RequirePause().Pause();
            PublishModes("pauseForSourceLifecycle");
            return result;
        }

        internal void InterruptVideoStep()
        {
            if (ControlMode == SimulationControlMode.Stepping)
                RequirePause().InterruptStepAfterCurrentFrame("video-export-pause");
        }

        public ControlResult Step(int count)
        {
            ThrowIfDisposed();
            RequireRecordingOriginReady();
            var result = RequirePause().Step(
                new StepRequest(
                    StepBoundary.MovieTick,
                    count));
            PublishModes("step");
            return result;
        }

        internal bool CompletePlaybackAtPausedBoundary()
        {
            // Called only by the completed-frame command pump, never while
            // gameplay is still consuming the last sample of an input batch.
            if (!disposed && ControlMode == SimulationControlMode.Paused && disconnectCleanupPending)
            {
                CleanupForDisconnect();
                return true;
            }
            return !disposed
                && ControlMode == SimulationControlMode.Paused
                && playback?.CompleteAtPausedBoundary() == true;
        }

        public ControlResult StartInputBatch(int count)
        {
            ThrowIfDisposed();
            RequireRecordingOriginReady();
            if (count < 1
                || count > MovieProtocolV1.DefaultMaxExpandedTicks)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Input batch count must be in [1,"
                    + MovieProtocolV1.DefaultMaxExpandedTicks
                    + "].");
            }

            if (ControlMode != SimulationControlMode.Paused)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Input batches require a Paused Runtime.");
            }

            if (PlaybackMode != PlaybackMode.Idle || inputBatchActive)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Input batches require idle playback.");
            }

            inputBatchBaseMovieTick = journal.LastCommittedMovieTick;
            inputBatchActive = true;
            var start = StartReplay();
            if (!start.Success)
            {
                inputBatchActive = false;
                inputBatchBaseMovieTick = -1;
                return new ControlResult(
                    false,
                    ControlMode,
                    start.Error);
            }

            var step = Step(count);
            if (!step.Success)
            {
                StopReplay();
                return step;
            }

            return step;
        }

        public ControlResult Resume()
        {
            ThrowIfDisposed();
            if (adoptedRestoreSettingsLease != null
                && !adoptedRestoreSettingsLease.Restore())
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Cold-restore outer time settings could not be restored exactly.");
            }

            var result = RequirePause().Resume();
            if (result.Success)
            {
                adoptedRestoreSettingsLease?.Dispose();
                adoptedRestoreSettingsLease = null;
            }
            PublishModes("resume");
            return result;
        }

        public ControlResult AdoptRestoredPauseBoundary(
            RuntimePauseController restoredPause,
            RestoreSettingsLease restoredSettings,
            MovieDocument restoredMovie)
        {
            ThrowIfDisposed();
            if (restoredPause == null)
            {
                throw new ArgumentNullException(nameof(restoredPause));
            }

            if (restoredSettings == null)
            {
                throw new ArgumentNullException(nameof(restoredSettings));
            }

            if (restoredMovie == null)
            {
                throw new ArgumentNullException(nameof(restoredMovie));
            }

            if (pause != null
                || playback != null
                || adoptedRestoreSettingsLease != null
                || inputBatchActive
                || restoredPause.Mode != SimulationControlMode.Paused)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Unified control cannot adopt the verified paused restore boundary.");
            }

            restoredPause.ReplacePausedBoundaryCommandPump(
                pauseBoundaryCommandPump, abortOnFocusLoss: false);
            pause = restoredPause;
            adoptedRestoreSettingsLease = restoredSettings;
            movie = restoredMovie;
            PublishModes("coldRestorePausedHandoff");
            return new ControlResult(
                true,
                ControlMode,
                string.Empty);
        }

        public ControlResult PrepareForReplayRestore()
        {
            ThrowIfDisposed();
            if (PlaybackMode != PlaybackMode.Idle || inputBatchActive)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Replay restore requires idle playback and no input batch.");
            }

            // Disposing a paused controller restores its time-settings lease
            // before the independent T09 restore coordinator takes ownership.
            DisposeControllers();
            inputBatchActive = false;
            inputBatchBaseMovieTick = -1;
            PublishModes("prepareReplayRestore");
            return new ControlResult(
                true,
                ControlMode,
                string.Empty);
        }

        public bool TryTakeCompletedStep(out StepResult? result)
        {
            result = null;
            return pause != null
                   && pause.TryTakeCompletedStep(out result);
        }

        public void CleanupForDisconnect()
        {
            if (disposed)
            {
                return;
            }

            if (pause?.Mode == SimulationControlMode.Stepping)
            {
                disconnectCleanupPending = true;
                pause.InterruptStepAfterCurrentFrame("companion-disconnected");
                return;
            }
            if (pause?.Mode == SimulationControlMode.Pausing)
            {
                disconnectCleanupPending = true;
                return;
            }
            if (pause?.Mode == SimulationControlMode.Running
                && playback?.Mode == PlaybackMode.Replaying)
            {
                var stopAtBoundary = pause.Pause();
                if (stopAtBoundary.Success)
                {
                    disconnectCleanupPending = true;
                    return;
                }
                // Scene/menu phases without a supported pause boundary retain
                // the existing binding cleanup, but must not report a held pause.
                LastPlaybackFault = "Disconnect could not retain a pause: " + stopAtBoundary.Error;
            }

            if (pause?.Mode == SimulationControlMode.Paused)
            {
                // The completed-frame gate belongs to the user's timeline,
                // not the lifetime of a Companion connection. Keep its clock
                // lease and command pump so a reconnect cannot advance time.
                // Input detachment does not grant a frame or rewrite Hero state.
                try
                {
                    playback?.Stop(PlaybackStopReason.Manual);
                    playback?.Dispose();
                    playback = null;
                }
                finally
                {
                    disconnectCleanupPending = false;
                    EndJournalPlaybackCapture();
                    inputBatchActive = false;
                    inputBatchBaseMovieTick = -1;
                }
                PublishModes("companion-disconnected-pause-retained");
                return;
            }

            try
            {
                if (playback?.Mode == PlaybackMode.Replaying)
                {
                    playback.Stop(PlaybackStopReason.Manual);
                }
            }
            catch
            {
                // Controller disposal below remains mandatory.
            }

            DisposeControllers();
            inputBatchActive = false;
            inputBatchBaseMovieTick = -1;
            PublishModes("companion-disconnected");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            journal.MovieTickCommitted -=
                OnJournalMovieTickCommitted;
            DisposeControllers();
            deferredRecordingArmGate?.Dispose();
        }

        private ControlResult ArmDeferredPause()
        {
            if (deferredPauseArmed)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Deferred recording-arm pause is already armed.");
            }

            if (ControlMode != SimulationControlMode.Running)
            {
                return new ControlResult(
                    false,
                    ControlMode,
                    "Deferred recording-arm pause requires a Running Runtime.");
            }

            EnsureControllers();
            deferredPauseArmed = true;
            PublishModes("deferredRecordingArmPauseArmed");
            return new ControlResult(
                true,
                ControlMode,
                string.Empty);
        }

        private PlaybackStartResult ArmDeferredReplay()
        {
            if (!deferredPauseArmed)
            {
                return new PlaybackStartResult(
                    false,
                    "Arm deferred pause before deferred replay.",
                    null,
                    Array.Empty<PlaybackEvent>());
            }

            if (deferredReplayArmed)
            {
                return new PlaybackStartResult(
                    false,
                    "Deferred recording-arm replay is already armed.",
                    null,
                    Array.Empty<PlaybackEvent>());
            }

            EnsureControllers();
            if (movie == null)
            {
                return new PlaybackStartResult(
                    false,
                    "Upload a validated movie before arming replay.",
                    null,
                    Array.Empty<PlaybackEvent>());
            }
            if (PlaybackMode != PlaybackMode.Idle)
            {
                return new PlaybackStartResult(
                    false,
                    "Deferred recording-arm replay requires idle playback.",
                    null,
                    Array.Empty<PlaybackEvent>());
            }

            deferredReplayArmed = true;
            PublishModes("deferredRecordingArmReplayArmed");
            return new PlaybackStartResult(
                true,
                string.Empty,
                null,
                Array.Empty<PlaybackEvent>());
        }

        private void TryActivateDeferredRecordingArm()
        {
            var gate = deferredRecordingArmGate;
            if (gate == null)
            {
                return;
            }

            if (!gate.ReleaseConsumed)
            {
                if (!gate.TryConsumeRelease())
                {
                    return;
                }

                // Reference input synchronization intentionally primes on
                // one unrecorded neutral frame after the host release. Do
                // not activate replay at this completed-frame boundary;
                // the next guard proves that exactly one full vanilla frame
                // has completed before pause/replay become active.
                return;
            }

            if (deferredActivationAttempted)
            {
                return;
            }

            deferredActivationAttempted = true;
            deferredNeutralPreRollCompletedFrameCount++;
            deferredActivationCount++;
            try
            {
                if (deferredNeutralPreRollCompletedFrameCount != 1)
                {
                    throw new InvalidOperationException(
                        "Deferred recording arm requires exactly one completed neutral pre-roll frame.");
                }
                if (!deferredPauseArmed || !deferredReplayArmed)
                {
                    throw new InvalidOperationException(
                        "Recording-arm release arrived before pause and replay were both armed.");
                }

                var activePause = pause
                                  ?? throw new InvalidOperationException(
                                      "Deferred pause controller is unavailable.");
                if (playback == null || movie == null)
                {
                    throw new InvalidOperationException(
                        "Deferred replay controller or movie is unavailable.");
                }

                var pauseResult = activePause.Pause();
                if (!pauseResult.Success)
                {
                    throw new InvalidOperationException(
                        "Deferred recording-arm pause failed: "
                        + pauseResult.Error);
                }

                var replayResult = StartReplayNow();
                if (!replayResult.Success)
                {
                    throw new InvalidOperationException(
                        "Deferred recording-arm replay failed: "
                        + replayResult.Error);
                }

                deferredActivationSucceeded = true;
                deferredActivationError = string.Empty;
                PublishModes("deferredRecordingArmActivated");
            }
            catch (Exception exception)
            {
                deferredActivationSucceeded = false;
                deferredActivationError = exception.GetType().Name
                                          + ":"
                                          + exception.Message;
                try
                {
                    if (playback?.Mode == PlaybackMode.Replaying)
                    {
                        playback.Stop(PlaybackStopReason.RuntimeFault);
                    }
                }
                catch
                {
                    // The RuntimePauseController guard still faults and
                    // restores its own control lease below.
                }
                PublishModes("deferredRecordingArmActivationFailed");
                throw;
            }
        }

        private void OnJournalMovieTickCommitted(
            long movieTick,
            InputSample input,
            TickStamp stamp)
        {
            // During replay, HeroActionReplayer already delivers the same
            // committed tick through IMovieTickGate. The shadow journal is
            // the authoritative tick source while playback is idle, which
            // is what makes Companion pause/step work during ordinary
            // gameplay rather than only during a loaded replay.
            if (disposed
                || playback?.Mode == PlaybackMode.Replaying)
            {
                return;
            }

            pause?.OnMovieTickCommitted(movieTick, stamp);
        }

        private void EnsureControllers()
        {
            if (pause != null && playback != null)
            {
                return;
            }

            if (pause == null && playback != null)
            {
                DisposeControllers();
            }

            if (pause == null)
            {
                pause = CreatePauseController();
            }

            if (playback == null)
            {
                CreatePlaybackController();
            }
        }

        private void CreateControllers()
        {
            pause = CreatePauseController();
            CreatePlaybackController();
        }

        private RuntimePauseController CreatePauseController()
        {
            return new RuntimePauseController(
                sessionId,
                manifestSha256,
                "companion",
                "COMPANION",
                abortOnFocusLoss: false,
                pausedBoundaryCommandPump: pauseBoundaryCommandPump,
                completedFrameBoundaryActivation:
                    deferredRecordingArmGate == null
                        ? null
                        : TryActivateDeferredRecordingArm);
        }

        private void CreatePlaybackController()
        {
            if (pause == null)
            {
                throw new InvalidOperationException(
                    "Pause controller is required before playback creation.");
            }

            playback = new RuntimePlaybackController(
                observation =>
                {
                    LastReplayMovieTick = observation.MovieTick;
                    ReplayObservationCount++;
                    if (journalPlaybackCaptureActive
                        && !journal.AppendPlaybackObservation(
                            observation,
                            out var captureError))
                    {
                        EndJournalPlaybackCapture();
                        publish(
                            HollowKnightTAS.Core.Ipc.IpcMessageTypes
                                .RuntimeModeChanged,
                            new Dictionary<string, string>(
                                StringComparer.Ordinal)
                            {
                                ["controlMode"] =
                                    pause?.Mode.ToString()
                                    ?? string.Empty,
                                ["journalPlaybackCaptureActive"] =
                                    "false",
                                ["journalPlaybackCaptureError"] =
                                    captureError,
                                ["playbackMode"] =
                                    playback?.Mode.ToString()
                                    ?? string.Empty,
                                ["reason"] =
                                    "journalPlaybackCaptureFailed"
                            });
                    }

                    if (!observation.Matches)
                    {
                        if (ReplayMismatchCount == 0)
                        {
                            FirstReplayMismatchMovieTick =
                                observation.MovieTick;
                            FirstReplayMismatchExpected =
                                FormatExpected(observation);
                            FirstReplayMismatchActual =
                                FormatActualDiagnostic(observation);
                        }
                        ReplayMismatchCount++;
                        LastReplayMismatchMovieTick =
                            observation.MovieTick;
                        LastReplayMismatchExpected =
                            FormatExpected(observation);
                        LastReplayMismatchActual =
                            FormatActualDiagnostic(observation);
                    }
                    ReplayPhysicalNoiseDetected |= observation.PhysicalNoise;
                    publish(
                        HollowKnightTAS.Core.Ipc.IpcMessageTypes
                            .TickLedger,
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["actual"] = FormatActual(observation),
                            ["expected"] = FormatExpected(observation),
                            ["matches"] =
                                observation.Matches
                                    ? "true"
                                    : "false",
                            ["controlSignalMatches"] =
                                observation.ControlSignalMatches
                                    ? "true"
                                    : "false",
                            ["committedEdgesMatch"] =
                                observation.CommittedEdgesMatch
                                    ? "true"
                                    : "false",
                            ["movieTick"] =
                                observation.MovieTick.ToString(
                                    CultureInfo.InvariantCulture),
                            ["physicalNoise"] =
                                observation.PhysicalNoise
                                    ? "true"
                                    : "false",
                            ["rawInputTick"] =
                                observation.RawInputTick.ToString(
                                    CultureInfo.InvariantCulture)
                        });
                },
                OnPlaybackEvent,
                (reason, report) =>
                {
                    EndJournalPlaybackCapture();
                    lastReplaySuspendedRawInputTickCount =
                        playback?.ReplaySuspendedRawInputTickCount ?? 0;
                    LastPlaybackStopReason = reason;
                    LastBindingRestoreEquivalent = report.Equivalent;
                    LastPlaybackFault = playback?.FaultMessage
                                        ?? string.Empty;
                    inputBatchActive = false;
                    inputBatchBaseMovieTick = -1;
                    publish(
                        HollowKnightTAS.Core.Ipc.IpcMessageTypes
                            .RuntimeModeChanged,
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["bindingRestoreEquivalent"] =
                                report.Equivalent
                                    ? "true"
                                    : "false",
                            ["controlMode"] =
                                pause?.Mode.ToString()
                                ?? string.Empty,
                            ["playbackMode"] =
                                playback?.Mode.ToString()
                                ?? string.Empty,
                            ["reason"] = reason.ToString(),
                            ["lastReplayMovieTick"] =
                                LastReplayMovieTick.ToString(
                                    CultureInfo.InvariantCulture),
                            ["observationCount"] =
                                ReplayObservationCount.ToString(
                                    CultureInfo.InvariantCulture),
                            ["suspendedRawInputTickCount"] =
                                lastReplaySuspendedRawInputTickCount.ToString(
                                    CultureInfo.InvariantCulture),
                            ["mismatchCount"] =
                                ReplayMismatchCount.ToString(
                                    CultureInfo.InvariantCulture),
                            ["lastMismatchMovieTick"] =
                                LastReplayMismatchMovieTick.ToString(
                                    CultureInfo.InvariantCulture),
                            ["lastMismatchExpected"] =
                                LastReplayMismatchExpected,
                            ["lastMismatchActual"] =
                                LastReplayMismatchActual,
                            ["physicalNoiseDetected"] =
                                ReplayPhysicalNoiseDetected
                                    ? "true"
                                    : "false",
                            ["fault"] = LastPlaybackFault
                        });
                },
                pause,
                null);
        }

        private void DisposeControllers()
        {
            try
            {
                playback?.Dispose();
                playback = null;
                pause?.Dispose();
                pause = null;
                adoptedRestoreSettingsLease?.Dispose();
                adoptedRestoreSettingsLease = null;
            }
            finally
            {
                EndJournalPlaybackCapture();
            }
        }

        private void EndJournalPlaybackCapture()
        {
            if (!journalPlaybackCaptureActive)
            {
                return;
            }

            journalPlaybackCaptureActive = false;
            journal.EndPlaybackCapture();
        }

        private static string FormatExpected(
            ReplayInputObservation observation)
        {
            return "held="
                   + observation.Expected.Held
                   + ";pressed="
                   + observation.Expected.Pressed
                   + ";released="
                   + observation.Expected.Released
                   + ";x="
                   + observation.Expected.AxisX.ToString(
                       CultureInfo.InvariantCulture)
                   + ";y="
                   + observation.Expected.AxisY.ToString(
                       CultureInfo.InvariantCulture);
        }

        private static string FormatActual(
            ReplayInputObservation observation)
        {
            return "held="
                   + observation.Actual.Held
                   + ";pressed="
                   + observation.Actual.Pressed
                   + ";released="
                   + observation.Actual.Released
                   + ";x="
                   + observation.Actual.AxisX.ToString(
                       CultureInfo.InvariantCulture)
                   + ";y="
                   + observation.Actual.AxisY.ToString(
                       CultureInfo.InvariantCulture);
        }

        private string FormatActualDiagnostic(
            ReplayInputObservation observation)
        {
            return FormatActual(observation)
                   + ";rawInputTick="
                   + observation.RawInputTick.ToString(
                       CultureInfo.InvariantCulture)
                   + ";attachedActionSetIsCurrent="
                   + (observation.AttachedActionSetIsCurrent
                       ? "true"
                       : "false")
                   + ";heroActionPhase="
                   + (pause?.LastHeroActionPhaseDiagnostic
                      ?? string.Empty);
        }

        private void OnPlaybackEvent(PlaybackEvent value)
        {
            var detail = value.Command switch
            {
                MarkerCommand marker => marker.Text,
                CheckpointCommand checkpoint =>
                    checkpoint.Identifier,
                AssertCommand assertion =>
                    assertion.SemanticPath
                    + " "
                    + assertion.Operator
                    + " "
                    + assertion.Value,
                _ => value.Command.GetType().Name
            };
            publish(
                HollowKnightTAS.Core.Ipc.IpcMessageTypes.Milestone,
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["commandIndex"] =
                        value.CommandIndex.ToString(
                            CultureInfo.InvariantCulture),
                    ["detail"] = detail,
                    ["kind"] = value.Kind.ToString(),
                    ["movieTick"] =
                        value.MovieTick.ToString(
                            CultureInfo.InvariantCulture)
                });
        }

        private RuntimePauseController RequirePause()
        {
            if (pause == null)
            {
                if (playback != null)
                {
                    DisposeControllers();
                }

                pause = CreatePauseController();
            }

            return pause
                   ?? throw new InvalidOperationException(
                       "Pause controller is unavailable.");
        }

        private void PublishModes(string reason)
        {
            publish(
                HollowKnightTAS.Core.Ipc.IpcMessageTypes.RuntimeModeChanged,
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["controlMode"] = ControlMode.ToString(),
                    ["playbackMode"] = PlaybackMode.ToString(),
                    ["reason"] = reason
                });
        }

        private void RequireRecordingOriginReady()
        {
            var status = journal.RecordingOriginStatus;
            if (status != "Unmanaged" && status != "Ready")
                throw new InvalidOperationException("Recording origin is " + status
                    + ". Gameplay input is unavailable until preparation completes. "
                    + journal.RecordingOriginDetail);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(RuntimeControlService));
            }
        }
    }
}
