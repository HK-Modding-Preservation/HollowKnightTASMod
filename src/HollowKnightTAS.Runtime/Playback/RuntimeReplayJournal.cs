using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Recording;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Runtime.ReplaySave;
using HollowKnightTAS.Runtime.Companion;
using HollowKnightTAS.Runtime.State;
using HollowKnightTAS.Runtime.Timing;
using InControl;
using Modding;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Playback
{
    public sealed class RuntimeReplayJournalFreezeResult
    {
        private readonly IReadOnlyList<byte[]> segmentBytes;
        private readonly IReadOnlyList<RuntimeReplayJournalCommand> commands;

        private RuntimeReplayJournalFreezeResult(
            bool success,
            string error,
            long effectiveMovieTick,
            string baselineId,
            string baselineSemanticSha256,
            byte[]? baselineBundleBytes,
            IEnumerable<byte[]> segments,
            IEnumerable<RuntimeReplayJournalCommand> commands,
            string journalHeadSha256,
            ReplayLifecycleLog? lifecycle = null,
            IReadOnlyDictionary<string, byte[]>? lifecycleObjects = null)
        {
            Success = success;
            Error = error ?? string.Empty;
            EffectiveMovieTick = effectiveMovieTick;
            BaselineId = baselineId ?? string.Empty;
            BaselineSemanticSha256 = baselineSemanticSha256 ?? string.Empty;
            BaselineBundleBytes = baselineBundleBytes == null
                ? null
                : (byte[])baselineBundleBytes.Clone();
            segmentBytes = new List<byte[]>(
                (segments ?? Array.Empty<byte[]>())
                .Select(value => (byte[])value.Clone()));
            this.commands = new List<RuntimeReplayJournalCommand>(
                commands ?? Array.Empty<RuntimeReplayJournalCommand>());
            JournalHeadSha256 = journalHeadSha256 ?? string.Empty;
            Lifecycle = lifecycle;
            LifecycleObjects = new System.Collections.ObjectModel.ReadOnlyDictionary<string, byte[]>(
                (lifecycleObjects ?? new Dictionary<string, byte[]>()).ToDictionary(
                    item => item.Key, item => (byte[])item.Value.Clone(), StringComparer.Ordinal));
        }

        public bool Success { get; }
        public string Error { get; }
        public long EffectiveMovieTick { get; }
        public string BaselineId { get; }
        public string BaselineSemanticSha256 { get; }
        public byte[]? BaselineBundleBytes { get; }
        public IReadOnlyList<byte[]> SegmentBytes => segmentBytes;
        public IReadOnlyList<RuntimeReplayJournalCommand> Commands => commands;
        public string JournalHeadSha256 { get; }
        public ReplayLifecycleLog? Lifecycle { get; }
        public IReadOnlyDictionary<string, byte[]> LifecycleObjects { get; }

        internal static RuntimeReplayJournalFreezeResult Succeeded(
            long effectiveMovieTick,
            string baselineId,
            string baselineSemanticSha256,
            byte[] baselineBundleBytes,
            IEnumerable<byte[]> segments,
            IEnumerable<RuntimeReplayJournalCommand> commands,
            string journalHeadSha256,
            ReplayLifecycleLog? lifecycle = null,
            IReadOnlyDictionary<string, byte[]>? lifecycleObjects = null)
        {
            return new RuntimeReplayJournalFreezeResult(
                true,
                string.Empty,
                effectiveMovieTick,
                baselineId,
                baselineSemanticSha256,
                baselineBundleBytes,
                segments,
                commands,
                journalHeadSha256, lifecycle, lifecycleObjects);
        }

        internal static RuntimeReplayJournalFreezeResult Failed(string error)
        {
            return new RuntimeReplayJournalFreezeResult(
                false,
                error,
                -1,
                string.Empty,
                string.Empty,
                null,
                Array.Empty<byte[]>(),
                Array.Empty<RuntimeReplayJournalCommand>(),
                string.Empty);
        }
    }

    public sealed class RuntimeReplayJournalCommand
    {
        public RuntimeReplayJournalCommand(
            long beforeMovieTick,
            string checkpointIdentifier)
        {
            if (beforeMovieTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(beforeMovieTick));
            }

            if (!MovieProtocolV1.IsIdentifier(
                    checkpointIdentifier))
            {
                throw new ArgumentException(
                    "A Movie v1 checkpoint identifier is required.",
                    nameof(checkpointIdentifier));
            }

            BeforeMovieTick = beforeMovieTick;
            CheckpointIdentifier = checkpointIdentifier;
        }

        public long BeforeMovieTick { get; }
        public string CheckpointIdentifier { get; }
    }

    public sealed class RuntimeReplayJournal : IDisposable
    {
        private const int SegmentRecordCount = 256;
        private const int BaselineSemanticStableFrameCount = 3;
        private readonly string manifestSha256;
        private readonly string journalRoot;
        private readonly Action<string> logDebug;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly IBaselineBundleProvider baselineProvider;
        private readonly RuntimeSnapshotCapture snapshotCapture;
        private readonly HeroActionRecorder recorder = new HeroActionRecorder();
        private readonly SceneEpochTracker sceneTracker;
        private readonly List<PersistedSegment> persistedSegments =
            new List<PersistedSegment>();
        private readonly List<RuntimeReplayJournalCommand> commands =
            new List<RuntimeReplayJournalCommand>();
        private ReplayJournal? journal;
        private SnapshotCaptureResult? baselineSemanticCapture;
        private BaselineCaptureResult? baselineBundleCapture;
        private string? currentDirectory;
        private bool started;
        private bool disposed;
        private bool pendingBaseline;
        private string loadRequestedBoundary = string.Empty;
        private string loadCompletedBoundary = string.Empty;
        private string firstAnchorBoundary = string.Empty;
        private bool suspended;
        private bool playbackCaptureActive;
        private string playbackCaptureError = string.Empty;
        private ulong? lastPlaybackRawInputTick;
        private int currentSaveSlot;
        private readonly List<ReplayLifecycleRecord> lifecycleRecords = new List<ReplayLifecycleRecord>();
        private ReplayLifecycleRecord? activeLifecycle;
        private ReplayLifecycleCursor? replayLifecycleWindow;
        private readonly Dictionary<string, byte[]> replayLifecycleObjects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private NativeLoadHookProgress? replayLifecycleLoad;
        private NativeLoadHookProgress? lifecycleLoad;
        private int lifecycleStartFrame;
        private readonly Dictionary<string, int> lifecycleSlotSizes = new Dictionary<string, int>(StringComparer.Ordinal);
        private const long MaximumLifecycleSlotStorageBytes = 64L * 1024 * 1024;
        private int baselineSequence;
        private int nextSegmentSequence;
        private long visualTick;
        private long fixedTick;
        private ulong currentInputTick;
        private TickStamp? lastCommittedStamp;
        private float nextBaselineRetryRealtime;
        private float nextBaselineCaptureWarningRealtime;
        private int baselineSemanticStableFrames;
        private int lastBaselineCaptureVisualFrame = -1;
        private bool baselineCaptureBoundaryRegistered;
        private readonly RuntimeRecordingRootCoordinator? sourceRecordingRoot;
        private System.Diagnostics.Stopwatch? sourcePreparationElapsed;
        private bool sourceRecordingRootReady;
        private bool coldRecordingRootVerified;
        private string sourceRecordingRootError = string.Empty;
        private string? baselineCandidateSha256;
        private SnapshotCaptureResult? baselineCandidateCapture;
        private string previousSegmentSha256 =
            ReplayJournalSegment.GenesisPreviousSha256;

        private RuntimeInitialRespawnPreparation? initialRespawnPreparation;
        internal string InitialRespawnPreparationError => initialRespawnPreparation?.Error ?? string.Empty;
        internal string InitialRespawnPreparationStatus => initialRespawnPreparation?.Status ?? "NotConfigured";
        internal int InitialRespawnInsertedWaitFrames => initialRespawnPreparation?.InsertedWaitFrames ?? 0;
        internal int InitialRespawnPreparationStartFrame => initialRespawnPreparation?.PreparationStartFrame ?? -1;
        internal int InitialRespawnNativeStartFrame => initialRespawnPreparation?.NativeStartFrame ?? -1;

        internal void PersistRestoreFailure(string phase, string reason)
        {
            // The supervisor can terminate the target immediately after the
            // failure event. Buffered ModLog/events are not a durable report.
            // One bounded file per session; never copy a trace or slot contents.
            try
            {
                var text = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + "\nphase=" + phase + "\n" + reason;
                if (text.Length > 16384) text = text.Substring(0, 16384) + "\n[truncated]";
                using var stream = new FileStream(Path.Combine(journalRoot, "last-restore-failure.txt"),
                    FileMode.Create, FileAccess.Write, FileShare.Read);
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            catch (Exception exception)
            {
                logWarning("Could not persist restore failure: " + exception.Message);
            }
        }

        internal void PrepareInitialRespawn()
        {
            initialRespawnPreparation ??= new RuntimeInitialRespawnPreparation(error =>
            {
                sourceRecordingRootError = error;
                logError(error);
            });
            initialRespawnPreparation.Arm();
        }

        public RuntimeReplayJournal(
            string sessionDirectory,
            string manifestSha256,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError,
            IBaselineBundleProvider? baselineProvider = null,
            RuntimeSnapshotCapture? baselineSnapshotCapture = null)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
            {
                throw new ArgumentException(
                    "A session directory is required.",
                    nameof(sessionDirectory));
            }

            this.manifestSha256 = manifestSha256
                                  ?? throw new ArgumentNullException(
                                      nameof(manifestSha256));
            this.logDebug = logDebug ?? throw new ArgumentNullException(nameof(logDebug));
            this.logWarning = logWarning
                              ?? throw new ArgumentNullException(nameof(logWarning));
            this.logError = logError ?? throw new ArgumentNullException(nameof(logError));
            this.baselineProvider = baselineProvider
                                    ?? new DesktopSaveSlotBaselineProvider();
            snapshotCapture = baselineSnapshotCapture
                              ?? ReplaySaveSnapshotCapture.Create();
            journalRoot = Path.Combine(sessionDirectory, "journal");
            Directory.CreateDirectory(journalRoot);
            sceneTracker = new SceneEpochTracker(_ => { }, (_, __, ___) => { });
            var startup = new RuntimeStartupProfileAttestor();
            if (startup.RunId.StartsWith("interactive-", StringComparison.Ordinal))
                sourceRecordingRoot = new RuntimeRecordingRootCoordinator(startup, startup.RunId,
                    chooseDynamicRoot: true);
        }

        public string RecordingOriginStatus => sourceRecordingRootError.Length == 0
            && (activeLifecycle != null || replayLifecycleWindow != null)
            ? "LifecycleActive"
            : coldRecordingRootVerified
            ? (IsAvailable ? "Ready" : "Faulted")
            : sourceRecordingRootError.Length != 0 ? "Faulted"
            : sourceRecordingRoot == null ? "Unmanaged"
            : sourceRecordingRootReady && IsAvailable ? "Ready" : "Preparing";
        public string RecordingOriginDetail => sourceRecordingRootError.Length == 0
            && (activeLifecycle != null || replayLifecycleWindow != null)
            ? "Native lifecycle operation is active; wait for its completed boundary."
            : coldRecordingRootVerified
            ? (IsAvailable ? "Persisted cold recording origin and semantic baseline were verified."
                : "Verified cold recording journal is no longer available.")
            : sourceRecordingRootError.Length != 0
                ? sourceRecordingRootError : sourceRecordingRoot?.Detail ?? string.Empty;
        internal bool CanPrepareRecordingOrigin => IsSupportedBaselineAnchor();
        internal int NextLifecycleSequence => lifecycleRecords.Count;
        internal int? ActiveLifecycleSequence => activeLifecycle?.Sequence;
        internal bool IsLifecycleDestinationReady => activeLifecycle != null
            && (activeLifecycle.Kind == ReplayLifecycleKind.ReturnToMenu
                ? HollowKnightTAS.Runtime.Control.RuntimePauseController.IsStableTitleMenu()
                : lifecycleLoad?.IsComplete == true
                    && currentSaveSlot == activeLifecycle.Slot && IsSupportedGameplay());
        internal bool CanLoadInitialGameSlot => sourceRecordingRoot != null
            && !sourceRecordingRoot.PreparationStarted && !sourceRecordingRootReady
            && sourceRecordingRootError.Length == 0 && currentSaveSlot == 0
            && LastCommittedMovieTick < 0;

        public bool IsAvailable =>
            !disposed
            && activeLifecycle == null && replayLifecycleWindow == null
            && sourceRecordingRootError.Length == 0
            && !pendingBaseline
            && journal != null
            && !journal.HasGap;
        public bool HasGap => journal?.HasGap == true;
        public bool PlaybackCaptureActive => playbackCaptureActive;
        public string PlaybackCaptureError => playbackCaptureError;
        public string? BaselineId => journal?.BaselineId;
        public string? BaselineSha256 => journal?.BaselineSha256;
        public long LastCommittedMovieTick =>
            journal?.LastCommittedMovieTick ?? -1;
        public long LastPersistedMovieTick =>
            journal?.LastPersistedMovieTick ?? -1;
        public string? CurrentDirectory => currentDirectory;
        public int CurrentSaveSlot => currentSaveSlot;
        public SnapshotCaptureResult? BaselineSemanticCapture =>
            baselineSemanticCapture;
        public BaselineCaptureResult? BaselineBundleCapture =>
            baselineBundleCapture;
        public string JournalHeadSha256 => previousSegmentSha256;
        public int PersistedSegmentCount => persistedSegments.Count;
        public TickStamp? LastCommittedStamp => lastCommittedStamp;
        public InputSample? LastCommittedSample => journal?.LastCommittedSample;
        public long CurrentVisualTick => visualTick;
        public long CurrentFixedTick => fixedTick;
        public int CurrentSceneEpoch => sceneTracker.CurrentEpoch;

        public event Action<long, InputSample, TickStamp>? MovieTickCommitted;
        public event Action<string, string>? BaselineEstablished;

        public bool BeginPlaybackCapture(out string error)
        {
            var current = journal;
            if (disposed
                || pendingBaseline
                || current == null
                || current.HasGap)
            {
                error = "Shadow journal has no usable replay baseline.";
                playbackCaptureError = error;
                return false;
            }

            if (playbackCaptureActive)
            {
                error = "Playback journal capture is already active.";
                playbackCaptureError = error;
                return false;
            }

            current.Suspend();
            playbackCaptureActive = true;
            playbackCaptureError = string.Empty;
            lastPlaybackRawInputTick = null;
            WriteEvent("playback-capture-begin", current.BaselineId);
            logDebug(
                "T06 shadow journal switched to replay-observation capture.");
            error = string.Empty;
            return true;
        }

        public bool AppendPlaybackObservation(
            ReplayInputObservation observation,
            out string error)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            var current = journal;
            if (disposed
                || !playbackCaptureActive
                || current == null
                || current.HasGap)
            {
                error = "Playback journal capture is unavailable.";
                playbackCaptureError = error;
                return false;
            }

            // HeroActionReplayer applies one synthetic neutral input update to
            // restore bindings/control after the final movie sample. That
            // cleanup is deliberately outside the movie timeline and must not
            // create an authoritative journal tick.
            if (observation.IsReleaseBoundary)
            {
                error = string.Empty;
                return true;
            }

            if (replayLifecycleWindow != null)
            {
                error = "Movie input crossed an unfinished replay lifecycle boundary.";
                FailReplayLifecycle(error);
                return false;
            }

            try
            {
                if (lastPlaybackRawInputTick.HasValue
                    && observation.RawInputTick
                       != lastPlaybackRawInputTick.Value + 1)
                {
                    // Replay intentionally skips raw input updates while its
                    // gameplay/transition gate is closed. Reset continuity so
                    // those skipped updates do not become movie frames or a
                    // false persistence gap.
                    current.Suspend();
                }

                var expected = observation.Expected;
                var sample = new InputSample(
                    observation.RawInputTick,
                    expected.Held,
                    expected.Pressed,
                    expected.Released,
                    expected.AxisX,
                    expected.AxisY);
                var stamp = new TickStamp(
                    observation.RawInputTick,
                    visualTick,
                    fixedTick,
                    sceneTracker.CurrentEpoch,
                    TickPhase.InControlCommitted);
                var append = current.Append(sample, stamp);
                if (!append.Success)
                {
                    WriteEvent("journal-gap", append.Error);
                    logWarning(
                        "T06 replay-observation journal gap: "
                        + append.Error);
                    error = append.Error;
                    playbackCaptureError = error;
                    return false;
                }

                lastPlaybackRawInputTick = observation.RawInputTick;
                lastCommittedStamp = stamp;
                MovieTickCommitted?.Invoke(
                    append.MovieTick,
                    sample,
                    stamp);
                if (current.RetainedCount >= SegmentRecordCount)
                {
                    Flush();
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                current.MarkPersistenceFailure(exception.Message);
                WriteEvent(
                    "journal-fault",
                    "playback-observation:"
                    + exception.GetType().Name
                    + ": "
                    + exception.Message);
                logError(
                    "T06 replay-observation journal failed: "
                    + exception);
                error = exception.Message;
                playbackCaptureError = error;
                return false;
            }
        }

        public void EndPlaybackCapture()
        {
            if (replayLifecycleWindow != null)
                FailReplayLifecycle("Playback capture ended during a native lifecycle operation.");
            if (!playbackCaptureActive)
            {
                return;
            }

            // Playback samples bypass Capture(), so its predecessor must be
            // synchronized before the next raw sample derives button edges.
            // This changes recorder bookkeeping only, never HeroActions state.
            recorder.Reset(LastCommittedSample?.Held ?? TasAction.None);
            playbackCaptureActive = false;
            lastPlaybackRawInputTick = null;
            journal?.Suspend();
            WriteEvent(
                "playback-capture-end",
                journal?.LastCommittedMovieTick.ToString(
                    CultureInfo.InvariantCulture)
                ?? "-1");
            logDebug(
                "T06 shadow journal returned to raw input capture.");
        }

        public bool RecordForcedSceneTransition(
            ForcedSceneTransitionSpec spec,
            out string error)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var current = journal;
            if (disposed || current == null || current.HasGap)
            {
                error = "Shadow journal is unavailable.";
                return false;
            }

            try
            {
                var beforeTick = checked(
                    current.LastCommittedMovieTick + 1);
                var identifier =
                    ForcedSceneTransitionCommand.Encode(spec);
                if (commands.Any(
                        value => value.BeforeMovieTick == beforeTick))
                {
                    error =
                        "A replay command is already registered at movie tick "
                        + beforeTick
                        + ".";
                    return false;
                }

                commands.Add(
                    new RuntimeReplayJournalCommand(
                        beforeTick,
                        identifier));
                WriteEvent(
                    "forced-scene-transition",
                    "beforeMovieTick="
                    + beforeTick
                    + ";scene="
                    + spec.SceneName);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            if (sourceRecordingRoot != null) PrepareInitialRespawn();
            ModHooks.SavegameLoadHook += OnSavegameLoad;
            ModHooks.AfterSavegameLoadHook += OnAfterSavegameLoad;
            ModHooks.NewGameHook += OnNewGame;
            InputManager.OnUpdate += OnInputManagerUpdated;
            sceneTracker.Start();
            pendingBaseline = true;
        }

        public void Flush()
        {
            var current = journal;
            if (current == null
                || current.HasGap
                || current.LastCommittedMovieTick <= current.LastPersistedMovieTick)
            {
                return;
            }

            try
            {
                var slice = current.CommitThrough(current.LastCommittedMovieTick);
                PersistSlice(slice);
                current.AcknowledgePersistedThrough(slice.LastMovieTick);
                WriteIndex();
            }
            catch (Exception exception)
            {
                current.MarkPersistenceFailure(exception.Message);
                WriteEvent(
                    "persistence-failure",
                    exception.GetType().Name + ": " + exception.Message);
                logError("T06 shadow journal persistence failed: " + exception);
            }
        }

        public RuntimeReplayJournalFreezeResult FreezeThrough(
            long effectiveMovieTick)
            => Freeze(effectiveMovieTick, includeLifecycle: false);

        internal RuntimeReplayJournalFreezeResult FreezeForReplaySave(long effectiveMovieTick)
            => Freeze(effectiveMovieTick, includeLifecycle: true);

        private RuntimeReplayJournalFreezeResult Freeze(long effectiveMovieTick, bool includeLifecycle)
        {
            if (activeLifecycle != null || replayLifecycleWindow != null || (!includeLifecycle && lifecycleRecords.Count != 0))
                return RuntimeReplayJournalFreezeResult.Failed(
                    "An active lifecycle cannot be frozen; completed operations require lifecycle-aware replay-save export.");
            if (!IsAvailable) return RuntimeReplayJournalFreezeResult.Failed("Journal origin is unavailable.");
            var current = journal;
            if (current == null)
            {
                return RuntimeReplayJournalFreezeResult.Failed(
                    "Shadow journal has no established baseline.");
            }

            if (current.HasGap)
            {
                return RuntimeReplayJournalFreezeResult.Failed(
                    "Shadow journal contains a gap.");
            }

            if (effectiveMovieTick != current.LastCommittedMovieTick)
            {
                return RuntimeReplayJournalFreezeResult.Failed(
                    "A replay save can freeze only the latest committed safe tick.");
            }

            if (baselineBundleCapture?.Success != true
                || baselineBundleCapture.CanonicalBytes == null)
            {
                return RuntimeReplayJournalFreezeResult.Failed(
                    "Raw desktop baseline bundle is unavailable.");
            }

            Flush();
            if (current.HasGap
                || current.LastPersistedMovieTick != effectiveMovieTick)
            {
                return RuntimeReplayJournalFreezeResult.Failed(
                    "Journal prefix could not be durably persisted through the target tick.");
            }

            try
            {
                var bytes = new List<byte[]>(persistedSegments.Count);
                var expectedSequence = 0;
                var expectedFirstTick = 0L;
                var previous = ReplayJournalSegment.GenesisPreviousSha256;
                foreach (var persisted in persistedSegments)
                {
                    var value = File.ReadAllBytes(persisted.Path);
                    var actual = Sha256Utility.ComputeHex(value);
                    var segment = ReplayJournalSegmentCodec.Deserialize(value);
                    if (!string.Equals(
                            actual,
                            persisted.Sha256,
                            StringComparison.Ordinal)
                        || segment.Sequence != expectedSequence
                        || segment.FirstMovieTick != expectedFirstTick
                        || !string.Equals(
                            segment.PreviousObjectSha256,
                            previous,
                            StringComparison.Ordinal))
                    {
                        return RuntimeReplayJournalFreezeResult.Failed(
                            "Persisted journal segment chain failed verification.");
                    }

                    bytes.Add(value);
                    previous = actual;
                    expectedSequence++;
                    expectedFirstTick = checked(segment.LastMovieTick + 1);
                }

                if (bytes.Count == 0
                    || expectedFirstTick - 1 != effectiveMovieTick
                    || !string.Equals(
                        previous,
                        previousSegmentSha256,
                        StringComparison.Ordinal))
                {
                    return RuntimeReplayJournalFreezeResult.Failed(
                        "Persisted journal prefix does not end at the target tick.");
                }

                ReplayLifecycleLog? lifecycle = null;
                var lifecycleObjects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                if (includeLifecycle && lifecycleRecords.Count != 0)
                {
                    lifecycle = new ReplayLifecycleLog(Sha256Utility.ComputeHex(baselineBundleCapture.CanonicalBytes), lifecycleRecords);
                    lifecycle.VerifySlotObjects(hash =>
                    {
                        if (!replayLifecycleObjects.TryGetValue(hash, out var content))
                        {
                            var path = Path.Combine(currentDirectory!, "lifecycle-objects", hash);
                            if (!File.Exists(path) || new FileInfo(path).Length > ReplayLifecycleLog.MaximumSlotBytes) return null;
                            content = File.ReadAllBytes(path);
                        }
                        lifecycleObjects[hash] = (byte[])content.Clone();
                        return content;
                    });
                }
                return RuntimeReplayJournalFreezeResult.Succeeded(
                    effectiveMovieTick,
                    current.BaselineId,
                    current.BaselineSha256,
                    baselineBundleCapture.CanonicalBytes,
                    bytes,
                    commands.Where(
                        value => value.BeforeMovieTick
                                 <= effectiveMovieTick),
                    previous, lifecycle, lifecycleObjects);
            }
            catch (Exception exception)
            {
                current.MarkPersistenceFailure(exception.Message);
                return RuntimeReplayJournalFreezeResult.Failed(
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            initialRespawnPreparation?.Dispose();
            ReleaseSourcePreparationInput();
            Flush();
            if (started)
            {
                ModHooks.SavegameLoadHook -= OnSavegameLoad;
                ModHooks.AfterSavegameLoadHook -= OnAfterSavegameLoad;
                ModHooks.NewGameHook -= OnNewGame;
                InputManager.OnUpdate -= OnInputManagerUpdated;
                sceneTracker.Dispose();
                started = false;
            }
            DisableBaselineCaptureBoundary();
        }

        private void TryEstablishBaselineAtCompletedFrameBoundary()
        {
            if (disposed)
            {
                return;
            }

            if (!pendingBaseline && journal != null)
            {
                ResetBaselineCandidate();
                return;
            }

            if (sourceRecordingRootError.Length != 0) return;
            // Include post-handshake semantic stabilization in the input lease
            // deadline. Unity time is normalized during preparation, so use a
            // monotonic wall clock independent of the recording clock.
            if (sourcePreparationElapsed?.Elapsed.TotalSeconds >= 120d)
            {
                sourceRecordingRootError = "Recording origin preparation timed out; physical input has been released. Restart the TAS session before recording.";
                ReleaseSourcePreparationInput();
                ResetBaselineCandidate();
                logError(sourceRecordingRootError);
                return;
            }
            if (sourceRecordingRoot?.PreparationStarted == true && !sourceRecordingRootReady)
            {
                if (!AdvanceSourceRecordingRoot()) return;
            }

            if (!IsSupportedBaselineAnchor())
            {
                ResetBaselineCandidate();
                return;
            }

            if (Time.realtimeSinceStartup < nextBaselineRetryRealtime)
            {
                ResetBaselineCandidate();
                return;
            }

            if (firstAnchorBoundary.Length == 0)
                firstAnchorBoundary = DescribeLoadBoundary();

            // Prepare at the first supported anchor on BOTH source and target.
            // Waiting for rendered semantic stability first lets the source
            // settle with a different pre-calibration physics remainder.
            if (sourceRecordingRoot != null && !sourceRecordingRootReady
                && !AdvanceSourceRecordingRoot()) return;

            var stamp = new TickStamp(
                currentInputTick,
                visualTick,
                fixedTick,
                sceneTracker.CurrentEpoch,
                TickPhase.LateUpdateEnd);
            var capture = snapshotCapture.Capture(stamp);
            if (!capture.Success)
            {
                ResetBaselineCandidate();
                if (Time.realtimeSinceStartup
                    >= nextBaselineCaptureWarningRealtime)
                {
                    nextBaselineCaptureWarningRealtime =
                        Time.realtimeSinceStartup + 1f;
                    logWarning(
                        "T06 shadow journal baseline capture unavailable at "
                        + capture.FailedProbeId
                        + ": "
                        + capture.Error);
                }

                return;
            }

            if (!string.Equals(
                    baselineCandidateSha256,
                    capture.Sha256,
                    StringComparison.Ordinal))
            {
                baselineCandidateSha256 = capture.Sha256;
                baselineCandidateCapture = capture;
                baselineSemanticStableFrames = 1;
                return;
            }

            baselineCandidateCapture = capture;
            baselineSemanticStableFrames = Math.Min(
                BaselineSemanticStableFrameCount,
                baselineSemanticStableFrames + 1);
            if (baselineSemanticStableFrames
                >= BaselineSemanticStableFrameCount)
            {
                EstablishBaseline(baselineCandidateCapture);
            }
        }

        private bool AdvanceSourceRecordingRoot()
        {
            try
            {
                if (sourcePreparationElapsed == null)
                    sourcePreparationElapsed = System.Diagnostics.Stopwatch.StartNew();
                sourceRecordingRootReady = sourceRecordingRoot!.Advance();
                return sourceRecordingRootReady;
            }
            catch (Exception exception)
            {
                sourceRecordingRootError = exception.Message;
                ReleaseSourcePreparationInput();
                logError("Recording origin preparation failed: " + sourceRecordingRootError);
                return false;
            }
        }

        // Internal only until the command boundary pump owns the complete
        // native operation. Slot bytes must be captured/verified before Begin.
        internal void BeginLifecycleOperation(ReplayLifecycleRecord request, MovieDocument sourceMovie, byte[]? slotBytes, byte[]? moddedBytes = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (sourceMovie == null) throw new ArgumentNullException(nameof(sourceMovie));
            if (disposed || playbackCaptureActive || activeLifecycle != null || journal == null || journal.HasGap
                || pendingBaseline || sourceRecordingRootError.Length != 0
                || (!sourceRecordingRootReady && !coldRecordingRootVerified)
                || request.Outcome != ReplayLifecycleOutcome.Waiting
                || request.Sequence != lifecycleRecords.Count
                || request.AfterMovieTick != journal.LastCommittedMovieTick)
                throw new InvalidOperationException("Journal cannot begin this lifecycle operation.");
            // Validate the complete sequence before changing journal state.
            var root = baselineBundleCapture?.CanonicalBytes
                ?? throw new InvalidOperationException("Lifecycle root bundle is unavailable.");
            var pendingLog = new ReplayLifecycleLog(Sha256Utility.ComputeHex(root), lifecycleRecords.Concat(new[] { request }));
            if (sourceMovie.Header.ManifestSha256 != manifestSha256
                || sourceMovie.Header.BaselineId != journal.BaselineId
                || sourceMovie.Header.BaselineSha256 != journal.BaselineSha256)
                throw new InvalidOperationException("Lifecycle source movie does not belong to the current journal.");
            pendingLog.VerifyInputPrefixes(sourceMovie);
            if (currentDirectory == null) throw new InvalidOperationException("Lifecycle journal directory is unavailable.");
            var objectDirectory = Path.Combine(currentDirectory, "lifecycle-objects");
            var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (request.Kind == ReplayLifecycleKind.LoadSlot)
            {
                VerifyLifecycleModdedSlot(request);
                if (slotBytes == null || slotBytes.Length == 0 || slotBytes.Length > ReplayLifecycleLog.MaximumSlotBytes)
                    throw new InvalidDataException("Lifecycle slot bytes are missing or too large.");
                var capturedSlot = (byte[])slotBytes.Clone();
                if (Sha256Utility.ComputeHex(capturedSlot) != request.SlotObjectSha256)
                    throw new InvalidDataException("Lifecycle slot bytes do not match the operation.");
                captured.Add(request.SlotObjectSha256, capturedSlot);
                if (request.ModdedSlotObjectSha256 == null
                    || (moddedBytes == null ? request.ModdedSlotObjectSha256 != string.Empty
                        : moddedBytes.Length > ReplayLifecycleLog.MaximumSlotBytes
                          || Sha256Utility.ComputeHex(moddedBytes) != request.ModdedSlotObjectSha256))
                    throw new InvalidDataException("Lifecycle modded slot capture is missing or mismatched.");
                if (moddedBytes != null) captured[request.ModdedSlotObjectSha256] = (byte[])moddedBytes.Clone();
            }
            else if (slotBytes != null || moddedBytes != null)
                throw new ArgumentException("Return-to-menu does not accept slot bytes.", nameof(slotBytes));
            if (lifecycleSlotSizes.Values.Sum(size => (long)size)
                + captured.Where(item => !lifecycleSlotSizes.ContainsKey(item.Key)).Sum(item => (long)item.Value.Length)
                > MaximumLifecycleSlotStorageBytes)
                throw new InvalidOperationException("Lifecycle slot storage budget is exhausted; no native operation was started.");
            Flush();
            if (journal.HasGap || journal.LastPersistedMovieTick != request.AfterMovieTick)
                throw new InvalidOperationException("Input prefix could not be persisted before the lifecycle operation.");
            Directory.CreateDirectory(objectDirectory);
            foreach (var item in captured)
            {
                var slotPath = Path.Combine(objectDirectory, item.Key);
                if (File.Exists(slotPath))
                {
                    if (Sha256Utility.ComputeFileHex(slotPath) != item.Key)
                        throw new InvalidDataException("Existing lifecycle slot object is corrupt.");
                }
                else WriteAtomic(slotPath, item.Value);
                lifecycleSlotSizes[item.Key] = item.Value.Length;
            }
            WriteAtomic(Path.Combine(currentDirectory, "lifecycle.bin"), pendingLog.Serialize());
            activeLifecycle = request;
            lifecycleLoad = request.Kind == ReplayLifecycleKind.LoadSlot ? new NativeLoadHookProgress(request.Slot) : null;
            lifecycleStartFrame = Time.frameCount;
            Suspend("native-lifecycle-requested");
        }

        internal void CompleteLifecycleOperation(int sequence)
        {
            var request = activeLifecycle;
            if (request == null || request.Sequence != sequence || sourceRecordingRootError.Length != 0)
                throw new InvalidOperationException("Lifecycle completion does not match the active journal operation.");
            if (journal?.LastCommittedMovieTick != request.AfterMovieTick)
                throw new InvalidOperationException("Input advanced during the native lifecycle operation.");
            if (request.Kind == ReplayLifecycleKind.LoadSlot
                && (lifecycleLoad?.IsComplete != true || currentSaveSlot != request.Slot))
                throw new InvalidOperationException("Native load hooks have not completed for the requested slot.");
            if (request.Kind == ReplayLifecycleKind.ReturnToMenu
                ? !HollowKnightTAS.Runtime.Control.RuntimePauseController.IsStableTitleMenu()
                : !IsSupportedGameplay())
                throw new InvalidOperationException("Native lifecycle has not reached its destination state.");
            var elapsed = (long)Time.frameCount - lifecycleStartFrame;
            var completed = new ReplayLifecycleRecord(request.Sequence, request.AfterMovieTick,
                request.InputPrefixSha256, request.Kind, request.Slot, request.SlotObjectSha256,
                ReplayLifecycleOutcome.Completed, elapsed, string.Empty, request.ModdedSlotObjectSha256);
            var root = baselineBundleCapture?.CanonicalBytes
                ?? throw new InvalidOperationException("Lifecycle root bundle is unavailable.");
            var completeLog = new ReplayLifecycleLog(Sha256Utility.ComputeHex(root), lifecycleRecords.Concat(new[] { completed }));
            WriteAtomic(Path.Combine(currentDirectory!, "lifecycle.bin"), completeLog.Serialize());
            lifecycleRecords.Add(completed);
            activeLifecycle = null;
            journal?.Suspend();
            suspended = true;
            WriteEvent("native-lifecycle-completed", "sequence=" + sequence + ";frames=" + elapsed);
        }

        internal void FailLifecycleOperation(int sequence, string reason)
        {
            if (activeLifecycle == null || activeLifecycle.Sequence != sequence)
                throw new InvalidOperationException("Lifecycle failure does not match the active journal operation.");
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Failure reason is required.", nameof(reason));
            sourceRecordingRootError = "Native lifecycle failed: " + reason;
            Suspend("native-lifecycle-failed");
            try
            {
                var request = activeLifecycle;
                var failure = reason.Substring(0, Math.Min(reason.Length, 128));
                var failed = new ReplayLifecycleRecord(request.Sequence, request.AfterMovieTick,
                    request.InputPrefixSha256, request.Kind, request.Slot, request.SlotObjectSha256,
                    ReplayLifecycleOutcome.Failed, Math.Max(0L, (long)Time.frameCount - lifecycleStartFrame), failure, request.ModdedSlotObjectSha256);
                var root = baselineBundleCapture?.CanonicalBytes
                    ?? throw new InvalidOperationException("Lifecycle root bundle is unavailable.");
                WriteAtomic(Path.Combine(currentDirectory!, "lifecycle.bin"),
                    new ReplayLifecycleLog(Sha256Utility.ComputeHex(root), lifecycleRecords.Concat(new[] { failed })).Serialize());
            }
            catch (Exception exception)
            {
                // A durable Waiting record remains non-replayable if the disk
                // fails while recording failure; never turn it into Completed.
                logError("Lifecycle failure record could not be persisted: " + exception.Message);
            }
            WriteEvent("native-lifecycle-failed", "sequence=" + sequence + ";reason=" + reason);
            logError(sourceRecordingRootError);
        }

        internal static void VerifyLifecycleModdedSlot(ReplayLifecycleRecord request)
        {
            if (request.Kind != ReplayLifecycleKind.LoadSlot) return;
            if (request.ModdedSlotObjectSha256 == null)
                throw new InvalidDataException("Legacy lifecycle did not capture modded slot settings.");
            var path = SavePathResolver.Current.GetSlotPath(request.Slot, ".modded.json");
            if (request.ModdedSlotObjectSha256.Length == 0)
            {
                if (File.Exists(path)) throw new InvalidDataException("Modded slot was absent at capture but now exists.");
            }
            else if (!File.Exists(path) || new FileInfo(path).Length > ReplayLifecycleLog.MaximumSlotBytes
                || Sha256Utility.ComputeFileHex(path) != request.ModdedSlotObjectSha256)
                throw new InvalidDataException("Modded slot differs from captured lifecycle settings.");
        }

        internal void BeginReplayLifecycle(ReplayLifecycleCursor cursor, IReadOnlyDictionary<string, byte[]> objects)
        {
            var request = cursor?.Active;
            if (disposed || !playbackCaptureActive || pendingBaseline || journal == null || journal.HasGap
                || activeLifecycle != null || replayLifecycleWindow != null || request == null
                || cursor!.Failure.Length != 0 || playbackCaptureError.Length != 0
                || sourceRecordingRootError.Length != 0
                || request.AfterMovieTick != LastCommittedMovieTick || request.Sequence != lifecycleRecords.Count)
                throw new InvalidOperationException("Replay lifecycle requires the claimed operation at its captured input boundary.");
            if (request.Kind == ReplayLifecycleKind.LoadSlot)
            {
                var requiredHashes = new[] { request.SlotObjectSha256, request.ModdedSlotObjectSha256 }
                    .Where(hash => !string.IsNullOrEmpty(hash)).Select(hash => hash!).Distinct(StringComparer.Ordinal).ToArray();
                var total = replayLifecycleObjects.Values.Sum(bytes => (long)bytes.Length);
                foreach (var hash in requiredHashes)
                {
                    if (!objects.TryGetValue(hash, out var bytes) || bytes == null)
                        throw new InvalidDataException("Replay lifecycle object is missing.");
                    if (!replayLifecycleObjects.ContainsKey(hash)) total += bytes.Length;
                }
                if (total > MaximumLifecycleSlotStorageBytes)
                    throw new InvalidDataException("Replay lifecycle slot storage budget is exhausted.");
                foreach (var hash in requiredHashes)
                {
                    if (!objects.TryGetValue(hash, out var bytes) || bytes.Length > ReplayLifecycleLog.MaximumSlotBytes
                        || Sha256Utility.ComputeHex(bytes) != hash)
                        throw new InvalidDataException("Replay lifecycle object is missing or changed.");
                    replayLifecycleObjects[hash] = (byte[])bytes.Clone();
                }
            }
            replayLifecycleWindow = cursor;
            replayLifecycleLoad = request.Kind == ReplayLifecycleKind.LoadSlot ? new NativeLoadHookProgress(request.Slot) : null;
            journal.Suspend();
            lastPlaybackRawInputTick = null;
        }

        internal bool ReplayLifecycleDestinationReady => replayLifecycleWindow?.Active is ReplayLifecycleRecord request
            && replayLifecycleWindow.Failure.Length == 0 && playbackCaptureError.Length == 0
            && (request.Kind == ReplayLifecycleKind.ReturnToMenu
                ? HollowKnightTAS.Runtime.Control.RuntimePauseController.IsStableTitleMenu()
                : replayLifecycleLoad?.IsComplete == true
                    && currentSaveSlot == request.Slot && IsSupportedGameplay());

        internal void CompleteReplayLifecycle(ReplayLifecycleCursor cursor)
        {
            if (!ReferenceEquals(cursor, replayLifecycleWindow) || !ReplayLifecycleDestinationReady
                || cursor.Active?.AfterMovieTick != LastCommittedMovieTick)
                throw new InvalidOperationException("Replay lifecycle has not reached its matching completed boundary.");
            var record = cursor.Active!;
            var completed = cursor.Complete(record.Sequence, Time.frameCount);
            // Preserve the lifecycle marker so the restored journal cannot be
            // exported as an ordinary frame-only recording.
            lifecycleRecords.Add(completed);
            replayLifecycleWindow = null;
            journal!.Suspend();
            lastPlaybackRawInputTick = null;
        }

        private void FailReplayLifecycle(string reason)
        {
            playbackCaptureError = reason;
            sourceRecordingRootError = "Replay lifecycle failed: " + reason;
            var cursor = replayLifecycleWindow;
            if (cursor?.Active != null && cursor.Failure.Length == 0)
                cursor.Fail(cursor.Active.Sequence, reason);
        }

        private void OnSavegameLoad(int slot)
        {
            if (replayLifecycleWindow != null)
            {
                var request = replayLifecycleWindow.Active;
                try
                {
                    if (request?.Kind != ReplayLifecycleKind.LoadSlot || request.Slot != slot
                        || replayLifecycleLoad == null || replayLifecycleWindow.Failure.Length != 0)
                        throw new InvalidDataException("Unexpected or repeated native load during lifecycle replay.");
                    var path = SavePathResolver.Current.GetSlotPath(slot, ".dat");
                    var length = new FileInfo(path).Length;
                    if (length <= 0 || length > ReplayLifecycleLog.MaximumSlotBytes
                        || Sha256Utility.ComputeFileHex(path) != request.SlotObjectSha256)
                        throw new InvalidDataException("Replay slot bytes differ from the lifecycle object.");
                    replayLifecycleLoad.ObserveSlotConfirmed(slot);
                    currentSaveSlot = slot;
                    loadRequestedBoundary = DescribeLoadBoundary();
                    Suspend("replay-lifecycle-load-requested");
                    return;
                }
                catch (Exception exception) { FailReplayLifecycle(exception.Message); }
                // Unexpected loads retain the existing root invalidation path.
            }
            if (activeLifecycle != null)
            {
                if (activeLifecycle.Kind == ReplayLifecycleKind.LoadSlot
                    && activeLifecycle.Slot == slot && lifecycleLoad != null)
                {
                    try
                    {
                        var slotPath = SavePathResolver.Current.GetSlotPath(slot, ".dat");
                        var length = new FileInfo(slotPath).Length;
                        if (length <= 0 || length > ReplayLifecycleLog.MaximumSlotBytes
                            || Sha256Utility.ComputeFileHex(slotPath) != activeLifecycle.SlotObjectSha256)
                            throw new InvalidDataException("Native slot changed after lifecycle preparation.");
                        lifecycleLoad.ObserveSlotConfirmed(slot);
                        currentSaveSlot = slot;
                        loadRequestedBoundary = DescribeLoadBoundary();
                        Suspend("controlled-savegame-load-requested");
                        return;
                    }
                    catch (Exception exception)
                    {
                        FailLifecycleOperation(activeLifecycle.Sequence, exception.Message);
                    }
                }
                else FailLifecycleOperation(activeLifecycle.Sequence,
                    "An unexpected or repeated save load interrupted the lifecycle operation.");
            }
            loadRequestedBoundary = DescribeLoadBoundary();
            loadCompletedBoundary = string.Empty;
            firstAnchorBoundary = string.Empty;
            InvalidateSourceRecordingRootForLoad();
            currentSaveSlot = slot;
            pendingBaseline = true;
            ResetBaselineCandidate();
            Suspend("savegame-load-requested");
        }

        private void OnAfterSavegameLoad(SaveGameData data)
        {
            if (replayLifecycleWindow != null)
            {
                try
                {
                    if (replayLifecycleWindow.Active?.Kind != ReplayLifecycleKind.LoadSlot
                        || replayLifecycleLoad == null || replayLifecycleWindow.Failure.Length != 0)
                        throw new InvalidOperationException("Unexpected native data-load callback during lifecycle replay.");
                    replayLifecycleLoad.ObserveDataLoaded();
                    VerifyLifecycleModdedSlot(replayLifecycleWindow.Active!);
                    loadCompletedBoundary = DescribeLoadBoundary();
                    return;
                }
                catch (Exception exception) { FailReplayLifecycle(exception.Message); }
            }
            if (activeLifecycle != null)
            {
                try
                {
                    if (activeLifecycle.Kind != ReplayLifecycleKind.LoadSlot
                        || lifecycleLoad == null || sourceRecordingRootError.Length != 0)
                        throw new InvalidOperationException("Unexpected native data-load callback during lifecycle recording.");
                    lifecycleLoad.ObserveDataLoaded();
                    VerifyLifecycleModdedSlot(activeLifecycle);
                    loadCompletedBoundary = DescribeLoadBoundary();
                    return;
                }
                catch (Exception exception) { FailLifecycleOperation(activeLifecycle.Sequence, exception.Message); }
            }
            loadCompletedBoundary = DescribeLoadBoundary();
            InvalidateSourceRecordingRootForLoad();
            pendingBaseline = true;
            ResetBaselineCandidate();
        }

        private void OnNewGame()
        {
            if (replayLifecycleWindow != null)
                FailReplayLifecycle("New game interrupted lifecycle replay.");
            loadRequestedBoundary = DescribeLoadBoundary();
            loadCompletedBoundary = string.Empty;
            firstAnchorBoundary = string.Empty;
            InvalidateSourceRecordingRootForLoad();
            currentSaveSlot = GameManager.instance == null
                ? 0
                : GameManager.instance.profileID;
            pendingBaseline = true;
            ResetBaselineCandidate();
            Suspend("new-game-requested");
        }

        private void InvalidateSourceRecordingRootForLoad()
        {
            var hadVerifiedColdRoot = coldRecordingRootVerified;
            coldRecordingRootVerified = false;
            // ClockStartup's root is process-scoped and one-shot. A save load
            // cannot reuse that acknowledgement as proof of a new origin.
            // Initial loads before the first root request remain supported.
            if (sourceRecordingRootError.Length != 0
                || (!hadVerifiedColdRoot && (sourceRecordingRoot == null
                    || (!sourceRecordingRoot.PreparationStarted && !sourceRecordingRootReady))))
                return;

            sourceRecordingRootReady = false;
            ReleaseSourcePreparationInput();
            sourceRecordingRootError =
                "Loading a save after recording origin preparation invalidates this recording session. "
                + "Restart through Studio to prepare a new origin; existing saved recordings are retained.";
            logError(sourceRecordingRootError);
        }

        private void ReleaseSourcePreparationInput()
        {
            sourcePreparationElapsed?.Stop();
            sourcePreparationElapsed = null;
            try { sourceRecordingRoot?.Dispose(); }
            catch (Exception exception)
            {
                sourceRecordingRootError = exception.Message;
                logError("Recording preparation cleanup failed: " + exception.Message);
            }
        }

        internal void ReanchorColdBaseline(BaselineBundle saved, SnapshotCaptureResult capture, int rootFrame)
        {
            // Hash equality at an earlier idle frame does not establish the
            // replay timeline. Discard only this session's warmup journal,
            // retaining its files, after the persisted boundary is proven.
            if (saved?.RecordingOrigin == null || !capture.Success
                || !string.Equals(saved.BaselineSemanticSha256, capture.Sha256, StringComparison.Ordinal)
                || saved.RecordingOrigin.CompareBoundary(checked(Time.frameCount - rootFrame),
                    Time.timeAsDouble, Time.fixedTimeAsDouble) != RecordingOriginAlignment.Matched)
                throw new InvalidOperationException("Cold journal requires the exact persisted origin and semantic hash.");
            if (sourceRecordingRoot != null || playbackCaptureActive)
                throw new InvalidOperationException("Cannot replace an active source/replay journal.");
            EstablishBaseline(capture, saved);
            if (!IsAvailable || !ReferenceEquals(baselineBundleCapture?.Bundle, saved))
                throw new InvalidOperationException("Cold journal origin could not be established.");
            coldRecordingRootVerified = true;
        }

        private void EstablishBaseline(SnapshotCaptureResult capture, BaselineBundle? replayBaseline = null)
        {
            coldRecordingRootVerified = false;
            if (capture == null
                || !capture.Success
                || capture.Snapshot == null
                || capture.CanonicalBytes == null
                || string.IsNullOrEmpty(capture.Sha256))
            {
                ResetBaselineCandidate();
                return;
            }

            Flush();
            var manager = GameManager.instance;
            if (manager != null
                && manager.profileID > 0
                && manager.profileID <= 4)
            {
                currentSaveSlot = manager.profileID;
            }

            if (currentSaveSlot <= 0 || currentSaveSlot > 4)
            {
                nextBaselineRetryRealtime = Time.realtimeSinceStartup + 1f;
                ResetBaselineCandidate();
                logWarning(
                    "T09 baseline capture is waiting for a persistent save slot.");
                return;
            }

            sceneTracker.ResetForRecording();
            capture = SnapshotCaptureResult.Succeeded(
                new TickStamp(
                    capture.Stamp.InputTick,
                    capture.Stamp.VisualTick,
                    capture.Stamp.FixedTick,
                    sceneTracker.CurrentEpoch,
                    TickPhase.LateUpdateEnd),
                capture.Snapshot,
                capture.CanonicalBytes,
                capture.Sha256!);

            baselineSequence++;
            var baselineId = replayBaseline?.BaselineId ?? (currentSaveSlot > 0
                ? "slot-"
                  + currentSaveSlot.ToString(CultureInfo.InvariantCulture)
                  + "-semantic-"
                  + baselineSequence.ToString("D4", CultureInfo.InvariantCulture)
                : "runtime-semantic-"
                  + baselineSequence.ToString("D4", CultureInfo.InvariantCulture));
            var bundleCapture = replayBaseline != null
                ? BaselineCaptureResult.Succeeded(replayBaseline, BaselineBundleCodec.Serialize(replayBaseline))
                : baselineProvider.CaptureCurrentBaseline(
                baselineId,
                currentSaveSlot,
                capture,
                DateTimeOffset.UtcNow);
            if (!bundleCapture.Success)
            {
                nextBaselineRetryRealtime = Time.realtimeSinceStartup + 1f;
                ResetBaselineCandidate();
                logWarning(
                    "T09 raw baseline bundle unavailable: "
                    + bundleCapture.Error);
                return;
            }

            if (sourceRecordingRoot != null)
            {
                var rootFrame = sourceRecordingRoot.RecordingBoundaryFrame;
                if (!sourceRecordingRootReady || !rootFrame.HasValue)
                    throw new InvalidOperationException("Verified recording boundary observation is missing.");
                var origin = new RecordingOrigin(
                    HollowKnightTAS.Core.Ipc.StartupProfileContract.ProfileId,
                    sourceRecordingRoot.RootBoundarySeconds,
                    checked(Time.frameCount - rootFrame.Value),
                    Time.timeAsDouble,
                    Time.fixedTimeAsDouble);
                var stampedBundle = bundleCapture.Bundle!.WithRecordingOrigin(origin);
                bundleCapture = BaselineCaptureResult.Succeeded(
                    stampedBundle, BaselineBundleCodec.Serialize(stampedBundle));
            }

            ReleaseSourcePreparationInput();
            if (sourceRecordingRootError.Length != 0) return;
            currentDirectory = Path.Combine(
                journalRoot,
                "baseline-"
                + baselineSequence.ToString("D4", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Path.Combine(currentDirectory, "segments"));
            journal = new ReplayJournal(baselineId, capture.Sha256!);
            baselineSemanticCapture = capture;
            baselineBundleCapture = bundleCapture;
            persistedSegments.Clear();
            commands.Clear();
            nextSegmentSequence = 0;
            previousSegmentSha256 =
                ReplayJournalSegment.GenesisPreviousSha256;
            lastCommittedStamp = null;
            recorder.Reset();
            suspended = false;
            playbackCaptureActive = false;
            playbackCaptureError = string.Empty;
            lastPlaybackRawInputTick = null;
            pendingBaseline = false;
            DisableBaselineCaptureBoundary();
            ResetBaselineCandidate();
            WriteBaseline(capture);
            WriteIndex();
            WriteEvent("baseline-established", capture.Sha256!);
            BaselineEstablished?.Invoke(baselineId, capture.Sha256!);
            logDebug(
                "T06 shadow journal baseline established id="
                + baselineId
                + " hash="
                + capture.Sha256
                + " anchor="
                + (GetSupportedBaselineAnchorKind() ?? "unknown"));
        }

        private void OnInputManagerUpdated(ulong inputTick, float deltaTime)
        {
            currentInputTick = inputTick;
            visualTick = Time.frameCount;
            fixedTick = ReadFixedTick();
            if (pendingBaseline)
            {
                UpdateBaselineCaptureBoundaryRegistration();
            }

            var current = journal;
            if (disposed
                || activeLifecycle != null || replayLifecycleWindow != null
                || pendingBaseline
                || current == null
                || current.HasGap)
            {
                return;
            }

            if (playbackCaptureActive)
            {
                return;
            }

            try
            {
                if (!IsSupportedGameplay())
                {
                    Suspend("unsupported-gameplay-lifecycle");
                    return;
                }

                // Once a recording root exists, record native input frames
                // during hitstop and its ramps too. Canonical time scale is
                // a baseline-anchor requirement, not a running-movie filter.

                if (suspended)
                {
                    current.Suspend();
                    suspended = false;
                    WriteEvent("journal-resumed", inputTick.ToString(CultureInfo.InvariantCulture));
                }

                var actions = InputHandler.Instance.inputActions;
                var sample = recorder.Capture(actions, inputTick);
                var committedStamp = new TickStamp(
                    inputTick,
                    visualTick,
                    fixedTick,
                    sceneTracker.CurrentEpoch,
                    TickPhase.InControlCommitted);
                var append = current.Append(sample, committedStamp);
                if (!append.Success)
                {
                    WriteEvent("journal-gap", append.Error);
                    logWarning("T06 shadow journal gap: " + append.Error);
                    return;
                }

                lastCommittedStamp = committedStamp;
                MovieTickCommitted?.Invoke(
                    append.MovieTick,
                    sample,
                    committedStamp);

                if (current.RetainedCount >= SegmentRecordCount)
                {
                    Flush();
                }
            }
            catch (Exception exception)
            {
                current.MarkPersistenceFailure(exception.Message);
                WriteEvent(
                    "journal-fault",
                    exception.GetType().Name + ": " + exception.Message);
                logError("T06 shadow journal failed: " + exception);
            }
        }

        private void UpdateBaselineCaptureBoundaryRegistration()
        {
            if (disposed || !pendingBaseline)
            {
                DisableBaselineCaptureBoundary();
                return;
            }

            if (!IsSupportedBaselineAnchor())
            {
                if (sourceRecordingRoot?.PreparationStarted == true && !sourceRecordingRootReady
                    && sourceRecordingRootError.Length == 0)
                {
                    sourceRecordingRootError = "The baseline anchor was left during recording origin preparation.";
                    ReleaseSourcePreparationInput();
                    logError(sourceRecordingRootError);
                }
                ResetBaselineCandidate();
                DisableBaselineCaptureBoundary();
                return;
            }

            if (baselineCaptureBoundaryRegistered)
            {
                return;
            }

            Application.onBeforeRender += OnBeforeRender;
            baselineCaptureBoundaryRegistered = true;
            lastBaselineCaptureVisualFrame = -1;
        }

        private void DisableBaselineCaptureBoundary()
        {
            if (!baselineCaptureBoundaryRegistered)
            {
                return;
            }

            Application.onBeforeRender -= OnBeforeRender;
            baselineCaptureBoundaryRegistered = false;
            lastBaselineCaptureVisualFrame = -1;
        }

        private void OnBeforeRender()
        {
            if (disposed || !pendingBaseline)
            {
                DisableBaselineCaptureBoundary();
                return;
            }

            var frame = Time.frameCount;
            if (frame == lastBaselineCaptureVisualFrame)
            {
                return;
            }

            lastBaselineCaptureVisualFrame = frame;
            visualTick = frame;
            fixedTick = ReadFixedTick();
            if (!IsSupportedBaselineAnchor())
            {
                ResetBaselineCandidate();
                DisableBaselineCaptureBoundary();
                return;
            }

            try { TryEstablishBaselineAtCompletedFrameBoundary(); }
            catch (Exception exception)
            {
                sourceRecordingRootError = "Recording origin capture failed: " + exception.Message;
                ReleaseSourcePreparationInput();
                DisableBaselineCaptureBoundary();
                logError(sourceRecordingRootError);
            }
        }

        private static long ReadFixedTick()
        {
            var step = Time.fixedDeltaTime;
            if (step <= 0f)
            {
                return 0;
            }

            return checked(
                (long)Math.Round(
                    (double)Time.fixedTime / step,
                    MidpointRounding.AwayFromZero));
        }

        private void Suspend(string reason)
        {
            if (journal == null || suspended)
            {
                return;
            }

            suspended = true;
            journal.Suspend();
            WriteEvent("journal-suspended", reason);
        }

        private static bool IsSupportedGameplay()
        {
            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            var input = InputHandler.Instance;
            return manager != null
                   && manager.gameState == GameState.PLAYING
                   && hero != null
                   && hero.gameObject.activeInHierarchy
                   && input != null
                   && input.inputActions != null;
        }

        private static bool IsSupportedBaselineAnchor()
        {
            return GetSupportedBaselineAnchorKind() != null;
        }

        private static string? GetSupportedBaselineAnchorKind()
        {
            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            var body = hero == null
                ? null
                : hero.GetComponent<Rigidbody2D>();
            if (!IsSupportedGameplay()
                || manager == null
                || manager.IsInSceneTransition
                || hero == null
                || body == null
                || body.velocity.x != 0f
                || body.velocity.y != 0f
                || !ReplayDeterministicTimingLease
                    .MovieTicksMayAdvance())
            {
                return null;
            }

            // A raw desktop slot reloads directly into the vanilla seated
            // bench state. It is stable and semantically reproducible even
            // though HeroController intentionally does not accept gameplay
            // input until the bench FSM performs the stand-up transition.
            if (PlayerData.instance?.atBench == true
                && !hero.acceptingInput)
            {
                return "vanilla-seated-bench";
            }

            // A raw Hollow Knight save can also reload at a controllable
            // neutral grounded anchor. A briefly controllable airborne state
            // persists during load, so hash stability alone is not enough.
            if (hero.acceptingInput
                && hero.hero_state == ActorStates.idle
                && hero.cState.onGround
                && !hero.cState.jumping
                && !hero.cState.falling
                && !hero.cState.dashing
                && !hero.cState.attacking
                && !hero.cState.wallSliding)
            {
                return "controllable-neutral";
            }

            return null;
        }

        private void ResetBaselineCandidate()
        {
            baselineSemanticStableFrames = 0;
            baselineCandidateSha256 = null;
            baselineCandidateCapture = null;
        }

        private void PersistSlice(JournalSlice slice)
        {
            if (currentDirectory == null || slice.Records.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder(slice.Records.Count * 256);
            foreach (var record in slice.Records)
            {
                builder.Append('{');
                AppendNumber(builder, "movieTick", record.MovieTick);
                AppendNumber(
                    builder,
                    "inputTick",
                    unchecked((long)record.Stamp.InputTick));
                AppendNumber(builder, "visualTick", record.Stamp.VisualTick);
                AppendNumber(builder, "fixedTick", record.Stamp.FixedTick);
                AppendNumber(builder, "sceneEpoch", record.Stamp.SceneEpoch);
                AppendString(builder, "phase", record.Stamp.Phase.ToString());
                AppendString(builder, "held", record.Sample.Held.ToString());
                AppendString(builder, "pressed", record.Sample.Pressed.ToString());
                AppendString(builder, "released", record.Sample.Released.ToString());
                AppendNumber(builder, "axisX", record.Sample.AxisX);
                AppendNumber(builder, "axisY", record.Sample.AxisY);
                builder.Append("}\n");
            }

            var name = slice.FirstMovieTick.ToString(
                           "D12",
                           CultureInfo.InvariantCulture)
                       + "-"
                       + slice.LastMovieTick.ToString(
                           "D12",
                           CultureInfo.InvariantCulture)
                       + ".jsonl";
            WriteAtomic(
                Path.Combine(currentDirectory, "segments", name),
                new UTF8Encoding(false).GetBytes(builder.ToString()));

            var segment = new ReplayJournalSegment(
                ReplayJournalSegment.CurrentSchemaVersion,
                nextSegmentSequence,
                previousSegmentSha256,
                slice.Records);
            var canonicalBytes = ReplayJournalSegmentCodec.Serialize(segment);
            var sha256 = Sha256Utility.ComputeHex(canonicalBytes);
            // Keep the on-disk staging path below the legacy Windows MAX_PATH
            // limit used by the target Mono runtime. The authenticated tick
            // range and content hash already live in the segment bytes/index.
            var canonicalName =
                nextSegmentSequence.ToString(
                    "D8",
                    CultureInfo.InvariantCulture)
                + ".hktjs";
            var canonicalPath = Path.Combine(
                currentDirectory,
                "segments",
                canonicalName);
            WriteAtomic(canonicalPath, canonicalBytes);
            persistedSegments.Add(
                new PersistedSegment(
                    nextSegmentSequence,
                    slice.FirstMovieTick,
                    slice.LastMovieTick,
                    sha256,
                    canonicalPath));
            nextSegmentSequence++;
            previousSegmentSha256 = sha256;
        }

        private static string DescribeLoadBoundary()
        {
            var hero = HeroController.SilentInstance;
            var body = hero == null ? null : hero.GetComponent<Rigidbody2D>();
            return string.Format(CultureInfo.InvariantCulture,
                "frame={0}; game={1:R}; fixed={2:R}; inputTick={3}; scene={4}; heroY={5}; bodyY={6}",
                Time.frameCount, Time.timeAsDouble, Time.fixedTimeAsDouble, InputManager.CurrentTick,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                hero == null ? "absent" : hero.transform.position.y.ToString("R", CultureInfo.InvariantCulture),
                body == null ? "absent" : body.position.y.ToString("R", CultureInfo.InvariantCulture));
        }

        private void WriteBaseline(SnapshotCaptureResult capture)
        {
            if (currentDirectory == null || journal == null)
            {
                return;
            }

            WriteAtomic(
                Path.Combine(currentDirectory, "baseline.snapshot"),
                capture.CanonicalBytes!);
            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "baselineId", journal.BaselineId);
            AppendString(builder, "baselineSha256", journal.BaselineSha256);
            AppendString(builder, "manifestSha256", manifestSha256);
            AppendNumber(builder, "saveSlot", currentSaveSlot);
            AppendNumber(
                builder,
                "inputTick",
                unchecked((long)capture.Stamp.InputTick));
            AppendNumber(builder, "visualTick", capture.Stamp.VisualTick);
            AppendNumber(builder, "fixedTick", capture.Stamp.FixedTick);
            AppendNumber(builder, "sceneEpoch", capture.Stamp.SceneEpoch);
            AppendString(builder, "phase", capture.Stamp.Phase.ToString());
            // Bounded diagnostics only, not restore inputs or semantic hashes.
            AppendString(builder, "loadRequestedBoundary", loadRequestedBoundary);
            AppendString(builder, "loadCompletedBoundary", loadCompletedBoundary);
            AppendString(builder, "firstAnchorBoundary", firstAnchorBoundary);
            AppendString(builder, "recordingOriginBoundary", DescribeLoadBoundary());
            builder.Append('}');
            WriteAtomic(
                Path.Combine(currentDirectory, "baseline.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
        }

        private void WriteIndex()
        {
            if (currentDirectory == null || journal == null)
            {
                return;
            }

            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "baselineId", journal.BaselineId);
            AppendString(builder, "baselineSha256", journal.BaselineSha256);
            AppendNumber(builder, "firstMovieTick", journal.FirstMovieTick);
            AppendNumber(
                builder,
                "lastCommittedMovieTick",
                journal.LastCommittedMovieTick);
            AppendNumber(
                builder,
                "lastPersistedMovieTick",
                journal.LastPersistedMovieTick);
            AppendBoolean(builder, "hasGap", journal.HasGap);
            AppendNumber(
                builder,
                "segmentCount",
                persistedSegments.Count);
            AppendString(
                builder,
                "journalHeadSha256",
                previousSegmentSha256);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(currentDirectory, "index.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
        }

        private void WriteEvent(string eventType, string detail)
        {
            if (currentDirectory == null)
            {
                return;
            }

            var builder = new StringBuilder(256);
            builder.Append('{');
            AppendString(builder, "event", eventType);
            AppendString(builder, "detail", detail ?? string.Empty);
            AppendNumber(builder, "visualTick", visualTick);
            AppendNumber(builder, "fixedTick", fixedTick);
            builder.Append("}\n");
            File.AppendAllText(
                Path.Combine(currentDirectory, "events.jsonl"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value ?? string.Empty);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendPropertyPrefix(StringBuilder builder, string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }

        private static void WriteAtomic(string destinationPath, byte[] bytes)
        {
            // Keep the temporary file beside its destination for atomic rename,
            // without appending a GUID to an already long content-hash filename.
            // Unity's Windows Mono cannot open that expanded (>260 char) path.
            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(destinationPath)!, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, null);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private sealed class PersistedSegment
        {
            public PersistedSegment(
                int sequence,
                long firstMovieTick,
                long lastMovieTick,
                string sha256,
                string path)
            {
                Sequence = sequence;
                FirstMovieTick = firstMovieTick;
                LastMovieTick = lastMovieTick;
                Sha256 = sha256;
                Path = path;
            }

            public int Sequence { get; }
            public long FirstMovieTick { get; }
            public long LastMovieTick { get; }
            public string Sha256 { get; }
            public string Path { get; }
        }
    }

}
