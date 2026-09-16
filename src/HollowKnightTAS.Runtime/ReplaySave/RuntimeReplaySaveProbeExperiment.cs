using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Control;
using HollowKnightTAS.Runtime.Playback;
using InControl;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class RuntimeReplaySaveProbeExperiment : IDisposable
    {
        private const long CaptureMovieTickTarget = 1000;
        private const float PauseWallSeconds = 3f;

        private readonly RuntimeReplaySaveManager manager;
        private readonly RuntimeReplayJournal journal;
        private readonly ReplaySaveProbeOptions options;
        private readonly string manifestSha256;
        private readonly string outputDirectory;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private RuntimeReplaySaveProbeRunner? runner;
        private RuntimePauseController? pauseController;
        private RuntimePlaybackController? playbackController;
        private ReplayRestoreHandle? restoreHandle;
        private readonly List<ReplayRestoreProgress> restoreAttempts =
            new List<ReplayRestoreProgress>();
        private bool loadRequested;
        private bool attached;
        private bool playbackStarted;
        private bool playbackStopped;
        private bool pauseRequested;
        private bool pauseResumed;
        private bool transitionRequested;
        private bool transitionSaveRequested;
        private bool returnTransitionRequested;
        private bool combatRequested;
        private bool slotSafetyCompleted;
        private bool slotSafetyPass;
        private bool stationarySemanticPass;
        private bool jumpSemanticPass;
        private bool dashSemanticPass;
        private bool combatSemanticPass;
        private bool? heroControlRestoreEquivalent;
        private string stationarySemanticDetail = string.Empty;
        private string jumpSemanticDetail = string.Empty;
        private string dashSemanticDetail = string.Empty;
        private string combatSemanticDetail = string.Empty;
        private bool stopped;
        private bool exitRequested;
        private bool exitIssued;
        private bool restoreStarted;
        private bool restoreAttemptResumed;
        private bool overwriteApprovalObserved;
        private int manualRequestCount;
        private int restoreAttempt;
        private long observationCount;
        private long pauseJournalTickBefore;
        private long pauseJournalTickAfter;
        private int pauseAutoCountBefore;
        private int pauseAutoCountAfter;
        private float startedAtRealtime;
        private float pauseStartedAtRealtime;
        private float pauseDuration;
        private string slotSafetyDetail = string.Empty;
        private string error = string.Empty;

        private RuntimeReplaySaveProbeExperiment(
            string sessionDirectory,
            string manifestSha256,
            RuntimeReplaySaveManager manager,
            RuntimeReplayJournal journal,
            ReplaySaveProbeOptions options,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError)
        {
            this.manifestSha256 = manifestSha256;
            this.manager = manager;
            this.journal = journal;
            this.options = options;
            this.logInfo = logInfo;
            this.logWarning = logWarning;
            this.logError = logError;
            outputDirectory = Path.Combine(
                sessionDirectory,
                "replay-save",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
        }

        public static RuntimeReplaySaveProbeExperiment? TryStart(
            string sessionDirectory,
            string manifestSha256,
            RuntimeReplaySaveManager manager,
            RuntimeReplayJournal journal,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError)
        {
            var parse = ReplaySaveProbeOptions.Parse(
                Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T09 replay-save probe arguments: "
                    + parse.Error);
            }

            var probe = new RuntimeReplaySaveProbeExperiment(
                sessionDirectory,
                manifestSha256,
                manager,
                journal,
                parse.Options,
                logInfo,
                logWarning,
                logError);
            probe.Start();
            return probe;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                Finish(false, "Application quit before probe completion.");
            }

            playbackController?.Dispose();
            playbackController = null;
            pauseController?.Dispose();
            pauseController = null;
            if (runner != null && !exitIssued)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        internal void OnUpdate()
        {
            try
            {
                if (exitRequested && !exitIssued)
                {
                    exitIssued = true;
                    Application.Quit();
                    return;
                }

                if (stopped)
                {
                    return;
                }

                var elapsed = Time.realtimeSinceStartup - startedAtRealtime;
                var maximumRuntimeSeconds =
                    options.Profile == ReplaySaveProbeProfile.Capture
                        ? 90f
                        : Math.Max(
                            300f,
                            120f + options.RestoreAttempts * 120f);
                if (elapsed > maximumRuntimeSeconds)
                {
                    Finish(
                        false,
                        "Replay-save probe exceeded "
                        + maximumRuntimeSeconds.ToString(
                            "R",
                            CultureInfo.InvariantCulture)
                        + " seconds.");
                    return;
                }

                if (options.Profile == ReplaySaveProbeProfile.Capture)
                {
                    AdvanceCaptureUpdate(elapsed);
                }
                else
                {
                    AdvanceRestoreUpdate(elapsed);
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        internal void OnLateUpdate()
        {
            if (stopped)
            {
                return;
            }

            try
            {
                if (options.Profile == ReplaySaveProbeProfile.Capture)
                {
                    AdvanceCaptureLateUpdate();
                }
                else
                {
                    AdvanceRestoreLateUpdate();
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private void Start()
        {
            startedAtRealtime = Time.realtimeSinceStartup;
            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeReplaySaveProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimeReplaySaveProbeRunner>();
            runner.Initialize(this);
            WriteAtomic(
                Path.Combine(outputDirectory, "probe.ready"),
                Utf8(options.ProfileId + " " + options.RunId + "\n"));
            logInfo(
                "T09 replay-save probe created profile="
                + options.ProfileId
                + " runId="
                + options.RunId);
        }

        private void AdvanceCaptureUpdate(float elapsed)
        {
            if (!loadRequested
                && elapsed >= 2f
                && options.AutoLoadSlot > 0
                && string.Equals(
                    UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene()
                        .name,
                    "Menu_Title",
                    StringComparison.Ordinal)
                && GameManager.instance != null)
            {
                loadRequested = true;
                GameManager.instance.LoadGameFromUI(options.AutoLoadSlot);
                return;
            }

            if (transitionRequested
                && !transitionSaveRequested
                && GameManager.instance?.IsInSceneTransition == true)
            {
                RequestManual("manual-scene-transition");
                transitionSaveRequested = true;
            }

            if (pauseRequested
                && !pauseResumed
                && Time.realtimeSinceStartup - pauseStartedAtRealtime
                   >= PauseWallSeconds)
            {
                pauseJournalTickAfter =
                    journal.LastCommittedMovieTick;
                pauseAutoCountAfter = AutoOperationCount();
                pauseDuration =
                    Time.realtimeSinceStartup - pauseStartedAtRealtime;
                var resume = pauseController!.Resume();
                if (!resume.Success)
                {
                    throw new InvalidOperationException(
                        "Capture pause could not resume: " + resume.Error);
                }

                pauseResumed = true;
            }

        }

        private void AdvanceCaptureLateUpdate()
        {
            if (!attached)
            {
                var gameManager = GameManager.instance;
                var hero = HeroController.SilentInstance;
                if (gameManager == null
                    || gameManager.gameState != GameState.PLAYING
                    || gameManager.IsInSceneTransition
                    || hero == null
                    || !hero.gameObject.activeInHierarchy
                    || InputHandler.Instance?.inputActions == null
                    || !journal.IsAvailable
                    || journal.BaselineSemanticCapture?.Success != true
                    || journal.BaselineBundleCapture?.Success != true)
                {
                    return;
                }

                RunSyntheticSlotSafetyProbe();
                manager.SetAutoSavePolicy(
                    new AutoSavePolicy(true, 120, 3));
                attached = true;
                StartCapturePlayback();
                return;
            }

            if (!playbackStarted)
            {
                StartCapturePlayback();
                return;
            }

            if (!playbackStopped
                || journal.LastCommittedMovieTick
                   < CaptureMovieTickTarget
                || manager.PendingCount != 0)
            {
                return;
            }

            var entries = manager.Inspect();
            var manual = entries
                .Where(
                    value => value.Descriptor.Reason
                             != ReplaySaveReason.AutomaticInterval)
                .ToArray();
            var automatic = entries
                .Where(
                    value => value.Descriptor.Reason
                             == ReplaySaveReason.AutomaticInterval)
                .ToArray();
            var transition = manual.SingleOrDefault(
                value => string.Equals(
                    value.Descriptor.Label,
                    "manual-scene-transition",
                    StringComparison.Ordinal));
            stationarySemanticPass = CheckSemantic(
                manual,
                "manual-stationary",
                snapshot =>
                    IsTrue(snapshot, "hero.cState.onGround")
                    && Math.Abs(
                        ReadFloat(snapshot, "hero.velocity.x")) <= 0.01f
                    && Math.Abs(
                        ReadFloat(snapshot, "hero.velocity.y")) <= 0.01f,
                out stationarySemanticDetail);
            jumpSemanticPass = CheckSemantic(
                manual,
                "manual-jump-rising",
                snapshot =>
                    IsTrue(snapshot, "hero.cState.jumping")
                    && ReadFloat(snapshot, "hero.velocity.y") > 0f,
                out jumpSemanticDetail);
            dashSemanticPass = CheckSemantic(
                manual,
                "manual-dash",
                snapshot => IsTrue(snapshot, "hero.cState.dashing"),
                out dashSemanticDetail);
            combatSemanticPass = CheckSemantic(
                manual,
                "manual-combat-attack",
                snapshot =>
                    string.Equals(
                        snapshot.Values["scene.name"].DisplayValue,
                        "GG_Vengefly",
                        StringComparison.Ordinal)
                    && IsTrue(snapshot, "hero.cState.attacking"),
                out combatSemanticDetail);
            var pass =
                manualRequestCount == 5
                && manual.Length == 5
                && automatic.Length == 3
                && entries.All(
                    value => value.Status == ReplaySaveStatus.Ready)
                && pauseResumed
                && pauseDuration >= PauseWallSeconds
                && pauseJournalTickBefore == pauseJournalTickAfter
                && pauseAutoCountBefore == pauseAutoCountAfter
                && transition != null
                && transition.Descriptor.EffectiveMovieTick
                   > transition.Descriptor.RequestedAtMovieTick
                && slotSafetyCompleted
                && slotSafetyPass
                && stationarySemanticPass
                && jumpSemanticPass
                && dashSemanticPass
                && combatSemanticPass
                && heroControlRestoreEquivalent == true
                && string.IsNullOrEmpty(error);
            if (!pass && string.IsNullOrEmpty(error))
            {
                error =
                    "Capture matrix acceptance conditions were not met.";
            }

            WriteCaptureResult(pass, entries);
            Finish(
                pass,
                pass
                    ? string.Empty
                    : "Capture matrix acceptance conditions were not met.",
                writeResult: false);
        }

        private void StartCapturePlayback()
        {
            pauseController = new RuntimePauseController(
                new DirectoryInfo(
                    Directory.GetParent(outputDirectory)!.Parent!.FullName)
                    .Name,
                manifestSha256,
                options.RunId,
                "T09_CAPTURE");
            var gate = new GameplayPauseGate(pauseController);
            playbackController = new RuntimePlaybackController(
                OnCaptureObservation,
                _ => { },
                (reason, report) =>
                {
                    playbackStopped = true;
                    heroControlRestoreEquivalent =
                        playbackController?.HeroControlRestoreEquivalent;
                    if (reason != PlaybackStopReason.Completed
                        || !report.Equivalent
                        || heroControlRestoreEquivalent != true)
                    {
                        error =
                            "Capture playback stopped with "
                            + reason
                            + ", bindingEquivalent="
                            + report.Equivalent
                            + ", heroControlEquivalent="
                            + heroControlRestoreEquivalent
                            + ", fault="
                            + (playbackController?.FaultMessage
                               ?? string.Empty)
                            + ".";
                    }
                },
                gate);
            var movie = CreateCaptureMovie();
            var start = playbackController.StartReplay(
                movie,
                new PlaybackContext(
                    manifestSha256,
                    journal.BaselineId!,
                    journal.BaselineSha256!,
                    playbackController.SceneEpoch,
                    allowSceneTransitions: true));
            if (!start.Success)
            {
                throw new InvalidOperationException(
                    "Capture playback could not start: " + start.Error);
            }

            playbackStarted = true;
        }

        private MovieDocument CreateCaptureMovie()
        {
            var span = new MovieSourceSpan(
                "generated-replay-save-capture.hktas",
                1,
                1,
                1);
            return new MovieDocument(
                "generated-replay-save-capture.hktas",
                new MovieHeader(
                    1,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    manifestSha256,
                    journal.BaselineId!,
                    journal.BaselineSha256!,
                    "input"),
                new MovieCommand[]
                {
                    new FrameRunCommand(
                        60,
                        TasAction.None,
                        0,
                        0,
                        false,
                        span),
                    new FrameRunCommand(
                        10,
                        TasAction.Dash,
                        0,
                        0,
                        false,
                        span),
                    new FrameRunCommand(
                        30,
                        TasAction.None,
                        0,
                        0,
                        false,
                        span),
                    new FrameRunCommand(
                        20,
                        TasAction.Attack,
                        0,
                        0,
                        false,
                        span),
                    new FrameRunCommand(
                        80,
                        TasAction.None,
                        0,
                        0,
                        false,
                        span),
                    new FrameRunCommand(
                        30,
                        TasAction.Jump,
                        0,
                        0,
                        false,
                        span),
                    new FrameRunCommand(
                        1070,
                        TasAction.None,
                        0,
                        0,
                        false,
                        span)
                });
        }

        private void OnCaptureObservation(ReplayInputObservation observation)
        {
            observationCount = observation.MovieTick + 1;
            if (!pauseRequested
                && observation.MovieTick >= 400
                && manager.PendingCount == 0)
            {
                var pause = pauseController!.Pause();
                if (!pause.Success)
                {
                    throw new InvalidOperationException(
                        "Capture pause failed: " + pause.Error);
                }

                pauseRequested = true;
                pauseStartedAtRealtime = Time.realtimeSinceStartup;
                pauseJournalTickBefore = journal.LastCommittedMovieTick;
                pauseAutoCountBefore = AutoOperationCount();
            }

            switch (observation.MovieTick)
            {
                case 62:
                    RequestManual("manual-dash");
                    break;
                case 50:
                    if (!transitionRequested)
                    {
                        var spec = new ForcedSceneTransitionSpec(
                            "GG_Vengefly",
                            "door_dreamEnter",
                            0f,
                            true,
                            true,
                            false,
                            GameManager.SceneLoadVisualizations
                                .GodsAndGlory);
                        if (!journal.RecordForcedSceneTransition(
                                spec,
                                out var commandError))
                        {
                            throw new InvalidOperationException(
                                "Forced transition could not be journaled: "
                                + commandError);
                        }

                        transitionRequested = true;
                        GameManager.instance!.BeginSceneTransition(
                            spec.ToSceneLoadInfo());
                    }

                    break;
                case 150:
                    RequestManual("manual-stationary");
                    break;
                case 101:
                    if (!combatRequested)
                    {
                        RequestManual("manual-combat-attack");
                        combatRequested = true;
                    }

                    break;
                case 201:
                    RequestManual("manual-jump-rising");
                    break;
                case 300:
                    if (!returnTransitionRequested)
                    {
                        var spec = new ForcedSceneTransitionSpec(
                            "GG_Workshop",
                            "door_dreamReturn",
                            0f,
                            true,
                            true,
                            false,
                            GameManager.SceneLoadVisualizations
                                .GodsAndGlory);
                        if (!journal.RecordForcedSceneTransition(
                                spec,
                                out var commandError))
                        {
                            throw new InvalidOperationException(
                                "Return transition could not be journaled: "
                                + commandError);
                        }

                        returnTransitionRequested = true;
                        GameManager.instance!.BeginSceneTransition(
                            spec.ToSceneLoadInfo());
                    }

                    break;
            }
        }

        private void RequestManual(string label)
        {
            var result = manager.RequestManualSave(label);
            if (!result.Accepted)
            {
                throw new InvalidOperationException(
                    "Manual save request was rejected: " + result.Error);
            }

            manualRequestCount++;
        }

        private bool CheckSemantic(
            IEnumerable<ReplaySaveCatalogEntry> entries,
            string label,
            Func<SemanticSnapshot, bool> predicate,
            out string detail)
        {
            var entry = entries.SingleOrDefault(
                value => string.Equals(
                    value.Descriptor.Label,
                    label,
                    StringComparison.Ordinal));
            if (entry == null)
            {
                detail = "entry-missing";
                return false;
            }

            var inspection = manager.InspectSemanticState(
                entry.Descriptor.ReplaySaveId);
            if (!inspection.Success || inspection.Snapshot == null)
            {
                detail =
                    inspection.Status + ": " + inspection.Detail;
                return false;
            }

            detail = DescribeSemantic(inspection.Snapshot);
            return predicate(inspection.Snapshot);
        }

        private static string DescribeSemantic(SemanticSnapshot snapshot)
        {
            return "scene="
                   + ReadDisplay(snapshot, "scene.name")
                   + ";actor="
                   + ReadDisplay(snapshot, "hero.actorState")
                   + ";onGround="
                   + ReadDisplay(snapshot, "hero.cState.onGround")
                   + ";jumping="
                   + ReadDisplay(snapshot, "hero.cState.jumping")
                   + ";dashing="
                   + ReadDisplay(snapshot, "hero.cState.dashing")
                   + ";attacking="
                   + ReadDisplay(snapshot, "hero.cState.attacking")
                   + ";position=("
                   + ReadDisplay(snapshot, "hero.position.x")
                   + ","
                   + ReadDisplay(snapshot, "hero.position.y")
                   + ");velocity=("
                   + ReadDisplay(snapshot, "hero.velocity.x")
                   + ","
                   + ReadDisplay(snapshot, "hero.velocity.y")
                   + ")";
        }

        private static string ReadDisplay(
            SemanticSnapshot snapshot,
            string key)
        {
            return snapshot.Values.TryGetValue(key, out var value)
                ? value.DisplayValue
                : "<missing>";
        }

        private static bool IsTrue(
            SemanticSnapshot snapshot,
            string key)
        {
            return snapshot.Values.TryGetValue(key, out var value)
                   && string.Equals(
                       value.DisplayValue,
                       "true",
                       StringComparison.Ordinal);
        }

        private static float ReadFloat(
            SemanticSnapshot snapshot,
            string key)
        {
            if (!snapshot.Values.TryGetValue(key, out var value)
                || !float.TryParse(
                    value.DisplayValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var result))
            {
                throw new InvalidOperationException(
                    "Semantic float is unavailable: " + key + ".");
            }

            return result;
        }

        private int AutoOperationCount()
        {
            return manager.List().Count(
                descriptor =>
                    descriptor.Reason
                    == ReplaySaveReason.AutomaticInterval);
        }

        private void RunSyntheticSlotSafetyProbe()
        {
            if (slotSafetyCompleted)
            {
                return;
            }

            slotSafetyCompleted = true;
            // The target Mono runtime still applies the legacy Windows path
            // limit. Keep synthetic raw bytes in a short, unique temp path and
            // delete them before publishing probe evidence.
            var privateRoot = Path.Combine(
                Path.GetTempPath(),
                "hktas-t09-"
                + Guid.NewGuid().ToString("N").Substring(0, 12));
            try
            {
                if (Directory.Exists(privateRoot))
                {
                    throw new InvalidOperationException(
                        "Synthetic slot-safety directory already exists.");
                }

                var persistent = Path.Combine(privateRoot, "persistent");
                var backups = Path.Combine(privateRoot, "backups");
                Directory.CreateDirectory(persistent);
                var semantic = journal.BaselineSemanticCapture;
                if (semantic?.Success != true
                    || semantic.CanonicalBytes == null
                    || semantic.Sha256 == null)
                {
                    throw new InvalidOperationException(
                        "Semantic baseline is unavailable for synthetic slot safety.");
                }

                var targetSave = Utf8("HKTAS synthetic target save bytes\n");
                var targetModded = Utf8("{\"synthetic\":\"target\"}");
                var originalSave = Utf8(
                    "HKTAS synthetic original save bytes\n");
                var originalModded = Utf8("{\"synthetic\":\"original\"}");
                var bundle = new BaselineBundle(
                    BaselineBundle.CurrentSchemaVersion,
                    "synthetic-slot-safety",
                    semantic.Sha256,
                    2,
                    DateTimeOffset.UtcNow,
                    targetSave,
                    targetModded,
                    semantic.CanonicalBytes);
                var provider = new DesktopSaveSlotBaselineProvider(
                    persistent,
                    backups);
                var savePath = Path.Combine(persistent, "user4.dat");
                var moddedPath = Path.Combine(
                    persistent,
                    "user4.modded.json");

                WriteAtomic(savePath, targetSave);
                WriteAtomic(moddedPath, targetModded);
                var samePlan = provider.PlanInstall(bundle, 4);
                var same = provider.Install(
                    samePlan,
                    UserOverwriteApproval.None);
                RequireSlotSafety(
                    samePlan.IsAlreadyInstalled
                    && !samePlan.RequiresOverwriteApproval
                    && same.Success
                    && !same.WroteSlot
                    && BytesEqual(File.ReadAllBytes(savePath), targetSave)
                    && BytesEqual(
                        File.ReadAllBytes(moddedPath),
                        targetModded),
                    "identical baseline must not rewrite the slot");

                WriteAtomic(savePath, originalSave);
                WriteAtomic(moddedPath, originalModded);
                var overwritePlan = provider.PlanInstall(bundle, 4);
                var denied = provider.Install(
                    overwritePlan,
                    UserOverwriteApproval.Denied);
                RequireSlotSafety(
                    overwritePlan.RequiresOverwriteApproval
                    && !denied.Success
                    && !denied.WroteSlot
                    && denied.Cancelled
                    && BytesEqual(
                        File.ReadAllBytes(savePath),
                        originalSave)
                    && BytesEqual(
                        File.ReadAllBytes(moddedPath),
                        originalModded),
                    "denied overwrite must leave both files byte-exact");

                var rollbackLease = new DedicatedTasSlotLease(
                    provider,
                    overwritePlan);
                try
                {
                    var approved = rollbackLease.Install(
                        UserOverwriteApproval.Approved);
                    RequireSlotSafety(
                        approved.Success
                        && approved.WroteSlot
                        && Directory.Exists(approved.BackupDirectory)
                        && BytesEqual(
                            File.ReadAllBytes(
                                Path.Combine(
                                    approved.BackupDirectory,
                                    "original.dat")),
                            originalSave)
                        && BytesEqual(
                            File.ReadAllBytes(
                                Path.Combine(
                                    approved.BackupDirectory,
                                    "original.modded.json")),
                            originalModded)
                        && File.Exists(
                            Path.Combine(
                                approved.BackupDirectory,
                                "manifest.json"))
                        && BytesEqual(
                            File.ReadAllBytes(savePath),
                            targetSave)
                        && BytesEqual(
                            File.ReadAllBytes(moddedPath),
                            targetModded),
                        "approved overwrite must back up before install");
                    rollbackLease.Rollback();
                    RequireSlotSafety(
                        BytesEqual(
                            File.ReadAllBytes(savePath),
                            originalSave)
                        && BytesEqual(
                            File.ReadAllBytes(moddedPath),
                            originalModded),
                        "rollback must restore original bytes");
                }
                finally
                {
                    rollbackLease.Dispose();
                }

                var commitPlan = provider.PlanInstall(bundle, 4);
                var commitLease = new DedicatedTasSlotLease(
                    provider,
                    commitPlan);
                try
                {
                    var approved = commitLease.Install(
                        UserOverwriteApproval.Approved);
                    RequireSlotSafety(
                        approved.Success && approved.WroteSlot,
                        "approved committed lease must install");
                    commitLease.Commit();
                }
                finally
                {
                    commitLease.Dispose();
                }

                RequireSlotSafety(
                    BytesEqual(File.ReadAllBytes(savePath), targetSave)
                    && BytesEqual(
                        File.ReadAllBytes(moddedPath),
                        targetModded),
                    "committed lease must retain the target baseline");
                slotSafetyPass = true;
                slotSafetyDetail =
                    "Synthetic identical, deny, backup, rollback, and commit checks passed.";
            }
            catch (Exception exception)
            {
                slotSafetyPass = false;
                slotSafetyDetail =
                    exception.GetType().Name + ": " + exception.Message;
                throw;
            }
            finally
            {
                if (Directory.Exists(privateRoot))
                {
                    Directory.Delete(privateRoot, true);
                }
            }
        }

        private static void RequireSlotSafety(
            bool condition,
            string failure)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    "Synthetic slot-safety check failed: " + failure + ".");
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        private void AdvanceRestoreUpdate(float elapsed)
        {
            if (!restoreStarted && elapsed >= 2f)
            {
                BeginRestoreAttempt();
                return;
            }

            if (!restoreHandle.HasValue)
            {
                return;
            }

            var progress = manager.Poll(restoreHandle.Value);
            if (progress.Phase
                == ReplayRestorePhase.AwaitingOverwriteApproval)
            {
                overwriteApprovalObserved = true;
                if (options.Profile
                    == ReplaySaveProbeProfile.RestoreDeny)
                {
                    var denial = manager.ApproveRestoreOverwrite(
                        restoreHandle.Value,
                        false);
                    var denied = manager.Poll(restoreHandle.Value);
                    var pass =
                        denial.Success
                        && denied.Phase == ReplayRestorePhase.Cancelled
                        && denied.Status == ReplaySaveStatus.Cancelled
                        && denied.RequiresOverwriteApproval == false;
                    WriteRestoreDenialResult(pass, denied);
                    Finish(
                        pass,
                        pass
                            ? string.Empty
                            : "Runtime overwrite denial did not cancel cleanly.",
                        writeResult: false);
                    return;
                }

                if (!options.AllowOverwrite)
                {
                    manager.ApproveRestoreOverwrite(
                        restoreHandle.Value,
                        false);
                    var denied = manager.Poll(restoreHandle.Value);
                    WriteRestoreDenialResult(false, denied);
                    Finish(
                        false,
                        "Restore unexpectedly required overwrite approval; "
                        + "the probe denied it because no explicit approval "
                        + "flag was supplied.",
                        writeResult: false);
                    return;
                }

                manager.ApproveRestoreOverwrite(
                    restoreHandle.Value,
                    true);
            }
            else if (progress.Phase == ReplayRestorePhase.Paused
                     && !restoreAttemptResumed)
            {
                restoreAttemptResumed = true;
                var resume = manager.ResumeRestore(
                    restoreHandle.Value);
                if (!resume.Success)
                {
                    throw new InvalidOperationException(
                        "Verified restore could not resume: "
                        + resume.Error);
                }

                restoreAttempts.Add(resume.Progress);
            }
            else if (progress.Phase == ReplayRestorePhase.Failed
                     || progress.Phase == ReplayRestorePhase.Cancelled)
            {
                WriteRestoreResult(false, progress);
                Finish(
                    false,
                    "Restore attempt failed: " + progress.Detail,
                    writeResult: false);
            }
        }

        private void AdvanceRestoreLateUpdate()
        {
            if (!restoreAttemptResumed || !restoreHandle.HasValue)
            {
                return;
            }

            var progress = manager.Poll(restoreHandle.Value);
            if (progress.Phase != ReplayRestorePhase.Completed)
            {
                return;
            }

            var last = restoreAttempts[restoreAttempts.Count - 1];
            var attemptPass =
                last.StrictSemanticEquivalent == true
                && !string.IsNullOrEmpty(last.ExpectedSemanticSha256)
                && string.Equals(
                    last.ExpectedSemanticSha256,
                    last.ActualSemanticSha256,
                    StringComparison.Ordinal)
                && string.Equals(
                    last.SemanticProjectionId,
                    ReplaySaveSemanticVerifier.ProjectionId,
                    StringComparison.Ordinal)
                && !string.IsNullOrEmpty(
                    last.ExpectedVerificationSha256)
                && string.Equals(
                    last.ExpectedVerificationSha256,
                    last.ActualVerificationSha256,
                    StringComparison.Ordinal)
                && last.BindingRestoreEquivalent == true
                && progress.SettingsRestoreEquivalent == true
                && progress.NextMovieTick
                   == checked(progress.TargetMovieTick + 1);
            if (!attemptPass)
            {
                WriteRestoreResult(false, progress);
                Finish(
                    false,
                    "Restore diagnostics did not satisfy exact semantic "
                    + "SHA-256, binding, settings, or cursor acceptance.",
                    writeResult: false);
                return;
            }

            restoreAttempts[restoreAttempts.Count - 1] = progress;
            restoreAttempt++;
            if (restoreAttempt >= options.RestoreAttempts)
            {
                WriteRestoreResult(true, progress);
                Finish(true, string.Empty, writeResult: false);
                return;
            }

            restoreHandle = null;
            restoreAttemptResumed = false;
            BeginRestoreAttempt();
        }

        private void BeginRestoreAttempt()
        {
            restoreStarted = true;
            restoreAttemptResumed = false;
            restoreHandle = manager.BeginRestore(options.ReplaySaveId);
        }

        private void WriteCaptureResult(
            bool pass,
            IReadOnlyList<ReplaySaveCatalogEntry> entries)
        {
            var builder = new StringBuilder(16384);
            builder.Append("{\"automaticCount\":");
            builder.Append(
                entries.Count(
                        value => value.Descriptor.Reason
                                 == ReplaySaveReason.AutomaticInterval)
                    .ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"combatSemanticDetail\":");
            CanonicalJsonWriter.AppendString(
                builder,
                combatSemanticDetail);
            builder.Append(",\"combatSemanticPass\":");
            builder.Append(combatSemanticPass ? "true" : "false");
            builder.Append(",\"dashSemanticDetail\":");
            CanonicalJsonWriter.AppendString(
                builder,
                dashSemanticDetail);
            builder.Append(",\"dashSemanticPass\":");
            builder.Append(dashSemanticPass ? "true" : "false");
            builder.Append(",\"entries\":[");
            for (var index = 0; index < entries.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var entry = entries[index];
                builder.Append("{\"effectiveMovieTick\":");
                builder.Append(
                    entry.Descriptor.EffectiveMovieTick.ToString(
                        CultureInfo.InvariantCulture));
                builder.Append(",\"id\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Descriptor.ReplaySaveId);
                builder.Append(",\"label\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Descriptor.Label);
                builder.Append(",\"reason\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Descriptor.Reason.ToString());
                builder.Append(",\"requestedAtMovieTick\":");
                builder.Append(
                    entry.Descriptor.RequestedAtMovieTick.ToString(
                        CultureInfo.InvariantCulture));
                builder.Append(",\"scene\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Descriptor.SceneName);
                builder.Append(",\"semanticSnapshotSha256\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Descriptor.SemanticSnapshotSha256);
                builder.Append(",\"status\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    entry.Status.ToString());
                builder.Append('}');
            }

            builder.Append("],\"error\":");
            CanonicalJsonWriter.AppendString(builder, error);
            builder.Append(",\"manualCount\":");
            builder.Append(
                entries.Count(
                        value => value.Descriptor.Reason
                                 != ReplaySaveReason.AutomaticInterval)
                    .ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"jumpSemanticPass\":");
            builder.Append(jumpSemanticPass ? "true" : "false");
            builder.Append(",\"jumpSemanticDetail\":");
            CanonicalJsonWriter.AppendString(
                builder,
                jumpSemanticDetail);
            builder.Append(",\"heroControlRestoreEquivalent\":");
            builder.Append(
                heroControlRestoreEquivalent == true
                    ? "true"
                    : "false");
            builder.Append(",\"pauseAutoCountAfter\":");
            builder.Append(
                pauseAutoCountAfter.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"pauseAutoCountBefore\":");
            builder.Append(
                pauseAutoCountBefore.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"pauseDurationSeconds\":");
            builder.Append(
                pauseDuration.ToString(
                    "R",
                    CultureInfo.InvariantCulture));
            builder.Append(",\"pauseJournalTickAfter\":");
            builder.Append(
                pauseJournalTickAfter.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"pauseJournalTickBefore\":");
            builder.Append(
                pauseJournalTickBefore.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"slotSafetyDetail\":");
            CanonicalJsonWriter.AppendString(builder, slotSafetyDetail);
            builder.Append(",\"slotSafetyPass\":");
            builder.Append(slotSafetyPass ? "true" : "false");
            builder.Append(",\"stationarySemanticPass\":");
            builder.Append(stationarySemanticPass ? "true" : "false");
            builder.Append(",\"stationarySemanticDetail\":");
            CanonicalJsonWriter.AppendString(
                builder,
                stationarySemanticDetail);
            AppendCommon(builder, pass);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                Utf8(builder.ToString()));
        }

        private void WriteRestoreResult(
            bool pass,
            ReplayRestoreProgress final)
        {
            var builder = new StringBuilder(16384);
            builder.Append("{\"attemptCount\":");
            builder.Append(
                restoreAttempts.Count.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append(",\"attempts\":[");
            for (var index = 0; index < restoreAttempts.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var attempt = restoreAttempts[index];
                builder.Append("{\"actualSemanticSha256\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    attempt.ActualSemanticSha256);
                builder.Append(",\"actualVerificationSha256\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    attempt.ActualVerificationSha256);
                builder.Append(",\"bindingRestoreEquivalent\":");
                builder.Append(
                    attempt.BindingRestoreEquivalent == true
                        ? "true"
                        : "false");
                builder.Append(",\"expectedSemanticSha256\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    attempt.ExpectedSemanticSha256);
                builder.Append(",\"expectedVerificationSha256\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    attempt.ExpectedVerificationSha256);
                builder.Append(",\"nextMovieTick\":");
                builder.Append(
                    attempt.NextMovieTick.ToString(
                        CultureInfo.InvariantCulture));
                builder.Append(",\"settingsRestoreEquivalent\":");
                builder.Append(
                    attempt.SettingsRestoreEquivalent == true
                        ? "true"
                        : "false");
                builder.Append(",\"semanticProjectionId\":");
                CanonicalJsonWriter.AppendString(
                    builder,
                    attempt.SemanticProjectionId);
                builder.Append(",\"strictSemanticEquivalent\":");
                if (attempt.StrictSemanticEquivalent.HasValue)
                {
                    builder.Append(
                        attempt.StrictSemanticEquivalent.Value
                            ? "true"
                            : "false");
                }
                else
                {
                    builder.Append("null");
                }
                builder.Append(",\"targetMovieTick\":");
                builder.Append(
                    attempt.TargetMovieTick.ToString(
                        CultureInfo.InvariantCulture));
                builder.Append('}');
            }

            builder.Append("],\"error\":");
            CanonicalJsonWriter.AppendString(
                builder,
                pass ? string.Empty : final.Detail);
            builder.Append(",\"replaySaveId\":");
            CanonicalJsonWriter.AppendString(
                builder,
                options.ReplaySaveId);
            AppendCommon(builder, pass);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                Utf8(builder.ToString()));
        }

        private void WriteRestoreDenialResult(
            bool pass,
            ReplayRestoreProgress progress)
        {
            var builder = new StringBuilder(1024);
            builder.Append("{\"error\":");
            CanonicalJsonWriter.AppendString(
                builder,
                pass ? string.Empty : progress.Detail);
            builder.Append(",\"overwriteApprovalObserved\":");
            builder.Append(
                overwriteApprovalObserved ? "true" : "false");
            builder.Append(",\"phase\":");
            CanonicalJsonWriter.AppendString(
                builder,
                progress.Phase.ToString());
            builder.Append(",\"replaySaveId\":");
            CanonicalJsonWriter.AppendString(
                builder,
                options.ReplaySaveId);
            builder.Append(",\"status\":");
            CanonicalJsonWriter.AppendString(
                builder,
                progress.Status.ToString());
            AppendCommon(builder, pass);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                Utf8(builder.ToString()));
        }

        private void AppendCommon(StringBuilder builder, bool pass)
        {
            builder.Append(",\"profile\":");
            CanonicalJsonWriter.AppendString(builder, options.ProfileId);
            builder.Append(",\"runId\":");
            CanonicalJsonWriter.AppendString(builder, options.RunId);
            builder.Append(",\"runPass\":");
            builder.Append(pass ? "true" : "false");
        }

        private void Fail(Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            logError("T09 replay-save probe failed: " + exception);
            Finish(false, error);
        }

        private void Finish(
            bool pass,
            string failure,
            bool writeResult = true)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            if (!pass && string.IsNullOrEmpty(error))
            {
                error = failure;
            }

            if (writeResult)
            {
                var builder = new StringBuilder(1024);
                builder.Append("{\"error\":");
                CanonicalJsonWriter.AppendString(builder, error);
                AppendCommon(builder, pass);
                builder.Append('}');
                WriteAtomic(
                    Path.Combine(outputDirectory, "result.json"),
                    Utf8(builder.ToString()));
            }

            playbackController?.Dispose();
            playbackController = null;
            pauseController?.Dispose();
            pauseController = null;
            logInfo(
                "T09 replay-save probe stopped profile="
                + options.ProfileId
                + " pass="
                + pass);
            if (options.ExitWhenDone)
            {
                exitRequested = true;
            }
        }

        private static byte[] Utf8(string value)
        {
            return new UTF8Encoding(false).GetBytes(value);
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private sealed class GameplayPauseGate : IMovieTickGate
        {
            private readonly RuntimePauseController pause;

            public GameplayPauseGate(RuntimePauseController pause)
            {
                this.pause = pause;
            }

            public bool TryAuthorizeMovieTick(ulong rawInputTick)
            {
                var gameManager = GameManager.instance;
                var hero = HeroController.SilentInstance;
                return gameManager != null
                       && gameManager.gameState == GameState.PLAYING
                       && !gameManager.IsInSceneTransition
                       && ReplayDeterministicTimingLease
                           .MovieTicksMayAdvance()
                       && hero != null
                       && hero.gameObject.activeInHierarchy
                       && pause.TryAuthorizeMovieTick(rawInputTick);
            }

            public void OnMovieTickSkipped(ulong rawInputTick)
            {
                pause.OnMovieTickSkipped(rawInputTick);
            }

            public void OnMovieTickCommitted(
                long movieTick,
                TickStamp stamp)
            {
                pause.OnMovieTickCommitted(movieTick, stamp);
            }
        }
    }

    internal enum ReplaySaveProbeProfile
    {
        Capture,
        Restore,
        RestoreDeny
    }

    internal sealed class ReplaySaveProbeOptions
    {
        private ReplaySaveProbeOptions(
            ReplaySaveProbeProfile profile,
            string runId,
            int autoLoadSlot,
            string replaySaveId,
            int restoreAttempts,
            bool allowOverwrite,
            bool exitWhenDone)
        {
            Profile = profile;
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
            ReplaySaveId = replaySaveId;
            RestoreAttempts = restoreAttempts;
            AllowOverwrite = allowOverwrite;
            ExitWhenDone = exitWhenDone;
        }

        public ReplaySaveProbeProfile Profile { get; }
        public string ProfileId =>
            Profile == ReplaySaveProbeProfile.RestoreDeny
                ? "RESTORE_DENY"
                : Profile.ToString().ToUpperInvariant();
        public string RunId { get; }
        public int AutoLoadSlot { get; }
        public string ReplaySaveId { get; }
        public int RestoreAttempts { get; }
        public bool AllowOverwrite { get; }
        public bool ExitWhenDone { get; }

        public static ReplaySaveProbeOptionsParseResult Parse(
            IReadOnlyList<string> arguments)
        {
            var profileText = ReadValue(
                arguments,
                "--hktas-replay-save-probe=");
            if (profileText == null)
            {
                return ReplaySaveProbeOptionsParseResult.NotRequested();
            }

            if (!Enum.TryParse(
                    profileText.Replace("_", string.Empty),
                    ignoreCase: true,
                    out ReplaySaveProbeProfile profile))
            {
                return ReplaySaveProbeOptionsParseResult.Failed(
                    "profile must be CAPTURE, RESTORE, or RESTORE_DENY.");
            }

            var runId = ReadValue(
                            arguments,
                            "--hktas-replay-save-probe-run=")
                        ?? profileText.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!MovieProtocolV1.IsIdentifier(runId))
            {
                return ReplaySaveProbeOptionsParseResult.Failed(
                    "run ID must be a Movie v1 identifier.");
            }

            var slotText = ReadValue(
                arguments,
                "--hktas-replay-save-probe-slot=");
            var slot = 0;
            if (profile == ReplaySaveProbeProfile.Capture
                && (!int.TryParse(
                        slotText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out slot)
                    || slot <= 0
                    || slot > 4))
            {
                return ReplaySaveProbeOptionsParseResult.Failed(
                    "capture profile requires a slot in [1, 4].");
            }

            var replaySaveId = ReadValue(
                                   arguments,
                                   "--hktas-replay-save-id=")
                               ?? string.Empty;
            if (profile != ReplaySaveProbeProfile.Capture
                && !MovieProtocolV1.IsIdentifier(replaySaveId))
            {
                return ReplaySaveProbeOptionsParseResult.Failed(
                    "restore profile requires a canonical replay-save ID.");
            }

            var attemptsText = ReadValue(
                arguments,
                "--hktas-replay-save-attempts=");
            var attempts = 10;
            if (attemptsText != null
                && (!int.TryParse(
                        attemptsText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out attempts)
                    || attempts <= 0
                    || attempts > 100))
            {
                return ReplaySaveProbeOptionsParseResult.Failed(
                    "restore attempts must be in [1, 100].");
            }

            return ReplaySaveProbeOptionsParseResult.Succeeded(
                new ReplaySaveProbeOptions(
                    profile,
                    runId,
                    slot,
                    replaySaveId,
                    attempts,
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-replay-save-approve-overwrite",
                            StringComparison.Ordinal)),
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-replay-save-probe-exit",
                            StringComparison.Ordinal))));
        }

        private static string? ReadValue(
            IEnumerable<string> arguments,
            string prefix)
        {
            foreach (var argument in arguments)
            {
                if (argument.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return argument.Substring(prefix.Length);
                }
            }

            return null;
        }
    }

    internal sealed class ReplaySaveProbeOptionsParseResult
    {
        private ReplaySaveProbeOptionsParseResult(
            bool requested,
            ReplaySaveProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public ReplaySaveProbeOptions? Options { get; }
        public string? Error { get; }

        public static ReplaySaveProbeOptionsParseResult NotRequested()
        {
            return new ReplaySaveProbeOptionsParseResult(
                false,
                null,
                null);
        }

        public static ReplaySaveProbeOptionsParseResult Succeeded(
            ReplaySaveProbeOptions options)
        {
            return new ReplaySaveProbeOptionsParseResult(
                true,
                options,
                null);
        }

        public static ReplaySaveProbeOptionsParseResult Failed(string error)
        {
            return new ReplaySaveProbeOptionsParseResult(
                true,
                null,
                error);
        }
    }

    [DefaultExecutionOrder(-50)]
    internal sealed class RuntimeReplaySaveProbeRunner : MonoBehaviour
    {
        private RuntimeReplaySaveProbeExperiment? owner;

        internal void Initialize(RuntimeReplaySaveProbeExperiment value)
        {
            owner = value;
        }

        private void Update()
        {
            owner?.OnUpdate();
        }

        private void LateUpdate()
        {
            owner?.OnLateUpdate();
        }
    }
}
