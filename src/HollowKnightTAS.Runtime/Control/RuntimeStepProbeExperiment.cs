using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Control;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.Verification;
using HollowKnightTAS.Runtime.Input;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Control
{
    public sealed class RuntimeStepProbeExperiment : IDisposable
    {
        private const string RouteScene = "GG_Vengefly";
        private const string SceneStopTarget = "GG_Workshop";
        private const float RouteStartX = 45.19f;
        private const float RouteStartY = 13.400625f;
        private const int StepCountTarget = 100;
        private const float PauseSecondsTarget = 5f;

        private readonly string manifestSha256;
        private readonly StepProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logDebug;
        private readonly Action<string> logError;
        private readonly string outputDirectory;
        private readonly string sessionId;
        private readonly RuntimeSnapshotCapture snapshotCapture =
            new RuntimeSnapshotCapture();
        private readonly List<StepResult> stepResults =
            new List<StepResult>(StepCountTarget);

        private RuntimeStepProbeRunner? runner;
        private RuntimePauseController? pauseController;
        private RuntimePlaybackController? playbackController;
        private MovieDocument? movie;
        private SnapshotCaptureResult? baselineCapture;
        private BindingRestoreReport? bindingRestore;
        private PlaybackStopReason? playbackStopReason;
        private bool attached;
        private bool playbackStarted;
        private bool playbackStopped;
        private bool pauseRequested;
        private bool pauseCaptureReady;
        private bool actionIssued;
        private bool stopped;
        private bool exitRequested;
        private bool exitIssued;
        private bool loadRequested;
        private bool routeTransitionRequested;
        private bool routeHarnessApplied;
        private bool originalAcceptingInput;
        private bool pauseHashStable;
        private bool pauseCursorStable;
        private bool pauseFixedStable;
        private bool runPass;
        private int warmupLateUpdates;
        private int pausedStableLateUpdates;
        private int settleLateUpdates;
        private int suppressedHealthManagerCount;
        private long observationCount;
        private long mismatchCount;
        private long pauseMovieBefore;
        private long pauseMovieAfter;
        private long pauseVisualBefore;
        private long pauseVisualAfter;
        private long pauseFixedBefore;
        private long pauseFixedAfter;
        private ulong pauseInputBefore;
        private ulong pauseInputAfter;
        private float startedAtRealtime;
        private float attachedAtRealtime;
        private float pauseStartedAtRealtime;
        private float pauseDurationSeconds;
        private string pauseBeforeHash = string.Empty;
        private string pauseAfterHash = string.Empty;
        private string endpointHash = string.Empty;
        private string stopReason = "NotStopped";
        private string error = string.Empty;
        private string namingVerdict = "NOT_APPLICABLE";
        private LedgerValidationReport? ledgerValidation;
        private Vector3 originalHeroPosition;
        private Vector2 originalHeroBodyPosition;

        private RuntimeStepProbeExperiment(
            string sessionDirectory,
            string manifestSha256,
            StepProbeOptions options,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logError)
        {
            this.manifestSha256 = manifestSha256;
            this.options = options;
            this.logInfo = logInfo;
            this.logDebug = logDebug;
            this.logError = logError;
            sessionId = new DirectoryInfo(sessionDirectory).Name;
            outputDirectory = Path.Combine(
                sessionDirectory,
                "step",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
        }

        public static RuntimeStepProbeExperiment? TryStart(
            string sessionDirectory,
            string manifestSha256,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            var parse = StepProbeOptions.Parse(Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T08 step probe arguments: " + parse.Error);
            }

            var experiment = new RuntimeStepProbeExperiment(
                sessionDirectory,
                manifestSha256,
                parse.Options,
                logInfo,
                logDebug,
                logError);
            experiment.Start();
            return experiment;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                if (options.Profile == StepProbeProfile.Shutdown
                    && actionIssued)
                {
                    FinishFromApplicationQuit();
                }
                else
                {
                    Finish(
                        false,
                        "ApplicationQuit",
                        "Application quit before probe completion.");
                }
            }

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
                if (!loadRequested
                    && elapsed >= 2f
                    && options.AutoLoadSlot > 0
                    && string.Equals(
                        USceneManager.GetActiveScene().name,
                        "Menu_Title",
                        StringComparison.Ordinal)
                    && GameManager.instance != null)
                {
                    loadRequested = true;
                    GameManager.instance.LoadGameFromUI(options.AutoLoadSlot);
                    return;
                }

                if (!attached)
                {
                    TryAttach();
                    if (!attached && elapsed > 90f)
                    {
                        Finish(
                            false,
                            "StartupTimeout",
                            "Playable Hero was unavailable.");
                    }

                    return;
                }

                if (Time.realtimeSinceStartup - attachedAtRealtime > 180f)
                {
                    Finish(
                        false,
                        "RunTimeout",
                        "Step probe exceeded 180 seconds.");
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        internal void OnLateUpdate()
        {
            if (stopped || !attached)
            {
                return;
            }

            try
            {
                DisableHazards();
                if (!playbackStarted)
                {
                    warmupLateUpdates++;
                    if (warmupLateUpdates >= 30)
                    {
                        StartPlayback();
                    }

                    return;
                }

                if (options.Profile == StepProbeProfile.Baseline)
                {
                    CompleteBaselineWhenReady();
                    return;
                }

                if (options.Profile == StepProbeProfile.Step)
                {
                    AdvanceStepProfile();
                    return;
                }

                AdvancePauseOrRestoreProfile();
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
                "HollowKnightTAS.RuntimeStepProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimeStepProbeRunner>();
            runner.Initialize(this);
            logInfo(
                "T08 step probe created profile="
                + options.ProfileId
                + " runId="
                + options.RunId);
        }

        private void TryAttach()
        {
            var hero = HeroController.SilentInstance;
            var manager = GameManager.instance;
            var input = InputHandler.Instance;
            if (hero == null
                || !hero.gameObject.activeInHierarchy
                || manager == null
                || manager.gameState != GameState.PLAYING
                || input == null
                || input.inputActions == null)
            {
                return;
            }

            if (!string.Equals(
                    USceneManager.GetActiveScene().name,
                    RouteScene,
                    StringComparison.Ordinal))
            {
                if (!routeTransitionRequested)
                {
                    routeTransitionRequested = true;
                    manager.BeginSceneTransition(
                        new GameManager.SceneLoadInfo
                        {
                            AlwaysUnloadUnusedAssets = true,
                            EntryGateName = "door_dreamEnter",
                            EntryDelay = 0f,
                            PreventCameraFadeOut = true,
                            WaitForSceneTransitionCameraFade = false,
                            SceneName = RouteScene,
                            Visualization =
                                GameManager.SceneLoadVisualizations.GodsAndGlory
                        });
                }

                return;
            }

            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "Hero Rigidbody2D is unavailable.");
            attached = true;
            attachedAtRealtime = Time.realtimeSinceStartup;
            originalAcceptingInput = hero.acceptingInput;
            originalHeroPosition = hero.gameObject.transform.position;
            originalHeroBodyPosition = body.position;
            hero.RelinquishControl();
            hero.RegainControl();
            body.velocity = Vector2.zero;
            routeHarnessApplied = true;
            logDebug("T08 step probe attached scene=" + RouteScene);
        }

        private void StartPlayback()
        {
            NormalizeRouteBaseline();
            baselineCapture = Capture("baseline");
            var baselineHash = baselineCapture.Sha256
                               ?? throw new InvalidOperationException(
                                   "Baseline hash is unavailable.");
            var baselineId = "slot-"
                             + options.AutoLoadSlot.ToString(
                                 CultureInfo.InvariantCulture)
                             + "-gg-vengefly";
            movie = CreateMovie(baselineId, baselineHash);
            var writer = new MovieCanonicalWriter();
            WriteAtomic(
                Path.Combine(outputDirectory, "generated-step-route.hktas"),
                writer.WriteUtf8(movie));
            WriteAtomic(
                Path.Combine(outputDirectory, "generated-step-route.movie-id"),
                Utf8(writer.ComputeMovieId(movie) + "\n"));

            pauseController = new RuntimePauseController(
                sessionId,
                manifestSha256,
                options.RunId,
                options.ProfileId);
            playbackController = new RuntimePlaybackController(
                OnObservation,
                _ => { },
                OnPlaybackStopped,
                pauseController);
            var start = playbackController.StartReplay(
                movie,
                new PlaybackContext(
                    manifestSha256,
                    baselineId,
                    baselineHash,
                    playbackController.SceneEpoch));
            if (!start.Success)
            {
                throw new InvalidOperationException(
                    "Runtime playback start failed: " + start.Error);
            }

            playbackStarted = true;
            if (options.Profile == StepProbeProfile.Step)
            {
                var pause = pauseController.Pause();
                if (!pause.Success)
                {
                    throw new InvalidOperationException(
                        "Could not pause before the first step: "
                        + pause.Error);
                }

                pauseRequested = true;
            }

            WriteAtomic(
                Path.Combine(outputDirectory, "probe.ready"),
                Utf8(options.ProfileId + " " + options.RunId + "\n"));
        }

        private MovieDocument CreateMovie(
            string baselineId,
            string baselineHash)
        {
            var held = options.Profile == StepProbeProfile.PauseMoving
                ? TasAction.Left
                : TasAction.None;
            var count = options.Profile == StepProbeProfile.Baseline
                        || options.Profile == StepProbeProfile.Step
                ? StepCountTarget
                : 600;
            var sourceName = "generated-step-route.hktas";
            var span = new MovieSourceSpan(sourceName, 1, 1, 1);
            return new MovieDocument(
                sourceName,
                new MovieHeader(
                    1,
                    "1.5.78.11833",
                    "1.5.78.11833-77",
                    manifestSha256,
                    baselineId,
                    baselineHash,
                    "input"),
                new MovieCommand[]
                {
                    new MarkerCommand("t08 start", span),
                    new FrameRunCommand(
                        count,
                        held,
                        0,
                        0,
                        false,
                        span),
                    new AssertCommand(
                        "scene.name",
                        "==",
                        RouteScene,
                        true,
                        span)
                });
        }

        private void AdvanceStepProfile()
        {
            var controller = RequirePauseController();
            while (controller.TryTakeCompletedStep(out var completed))
            {
                stepResults.Add(
                    completed
                    ?? throw new InvalidOperationException(
                        "Completed step queue returned null."));
            }

            if (stepResults.Count < StepCountTarget
                && !playbackStopped
                && controller.Mode == SimulationControlMode.Paused)
            {
                var start = controller.Step(
                    new StepRequest(StepBoundary.MovieTick, 1));
                if (!start.Success)
                {
                    throw new InvalidOperationException(
                        "Step request failed: " + start.Error);
                }

                return;
            }

            if (stepResults.Count != StepCountTarget || !playbackStopped)
            {
                return;
            }

            settleLateUpdates++;
            if (settleLateUpdates < 3)
            {
                return;
            }

            endpointHash = CaptureVerificationHash("endpoint");
            namingVerdict = CalculateNamingVerdict(stepResults);
            var resume = controller.Resume();
            if (!resume.Success)
            {
                throw new InvalidOperationException(
                    "Resume after stepping failed: " + resume.Error);
            }

            var allExact = stepResults.All(
                value => value.MovieTickDelta == 1);
            Finish(
                allExact
                && observationCount == StepCountTarget + 1
                && mismatchCount == 0,
                "Completed",
                string.Empty);
        }

        private void CompleteBaselineWhenReady()
        {
            if (!playbackStopped)
            {
                return;
            }

            settleLateUpdates++;
            if (settleLateUpdates < 3)
            {
                return;
            }

            endpointHash = CaptureVerificationHash("endpoint");
            Finish(
                playbackStopReason == PlaybackStopReason.Completed
                && observationCount == StepCountTarget + 1
                && mismatchCount == 0,
                "Completed",
                string.Empty);
        }

        private void AdvancePauseOrRestoreProfile()
        {
            var controller = RequirePauseController();
            if (!pauseRequested && observationCount >= 20)
            {
                var pause = controller.Pause();
                if (!pause.Success)
                {
                    throw new InvalidOperationException(
                        "Pause request failed: " + pause.Error);
                }

                pauseRequested = true;
                return;
            }

            if (!pauseRequested
                || controller.Mode == SimulationControlMode.Pausing)
            {
                return;
            }

            if (!pauseCaptureReady
                && controller.Mode == SimulationControlMode.Paused)
            {
                pausedStableLateUpdates++;
                if (pausedStableLateUpdates < 3)
                {
                    return;
                }

                pauseBeforeHash = CaptureVerificationHash("pause-before");
                pauseMovieBefore = controller.TotalMovieTicks;
                pauseVisualBefore = controller.VisualTick;
                pauseFixedBefore = controller.FixedTick;
                pauseInputBefore = controller.CurrentRawInputTick;
                pauseStartedAtRealtime = Time.realtimeSinceStartup;
                pauseCaptureReady = true;
                return;
            }

            if (!pauseCaptureReady || actionIssued)
            {
                CompleteRestoreProfileWhenReady();
                return;
            }

            if (options.Profile == StepProbeProfile.PauseStationary
                || options.Profile == StepProbeProfile.PauseMoving)
            {
                if (Time.realtimeSinceStartup - pauseStartedAtRealtime
                    < PauseSecondsTarget)
                {
                    return;
                }

                CapturePauseAfter(controller);
                var resume = controller.Resume();
                if (!resume.Success)
                {
                    throw new InvalidOperationException(
                        "Resume after pause test failed: " + resume.Error);
                }

                playbackController!.Stop(PlaybackStopReason.Manual);
                actionIssued = true;
                return;
            }

            actionIssued = true;
            switch (options.Profile)
            {
                case StepProbeProfile.Resume:
                    var resume = controller.Resume();
                    if (!resume.Success)
                    {
                        throw new InvalidOperationException(
                            "Normal resume failed: " + resume.Error);
                    }

                    playbackController!.Stop(PlaybackStopReason.Manual);
                    break;
                case StepProbeProfile.Scene:
                    var manager = GameManager.instance
                                  ?? throw new InvalidOperationException(
                                      "GameManager disappeared before scene restore.");
                    manager.BeginSceneTransition(
                        new GameManager.SceneLoadInfo
                        {
                            AlwaysUnloadUnusedAssets = true,
                            EntryGateName = "door_dreamEnter",
                            EntryDelay = 0f,
                            PreventCameraFadeOut = true,
                            WaitForSceneTransitionCameraFade = false,
                            SceneName = SceneStopTarget,
                            Visualization =
                                GameManager.SceneLoadVisualizations.GodsAndGlory
                        });
                    break;
                case StepProbeProfile.Focus:
                    controller.TriggerFocusLossForProbe();
                    playbackController!.Stop(PlaybackStopReason.Manual);
                    break;
                case StepProbeProfile.Fault:
                    controller.TriggerControlledFault("t08-probe");
                    playbackController!.Stop(PlaybackStopReason.Manual);
                    break;
                case StepProbeProfile.Shutdown:
                    Application.Quit();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void CompleteRestoreProfileWhenReady()
        {
            if (!actionIssued
                || options.Profile == StepProbeProfile.Shutdown
                || !playbackStopped)
            {
                return;
            }

            var report = RequirePauseController().LastRestoreReport;
            if (report == null)
            {
                return;
            }

            settleLateUpdates++;
            if (settleLateUpdates < 3)
            {
                return;
            }

            var expectedPlaybackReason =
                options.Profile == StepProbeProfile.Scene
                    ? PlaybackStopReason.SceneChanged
                    : PlaybackStopReason.Manual;
            var abortReasonPass = options.Profile switch
            {
                StepProbeProfile.PauseStationary => true,
                StepProbeProfile.PauseMoving => true,
                StepProbeProfile.Resume => true,
                StepProbeProfile.Scene =>
                    RequirePauseController().LastAbortReason.StartsWith(
                        "scene-load-requested:",
                        StringComparison.Ordinal)
                    || RequirePauseController().LastAbortReason.StartsWith(
                        "scene-change:",
                        StringComparison.Ordinal),
                StepProbeProfile.Focus =>
                    string.Equals(
                        RequirePauseController().LastAbortReason,
                        "focus-loss",
                        StringComparison.Ordinal),
                StepProbeProfile.Fault =>
                    RequirePauseController().LastAbortReason.StartsWith(
                        "controlled-fault:t08-probe",
                        StringComparison.Ordinal),
                _ => false
            };
            Finish(
                report.Attempted
                && report.Equivalent
                && abortReasonPass
                && playbackStopReason == expectedPlaybackReason,
                "Completed",
                string.Empty);
        }

        private void CapturePauseAfter(RuntimePauseController controller)
        {
            pauseAfterHash = CaptureVerificationHash("pause-after");
            pauseMovieAfter = controller.TotalMovieTicks;
            pauseVisualAfter = controller.VisualTick;
            pauseFixedAfter = controller.FixedTick;
            pauseInputAfter = controller.CurrentRawInputTick;
            pauseDurationSeconds =
                Time.realtimeSinceStartup - pauseStartedAtRealtime;
            pauseHashStable = string.Equals(
                pauseBeforeHash,
                pauseAfterHash,
                StringComparison.Ordinal);
            pauseCursorStable = pauseMovieBefore == pauseMovieAfter;
            pauseFixedStable = pauseFixedBefore == pauseFixedAfter;
        }

        private void OnObservation(ReplayInputObservation observation)
        {
            observationCount++;
            if (!observation.Matches)
            {
                mismatchCount++;
            }
        }

        private void OnPlaybackStopped(
            PlaybackStopReason reason,
            BindingRestoreReport report)
        {
            playbackStopReason = reason;
            bindingRestore = report;
            playbackStopped = true;
        }

        private void FinishFromApplicationQuit()
        {
            stopped = true;
            stopReason = "ApplicationQuit";
            pauseController?.Dispose();
            playbackController?.Dispose();
            var report = pauseController?.LastRestoreReport;
            runPass = report?.Attempted == true
                      && report.Equivalent
                      && string.Equals(
                          pauseController?.LastAbortReason,
                          "mod-shutdown",
                          StringComparison.Ordinal)
                      && bindingRestore?.Equivalent == true
                      && ValidateLedger();
            WriteEvidence();
            RestoreRouteHarness();
            logInfo(
                "T08 shutdown probe stopped pass="
                + (runPass ? "true" : "false"));
        }

        private void Finish(bool provisionalPass, string reason, string message)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            stopReason = reason;
            error = message ?? string.Empty;
            try
            {
                pauseController?.Dispose();
                playbackController?.Dispose();
                runPass = provisionalPass
                          && EvaluateCommonAcceptance();
                WriteEvidence();
            }
            catch (Exception exception)
            {
                runPass = false;
                error = string.IsNullOrEmpty(error)
                    ? exception.ToString()
                    : error + " | cleanup: " + exception;
                logError("T08 step probe cleanup failed: " + exception);
                TryWriteEvidence();
            }
            finally
            {
                RestoreRouteHarness();
            }

            logInfo(
                "T08 step probe stopped profile="
                + options.ProfileId
                + " pass="
                + (runPass ? "true" : "false")
                + " reason="
                + stopReason);
            if (options.ExitOnComplete)
            {
                exitRequested = true;
            }
        }

        private bool EvaluateCommonAcceptance()
        {
            if (!ValidateLedger())
            {
                return false;
            }

            if (bindingRestore?.Attempted != true
                || bindingRestore.Equivalent != true)
            {
                return false;
            }

            if (options.Profile == StepProbeProfile.Baseline)
            {
                return !string.IsNullOrEmpty(endpointHash);
            }

            var restore = pauseController?.LastRestoreReport;
            if (restore?.Attempted != true || !restore.Equivalent)
            {
                return false;
            }

            if (options.Profile == StepProbeProfile.PauseStationary
                || options.Profile == StepProbeProfile.PauseMoving)
            {
                return pauseDurationSeconds >= PauseSecondsTarget
                       && pauseHashStable
                       && pauseCursorStable
                       && pauseFixedStable
                       && pauseVisualAfter > pauseVisualBefore
                       && pauseInputAfter > pauseInputBefore;
            }

            if (options.Profile == StepProbeProfile.Step)
            {
                return stepResults.Count == StepCountTarget
                       && stepResults.All(
                           value => value.MovieTickDelta == 1)
                       && !string.IsNullOrEmpty(endpointHash);
            }

            return true;
        }

        private bool ValidateLedger()
        {
            ledgerValidation = pauseController == null
                ? null
                : TickLedgerValidator.Validate(pauseController.Ledger);
            return ledgerValidation?.IsValid == true;
        }

        private void Fail(Exception exception)
        {
            logError("T08 step probe failed: " + exception);
            Finish(false, exception.GetType().Name, exception.Message);
        }

        private RuntimePauseController RequirePauseController()
        {
            return pauseController
                   ?? throw new InvalidOperationException(
                       "Pause controller is unavailable.");
        }

        private SnapshotCaptureResult Capture(string name)
        {
            var capture = snapshotCapture.Capture(
                new TickStamp(
                    RequirePauseControllerOrZeroInputTick(),
                    pauseController?.VisualTick ?? 0,
                    pauseController?.FixedTick ?? 0,
                    pauseController?.SceneEpoch ?? 0,
                    TickPhase.LateUpdateEnd));
            if (!capture.Success || capture.CanonicalBytes == null)
            {
                throw new InvalidOperationException(
                    name
                    + " snapshot failed at "
                    + capture.FailedProbeId
                    + ": "
                    + capture.Error);
            }

            WriteAtomic(
                Path.Combine(outputDirectory, name + ".snapshot"),
                capture.CanonicalBytes);
            WriteAtomic(
                Path.Combine(outputDirectory, name + ".snapshot.sha256"),
                Utf8(capture.Sha256 + "\n"));
            return capture;
        }

        private string CaptureVerificationHash(string name)
        {
            var capture = Capture(name);
            var normalized = VerificationSnapshotNormalizer.Normalize(
                capture.CanonicalBytes!);
            var hash = Sha256Utility.ComputeHex(normalized);
            WriteAtomic(
                Path.Combine(
                    outputDirectory,
                    name + ".verification.snapshot"),
                normalized);
            WriteAtomic(
                Path.Combine(
                    outputDirectory,
                    name + ".verification.snapshot.sha256"),
                Utf8(hash + "\n"));
            return hash;
        }

        private ulong RequirePauseControllerOrZeroInputTick()
        {
            return pauseController?.CurrentRawInputTick ?? 0;
        }

        private void NormalizeRouteBaseline()
        {
            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "Hero disappeared before baseline normalization.");
            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "Hero Rigidbody2D disappeared before normalization.");
            body.velocity = Vector2.zero;
            body.position = new Vector2(RouteStartX, RouteStartY);
            hero.gameObject.transform.position =
                new Vector3(
                    RouteStartX,
                    RouteStartY,
                    hero.gameObject.transform.position.z);
            Physics2D.SyncTransforms();
        }

        private void DisableHazards()
        {
            var hero = HeroController.SilentInstance;
            foreach (var healthManager in
                     UnityEngine.Object.FindObjectsOfType<HealthManager>())
            {
                if (healthManager == null
                    || healthManager.gameObject == null
                    || !healthManager.gameObject.activeSelf
                    || (hero != null
                        && ReferenceEquals(
                            healthManager.gameObject,
                            hero.gameObject)))
                {
                    continue;
                }

                healthManager.gameObject.SetActive(false);
                suppressedHealthManagerCount++;
            }
        }

        private void RestoreRouteHarness()
        {
            if (!routeHarnessApplied)
            {
                return;
            }

            routeHarnessApplied = false;
            var hero = HeroController.SilentInstance;
            if (hero == null || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            var body = hero.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.position = originalHeroBodyPosition;
            }

            hero.gameObject.transform.position = originalHeroPosition;
            if (!originalAcceptingInput)
            {
                hero.RelinquishControl();
            }
        }

        private void WriteEvidence()
        {
            WriteLedger();
            WriteStepResults();
            WriteTimeSettings();
            WriteResult();
            WriteVerdict();
        }

        private void TryWriteEvidence()
        {
            try
            {
                WriteEvidence();
            }
            catch (Exception exception)
            {
                logError("T08 fallback evidence write failed: " + exception);
            }
        }

        private void WriteLedger()
        {
            var builder = new StringBuilder(65536);
            if (pauseController != null)
            {
                foreach (var record in pauseController.Ledger)
                {
                    builder.Append(TickLedgerRecordJson.Serialize(record));
                    builder.Append('\n');
                }
            }

            WriteAtomic(
                Path.Combine(outputDirectory, "step-ledger.jsonl"),
                Utf8(builder.ToString()));
        }

        private void WriteStepResults()
        {
            var builder = new StringBuilder(32768);
            for (var index = 0; index < stepResults.Count; index++)
            {
                var result = stepResults[index];
                builder.Append('{');
                AppendNumber(builder, "index", index + 1);
                AppendString(
                    builder,
                    "boundary",
                    result.Request.Boundary.ToString());
                AppendNumber(builder, "requestedCount", result.Request.Count);
                AppendNumber(
                    builder,
                    "movieTickDelta",
                    result.MovieTickDelta);
                AppendNumber(
                    builder,
                    "visualTickDelta",
                    result.VisualTickDelta);
                AppendNumber(
                    builder,
                    "fixedTickDelta",
                    result.FixedTickDelta);
                AppendUnsigned(
                    builder,
                    "inputTickDelta",
                    result.InputTickDelta);
                AppendNumber(builder, "ledgerCount", result.Ledger.Count);
                builder.Append("}\n");
            }

            WriteAtomic(
                Path.Combine(outputDirectory, "step-results.jsonl"),
                Utf8(builder.ToString()));
        }

        private void WriteTimeSettings()
        {
            var before = pauseController?.PauseBefore;
            var after = pauseController?.LastRestoreReport?.After;
            WriteAtomic(
                Path.Combine(
                    outputDirectory,
                    "time-settings-before.json"),
                SerializeTimeSettings(before));
            WriteAtomic(
                Path.Combine(
                    outputDirectory,
                    "time-settings-after.json"),
                SerializeTimeSettings(after));
        }

        private void WriteResult()
        {
            var controller = pauseController;
            var restore = controller?.LastRestoreReport;
            var distinctStepDeltas = stepResults
                .Select(
                    value => value.VisualTickDelta.ToString(
                                 CultureInfo.InvariantCulture)
                             + "/"
                             + value.FixedTickDelta.ToString(
                                 CultureInfo.InvariantCulture)
                             + "/"
                             + value.InputTickDelta.ToString(
                                 CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)
                .Count();
            var pauseMetricsPresent =
                options.Profile == StepProbeProfile.PauseStationary
                || options.Profile == StepProbeProfile.PauseMoving;
            var timeRestoreApplicable =
                options.Profile != StepProbeProfile.Baseline;
            var builder = new StringBuilder(4096);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "profile", options.ProfileId);
            AppendString(builder, "runId", options.RunId);
            AppendString(builder, "stopReason", stopReason);
            AppendString(builder, "error", error);
            AppendString(
                builder,
                "controlMode",
                controller?.Mode.ToString() ?? string.Empty);
            AppendString(
                builder,
                "controlAbortReason",
                controller?.LastAbortReason ?? string.Empty);
            AppendString(
                builder,
                "playbackStopReason",
                playbackStopReason?.ToString() ?? string.Empty);
            AppendNumber(builder, "observationCount", observationCount);
            AppendNumber(builder, "mismatchCount", mismatchCount);
            AppendNumber(builder, "stepCount", stepResults.Count);
            AppendNumber(
                builder,
                "distinctStepDeltaTupleCount",
                distinctStepDeltas);
            AppendString(builder, "namingVerdict", namingVerdict);
            AppendString(
                builder,
                "verificationProjection",
                VerificationSnapshotNormalizer.ProjectionId);
            AppendString(builder, "endpointVerificationSha256", endpointHash);
            AppendBoolean(
                builder,
                "pauseMetricsPresent",
                pauseMetricsPresent);
            AppendNumber(
                builder,
                "pauseMovieBefore",
                pauseMetricsPresent ? pauseMovieBefore : 0);
            AppendNumber(
                builder,
                "pauseMovieAfter",
                pauseMetricsPresent ? pauseMovieAfter : 0);
            AppendNumber(
                builder,
                "pauseVisualDelta",
                pauseMetricsPresent
                    ? pauseVisualAfter - pauseVisualBefore
                    : 0);
            AppendNumber(
                builder,
                "pauseFixedDelta",
                pauseMetricsPresent
                    ? pauseFixedAfter - pauseFixedBefore
                    : 0);
            AppendUnsigned(
                builder,
                "pauseInputDelta",
                pauseMetricsPresent
                && pauseInputAfter >= pauseInputBefore
                    ? pauseInputAfter - pauseInputBefore
                    : 0);
            AppendFloat(
                builder,
                "pauseDurationSeconds",
                pauseDurationSeconds);
            AppendString(
                builder,
                "pauseBeforeVerificationSha256",
                pauseBeforeHash);
            AppendString(
                builder,
                "pauseAfterVerificationSha256",
                pauseAfterHash);
            AppendBoolean(builder, "pauseHashStable", pauseHashStable);
            AppendBoolean(builder, "pauseCursorStable", pauseCursorStable);
            AppendBoolean(builder, "pauseFixedStable", pauseFixedStable);
            AppendBoolean(
                builder,
                "timeRestoreApplicable",
                timeRestoreApplicable);
            AppendBoolean(
                builder,
                "timeRestoreAttempted",
                restore?.Attempted == true);
            AppendBoolean(
                builder,
                "timeRestoreEquivalent",
                restore?.Equivalent == true);
            AppendBoolean(
                builder,
                "bindingRestoreEquivalent",
                bindingRestore?.Equivalent == true);
            AppendBoolean(
                builder,
                "ledgerValid",
                ledgerValidation?.IsValid == true);
            AppendString(
                builder,
                "ledgerValidationError",
                ledgerValidation?.Error.ToString() ?? string.Empty);
            AppendNumber(
                builder,
                "ledgerRecordCount",
                ledgerValidation?.RecordCount ?? 0);
            AppendNumber(
                builder,
                "suppressedHealthManagerCount",
                suppressedHealthManagerCount);
            AppendBoolean(builder, "runPass", runPass);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                Utf8(builder.ToString()));
        }

        private void WriteVerdict()
        {
            WriteAtomic(
                Path.Combine(outputDirectory, "verdict.md"),
                Utf8(
                    "# T08 Pause / Controlled Step Verdict\n\n"
                    + "- Profile: `" + options.ProfileId + "`\n"
                    + "- Playback stop: `"
                    + (playbackStopReason?.ToString() ?? stopReason)
                    + "`\n"
                    + "- Pause duration: "
                    + pauseDurationSeconds.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + " seconds\n"
                    + "- Pause cursor/hash/fixed stable: "
                    + pauseCursorStable
                    + " / "
                    + pauseHashStable
                    + " / "
                    + pauseFixedStable
                    + "\n"
                    + "- Completed steps: "
                    + stepResults.Count.ToString(CultureInfo.InvariantCulture)
                    + "\n"
                    + "- Naming verdict: `" + namingVerdict + "`\n"
                    + "- Time restore equivalent: "
                    + (pauseController?.LastRestoreReport?.Equivalent == true)
                    + "\n"
                    + "- Binding restore equivalent: "
                    + (bindingRestore?.Equivalent == true)
                    + "\n"
                    + "- Run pass: " + runPass + "\n"));
        }

        private static byte[] SerializeTimeSettings(
            TimeSettingsSnapshot? value)
        {
            var builder = new StringBuilder(256);
            builder.Append('{');
            if (value == null)
            {
                AppendBoolean(builder, "present", false);
            }
            else
            {
                AppendBoolean(builder, "present", true);
                AppendNumber(
                    builder,
                    "timeScaleBits",
                    value.TimeScaleBits);
                AppendNumber(
                    builder,
                    "fixedDeltaTimeBits",
                    value.FixedDeltaTimeBits);
                AppendNumber(
                    builder,
                    "targetFrameRate",
                    value.TargetFrameRate);
                AppendNumber(builder, "vSyncCount", value.VSyncCount);
            }

            builder.Append('}');
            return Utf8(builder.ToString());
        }

        private static string CalculateNamingVerdict(
            IReadOnlyList<StepResult> results)
        {
            if (results.Count == 0
                || results.Any(value => value.MovieTickDelta != 1))
            {
                return "DIAGNOSTIC_ONLY";
            }

            var tuples = results
                .Select(
                    value => new
                    {
                        value.VisualTickDelta,
                        value.FixedTickDelta,
                        value.InputTickDelta
                    })
                .Distinct()
                .Count();
            return tuples == 1
                ? "TICK_STEP"
                : "CONTROLLED_STEP";
        }

        private static byte[] Utf8(string value)
        {
            return new UTF8Encoding(false, true).GetBytes(value);
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

        private static void AppendUnsigned(
            StringBuilder builder,
            string name,
            ulong value)
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

        private static void AppendFloat(
            StringBuilder builder,
            string name,
            float value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendPropertyPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }

        private static void WriteAtomic(
            string destinationPath,
            byte[] bytes)
        {
            var directory = Path.GetDirectoryName(destinationPath)
                            ?? throw new InvalidOperationException(
                                "Step evidence path has no containing directory.");
            var temporaryPath = Path.Combine(
                directory,
                ".t-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
    }

    [DefaultExecutionOrder(-31900)]
    internal sealed class RuntimeStepProbeRunner : MonoBehaviour
    {
        private RuntimeStepProbeExperiment? owner;

        internal void Initialize(RuntimeStepProbeExperiment value)
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

    internal enum StepProbeProfile
    {
        PauseStationary,
        PauseMoving,
        Baseline,
        Step,
        Resume,
        Scene,
        Focus,
        Fault,
        Shutdown
    }

    internal sealed class StepProbeOptions
    {
        private StepProbeOptions(
            StepProbeProfile profile,
            string runId,
            int autoLoadSlot,
            bool exitOnComplete)
        {
            Profile = profile;
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
            ExitOnComplete = exitOnComplete;
        }

        public StepProbeProfile Profile { get; }
        public string ProfileId => Profile switch
        {
            StepProbeProfile.PauseStationary => "PAUSE_STATIONARY",
            StepProbeProfile.PauseMoving => "PAUSE_MOVING",
            _ => Profile.ToString().ToUpperInvariant()
        };
        public string RunId { get; }
        public int AutoLoadSlot { get; }
        public bool ExitOnComplete { get; }

        public static StepProbeParseResult Parse(string[] arguments)
        {
            var profileText = ReadValue(arguments, "--hktas-step-probe=");
            if (profileText == null)
            {
                return StepProbeParseResult.NotRequested();
            }

            var normalized = profileText.Replace("_", string.Empty);
            if (!Enum.TryParse(
                    normalized,
                    true,
                    out StepProbeProfile profile))
            {
                return StepProbeParseResult.Invalid(
                    "profile must be PAUSE_STATIONARY, PAUSE_MOVING, "
                    + "BASELINE, STEP, RESUME, SCENE, FOCUS, FAULT, or "
                    + "SHUTDOWN");
            }

            var runId = ReadValue(arguments, "--hktas-step-probe-run=")
                        ?? profileText.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!IsSafeIdentifier(runId))
            {
                return StepProbeParseResult.Invalid("run id is invalid");
            }

            var slotText = ReadValue(arguments, "--hktas-step-probe-slot=")
                           ?? "2";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return StepProbeParseResult.Invalid(
                    "slot must be in [1, 4]");
            }

            return StepProbeParseResult.Valid(
                new StepProbeOptions(
                    profile,
                    runId,
                    slot,
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-step-probe-exit",
                            StringComparison.OrdinalIgnoreCase))));
        }

        private static string? ReadValue(
            string[] arguments,
            string prefix)
        {
            return arguments
                .FirstOrDefault(
                    value => value.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                ?.Substring(prefix.Length);
        }

        private static bool IsSafeIdentifier(string value)
        {
            return value.Length > 0
                   && value.Length <= 64
                   && value.All(
                       character => char.IsLetterOrDigit(character)
                                    || character == '.'
                                    || character == '-'
                                    || character == '_');
        }
    }

    internal sealed class StepProbeParseResult
    {
        private StepProbeParseResult(
            bool requested,
            StepProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public StepProbeOptions? Options { get; }
        public string? Error { get; }

        public static StepProbeParseResult NotRequested()
        {
            return new StepProbeParseResult(false, null, null);
        }

        public static StepProbeParseResult Valid(StepProbeOptions options)
        {
            return new StepProbeParseResult(true, options, null);
        }

        public static StepProbeParseResult Invalid(string error)
        {
            return new StepProbeParseResult(true, null, error);
        }
    }
}
