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
using HollowKnightTAS.Core.Recording;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Input;
using HollowKnightTAS.Runtime.Rng;
using HollowKnightTAS.Runtime.State;
using HollowKnightTAS.Runtime.Verification;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Playback
{
    public sealed class RuntimePlaybackProbeExperiment : IDisposable
    {
        private const string RouteScene = "GG_Vengefly";
        private const string SceneStopTarget = "GG_Workshop";
        private const float RouteStartX = 45.19f;
        private const float RouteStartY = 13.400625f;
        private readonly string manifestSha256;
        private readonly RuntimeReplayJournal shadowJournal;
        private readonly RuntimeRngProbe? rngProbe;
        private readonly PlaybackProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logDebug;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly string outputDirectory;
        private readonly string sessionId;
        private readonly RuntimeSnapshotCapture snapshotCapture =
            new RuntimeSnapshotCapture();
        private readonly StringBuilder eventLines = new StringBuilder(32768);
        private RuntimePlaybackProbeRunner? runner;
        private RuntimePlaybackController? controller;
        private RuntimeVerificationSession? verificationSession;
        private SnapshotCaptureResult? baselineCapture;
        private SnapshotCaptureResult? endpointCapture;
        private MovieDocument? movie;
        private BindingRestoreReport? restoreReport;
        private PlaybackStopReason? actualStopReason;
        private bool attached;
        private bool playbackStarted;
        private bool playbackStopped;
        private bool actionIssued;
        private bool stopped;
        private bool exitRequested;
        private bool exitIssued;
        private bool loadRequested;
        private bool routeSceneTransitionRequested;
        private bool runPass;
        private bool endpointMilestonePass;
        private bool physicalNoiseDetected;
        private bool routeHarnessApplied;
        private bool originalAcceptingInput;
        private Vector3 originalHeroPosition;
        private Vector2 originalHeroBodyPosition;
        private TasBindingSource? syntheticLeftNoise;
        private TasBindingSource? syntheticAttackNoise;
        private int warmupLateUpdates;
        private int settleLateUpdates;
        private int eventCount;
        private int suppressedHealthManagerCount;
        private long observationCount;
        private long mismatchCount;
        private long visualTick;
        private long fixedTick;
        private float startedAtRealtime;
        private float attachedAtRealtime;
        private string stopReason = "NotStopped";
        private string endpointMilestoneError = string.Empty;

        private RuntimePlaybackProbeExperiment(
            string sessionDirectory,
            string manifestSha256,
            RuntimeReplayJournal shadowJournal,
            RuntimeRngProbe? rngProbe,
            PlaybackProbeOptions options,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            this.manifestSha256 = manifestSha256;
            this.shadowJournal = shadowJournal;
            this.rngProbe = rngProbe;
            this.options = options;
            this.logInfo = logInfo;
            this.logDebug = logDebug;
            this.logWarning = logWarning;
            this.logError = logError;
            outputDirectory = Path.Combine(
                sessionDirectory,
                "playback",
                options.RunId);
            sessionId = new DirectoryInfo(sessionDirectory).Name;
            Directory.CreateDirectory(outputDirectory);
        }

        public static RuntimePlaybackProbeExperiment? TryStart(
            string sessionDirectory,
            string manifestSha256,
            RuntimeReplayJournal shadowJournal,
            RuntimeRngProbe? rngProbe,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            var parse = PlaybackProbeOptions.Parse(Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T06 playback probe arguments: " + parse.Error);
            }

            var experiment = new RuntimePlaybackProbeExperiment(
                sessionDirectory,
                manifestSha256,
                shadowJournal,
                rngProbe,
                parse.Options,
                logInfo,
                logDebug,
                logWarning,
                logError);
            experiment.Start();
            return experiment;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                Finish(false, "ApplicationQuit", "Application quit before completion.");
            }

            controller?.Dispose();
            controller = null;
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

                visualTick++;
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
                        Finish(false, "StartupTimeout", "Playable Hero was unavailable.");
                    }

                    return;
                }

                if (Time.realtimeSinceStartup - attachedAtRealtime > 120f)
                {
                    Finish(false, "RunTimeout", "Playback probe exceeded 120 seconds.");
                    return;
                }

                if (playbackStarted && !playbackStopped)
                {
                    if (options.Profile == PlaybackProbeProfile.Manual
                        && !actionIssued
                        && observationCount >= 20)
                    {
                        actionIssued = true;
                        controller!.Stop(PlaybackStopReason.Manual);
                    }
                    else if (options.Profile == PlaybackProbeProfile.Emergency
                             && !actionIssued
                             && observationCount >= 20)
                    {
                        actionIssued = true;
                        controller!.EmergencyStop();
                    }
                    else if (options.Profile == PlaybackProbeProfile.Scene
                             && !actionIssued
                             && observationCount >= 1)
                    {
                        actionIssued = true;
                        var manager = GameManager.instance
                                      ?? throw new InvalidOperationException(
                                          "GameManager disappeared before scene stop.");
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
                    }
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        internal void OnFixedUpdate()
        {
            fixedTick++;
        }

        internal void OnLateUpdate()
        {
            if (stopped || !attached)
            {
                return;
            }

            try
            {
                DisableVerificationHazards();
                if (options.Profile == PlaybackProbeProfile.Shadow)
                {
                    AdvanceShadowProfile();
                    return;
                }

                if (!playbackStarted)
                {
                    warmupLateUpdates++;
                    if (warmupLateUpdates >= 30)
                    {
                        StartPlayback();
                    }

                    return;
                }

                verificationSession?.CapturePending(CurrentLateStamp());

                if (!playbackStopped)
                {
                    return;
                }

                settleLateUpdates++;
                if (options.Profile == PlaybackProbeProfile.Scene)
                {
                    if (settleLateUpdates >= 3)
                    {
                        ValidateAndFinish();
                    }

                    return;
                }

                if (settleLateUpdates < 10)
                {
                    return;
                }

                if (HeroController.SilentInstance != null
                    && GameManager.instance != null
                    && GameManager.instance.gameState == GameState.PLAYING)
                {
                    endpointCapture = snapshotCapture.Capture(CurrentLateStamp());
                    if (endpointCapture.Success)
                    {
                        WriteSnapshot("endpoint.snapshot", endpointCapture);
                    }
                }

                if (endpointCapture?.Success == true
                    && verificationSession != null)
                {
                    verificationSession.Complete(
                        ExpandedMovieTicks(),
                        endpointCapture);
                }

                ValidateAndFinish();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private void Start()
        {
            startedAtRealtime = Time.realtimeSinceStartup;
            var gameObject = new GameObject("HollowKnightTAS.RuntimePlaybackProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimePlaybackProbeRunner>();
            runner.Initialize(this);
            logInfo(
                "T06 playback probe created profile="
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

            if (options.Profile != PlaybackProbeProfile.Shadow
                && !string.Equals(
                    USceneManager.GetActiveScene().name,
                    RouteScene,
                    StringComparison.Ordinal))
            {
                if (!routeSceneTransitionRequested)
                {
                    routeSceneTransitionRequested = true;
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

            attached = true;
            attachedAtRealtime = Time.realtimeSinceStartup;
            visualTick = 0;
            fixedTick = 0;
            if (options.Profile != PlaybackProbeProfile.Shadow)
            {
                var body = hero.GetComponent<Rigidbody2D>()
                           ?? throw new InvalidOperationException(
                               "Hero Rigidbody2D is unavailable.");
                originalAcceptingInput = hero.acceptingInput;
                originalHeroPosition = hero.gameObject.transform.position;
                originalHeroBodyPosition = body.position;
                hero.RelinquishControl();
                hero.RegainControl();
                body.velocity = Vector2.zero;
                routeHarnessApplied = true;
            }

            logDebug(
                "T06 playback probe attached scene="
                + USceneManager.GetActiveScene().name);
        }

        private void StartPlayback()
        {
            NormalizeRouteBaseline();
            baselineCapture = snapshotCapture.Capture(CurrentLateStamp());
            RequireCapture(baselineCapture, "route baseline");
            WriteSnapshot("baseline.snapshot", baselineCapture);
            var baselineId = "slot-"
                             + options.AutoLoadSlot.ToString(CultureInfo.InvariantCulture)
                             + "-gg-vengefly";
            var verificationProfile =
                options.Profile == PlaybackProbeProfile.Verify
                || options.Profile == PlaybackProbeProfile.Divergence;
            movie = verificationProfile
                ? CreateVerificationRouteMovie(
                    baselineId,
                    baselineCapture.Sha256!,
                    false)
                : CreateRouteMovie(
                    options.Profile == PlaybackProbeProfile.Edited ? 20 : 10,
                    45,
                    120,
                    baselineId,
                    baselineCapture.Sha256!);
            var replayMovie = options.Profile == PlaybackProbeProfile.Divergence
                ? CreateVerificationRouteMovie(
                    baselineId,
                    baselineCapture.Sha256!,
                    true)
                : movie;
            var validation = new MovieValidator().Validate(
                movie,
                new MovieValidationContext(
                    1000,
                    MovieProtocolV1.DefaultSemanticPaths,
                    manifestSha256,
                    baselineCapture.Sha256!));
            if (!validation.Success)
            {
                throw new InvalidOperationException(
                    "Generated route movie failed validation: "
                    + string.Join(
                        "; ",
                        validation.Diagnostics.Select(value => value.ToString())));
            }

            var writer = new MovieCanonicalWriter();
            WriteAtomic(
                Path.Combine(outputDirectory, "generated-route.hktas"),
                writer.WriteUtf8(movie));
            WriteAtomic(
                Path.Combine(outputDirectory, "generated-route.movie-id"),
                new UTF8Encoding(false).GetBytes(
                    writer.ComputeMovieId(movie) + "\n"));
            if (options.Profile == PlaybackProbeProfile.Divergence)
            {
                WriteAtomic(
                    Path.Combine(
                        outputDirectory,
                        "deliberate-divergence-execution.hktas"),
                    writer.WriteUtf8(replayMovie));
                WriteAtomic(
                    Path.Combine(
                        outputDirectory,
                        "deliberate-divergence.json"),
                    new UTF8Encoding(false).GetBytes(
                        "{\"schemaVersion\":1,"
                        + "\"declaredMovieId\":\""
                        + writer.ComputeMovieId(movie)
                        + "\",\"injectedMovieTick\":490,"
                        + "\"expectedHeld\":\"None\","
                        + "\"injectedHeld\":\"Attack\"}"));
            }

            if (options.Profile == PlaybackProbeProfile.Verify
                || options.Profile == PlaybackProbeProfile.Divergence)
            {
                verificationSession = new RuntimeVerificationSession(
                    Path.Combine(outputDirectory, "verification"),
                    sessionId,
                    manifestSha256,
                    rngProbe?.StateAvailable == true
                        ? () => rngProbe.CaptureCurrent().Sha256
                        : (Func<string>?)null);
                verificationSession.Begin(movie, baselineCapture);
            }

            controller = new RuntimePlaybackController(
                OnObservation,
                OnPlaybackEvent,
                OnPlaybackStopped);
            if (options.Profile == PlaybackProbeProfile.Physical)
            {
                AddSyntheticPhysicalNoise();
            }

            if (options.Profile == PlaybackProbeProfile.Fault)
            {
                controller.FaultInjectionMovieTick = 20;
            }

            var start = controller.StartReplay(
                replayMovie,
                new PlaybackContext(
                    manifestSha256,
                    baselineId,
                    baselineCapture.Sha256!,
                    controller.SceneEpoch));
            if (!start.Success)
            {
                throw new InvalidOperationException(
                    "Runtime playback start failed: " + start.Error);
            }

            playbackStarted = true;
            WriteBindingBefore(controller.Replayer!.BindingBefore);
            WriteAtomic(
                Path.Combine(outputDirectory, "probe.ready"),
                new UTF8Encoding(false).GetBytes(
                    options.ProfileId + " " + options.RunId + "\n"));
        }

        private void AdvanceShadowProfile()
        {
            warmupLateUpdates++;
            if (warmupLateUpdates < 60
                || shadowJournal.LastCommittedMovieTick < 30)
            {
                return;
            }

            shadowJournal.Flush();
            var pass = shadowJournal.IsAvailable
                       && !shadowJournal.HasGap
                       && shadowJournal.LastCommittedMovieTick >= 30
                       && shadowJournal.LastPersistedMovieTick
                       == shadowJournal.LastCommittedMovieTick
                       && !string.IsNullOrWhiteSpace(shadowJournal.BaselineId)
                       && !string.IsNullOrWhiteSpace(shadowJournal.BaselineSha256);
            if (!pass)
            {
                throw new InvalidOperationException(
                    "Shadow journal did not produce a complete persisted prefix.");
            }

            runPass = true;
            Finish(true, "Completed", string.Empty);
        }

        private void OnObservation(ReplayInputObservation observation)
        {
            observationCount++;
            if (!observation.Matches)
            {
                mismatchCount++;
            }

            eventLines.Append('{');
            AppendString(eventLines, "event", "input");
            AppendNumber(eventLines, "movieTick", observation.MovieTick);
            AppendNumber(
                eventLines,
                "rawInputTick",
                unchecked((long)observation.RawInputTick));
            AppendString(eventLines, "expectedHeld", observation.Expected.Held.ToString());
            AppendString(eventLines, "actualHeld", observation.Actual.Held.ToString());
            AppendString(eventLines, "expectedPressed", observation.Expected.Pressed.ToString());
            AppendString(eventLines, "actualPressed", observation.Actual.Pressed.ToString());
            AppendString(eventLines, "expectedReleased", observation.Expected.Released.ToString());
            AppendString(eventLines, "actualReleased", observation.Actual.Released.ToString());
            AppendNumber(eventLines, "expectedAxisX", observation.Expected.AxisX);
            AppendNumber(eventLines, "actualAxisX", observation.Actual.AxisX);
            AppendNumber(eventLines, "expectedAxisY", observation.Expected.AxisY);
            AppendNumber(eventLines, "actualAxisY", observation.Actual.AxisY);
            AppendBoolean(eventLines, "matches", observation.Matches);
            AppendBoolean(
                eventLines,
                "controlSignalMatches",
                observation.ControlSignalMatches);
            AppendBoolean(
                eventLines,
                "committedEdgesMatch",
                observation.CommittedEdgesMatch);
            AppendBoolean(eventLines, "physicalNoise", observation.PhysicalNoise);
            eventLines.Append("}\n");
            verificationSession?.ObserveInput(
                observation,
                new TickStamp(
                    observation.RawInputTick,
                    visualTick,
                    fixedTick,
                    controller?.SceneEpoch ?? 0,
                    TickPhase.InControlCommitted));
        }

        private void OnPlaybackEvent(PlaybackEvent playbackEvent)
        {
            eventCount++;
            verificationSession?.ObserveEvent(playbackEvent);
            eventLines.Append('{');
            AppendString(eventLines, "event", playbackEvent.Kind.ToString());
            AppendNumber(eventLines, "movieTick", playbackEvent.MovieTick);
            AppendNumber(eventLines, "commandIndex", playbackEvent.CommandIndex);
            eventLines.Append("}\n");
        }

        private void OnPlaybackStopped(
            PlaybackStopReason reason,
            BindingRestoreReport report)
        {
            actualStopReason = reason;
            restoreReport = report;
            physicalNoiseDetected =
                controller?.Replayer?.PhysicalNoiseDetected == true;
            RemoveSyntheticPhysicalNoise();
            playbackStopped = true;
            WriteBindingRestore(report);
            WriteAtomic(
                Path.Combine(outputDirectory, "playback-events.jsonl"),
                new UTF8Encoding(false).GetBytes(eventLines.ToString()));
        }

        private void ValidateAndFinish()
        {
            if (!playbackStopped || restoreReport == null || actualStopReason == null)
            {
                throw new InvalidOperationException(
                    "Playback did not produce a stop and restore report.");
            }

            var expectedReason = ExpectedStopReason(options.Profile);
            var expectedObservations = options.Profile switch
            {
                PlaybackProbeProfile.Original => 176,
                PlaybackProbeProfile.Edited => 186,
                PlaybackProbeProfile.Physical => 176,
                PlaybackProbeProfile.Verify => 612,
                PlaybackProbeProfile.Divergence => 612,
                _ => -1
            };
            var completeCountPass = expectedObservations < 0
                                    || observationCount == expectedObservations;
            var physicalPass = options.Profile != PlaybackProbeProfile.Physical
                               || physicalNoiseDetected;
            var endpointRequired = options.Profile == PlaybackProbeProfile.Original
                                   || options.Profile == PlaybackProbeProfile.Edited
                                   || options.Profile == PlaybackProbeProfile.Physical
                                   || options.Profile == PlaybackProbeProfile.Verify
                                   || options.Profile == PlaybackProbeProfile.Divergence;
            endpointMilestonePass = !endpointRequired
                                    || EvaluateEndpointMilestone(
                                        out endpointMilestoneError);
            runPass = actualStopReason == expectedReason
                      && restoreReport.Attempted
                      && restoreReport.Equivalent
                      && mismatchCount == 0
                      && completeCountPass
                      && physicalPass
                      && endpointMilestonePass
                      && shadowJournal.IsAvailable
                      && !shadowJournal.HasGap;
            if (!runPass)
            {
                throw new InvalidOperationException(
                    "Playback acceptance conditions were not met.");
            }

            shadowJournal.Flush();
            Finish(true, "Completed", string.Empty);
        }

        private void Fail(Exception exception)
        {
            logError("T06 playback probe failed: " + exception);
            Finish(false, exception.GetType().Name, exception.Message);
        }

        private void Finish(bool pass, string reason, string error)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            runPass = runPass && pass;
            stopReason = reason;
            try
            {
                controller?.Dispose();
                RemoveSyntheticPhysicalNoise();
                RestoreRouteHarness();
                WriteResult(error);
            }
            catch (Exception exception)
            {
                runPass = false;
                logError("T06 playback probe cleanup failed: " + exception);
            }

            logInfo(
                "T06 playback probe stopped profile="
                + options.ProfileId
                + " pass="
                + runPass
                + " reason="
                + stopReason);
            if (options.ExitOnComplete)
            {
                exitRequested = true;
            }
        }

        private void AddSyntheticPhysicalNoise()
        {
            var actions = InputHandler.Instance?.inputActions
                          ?? throw new InvalidOperationException(
                              "HeroActions disappeared before physical-noise setup.");
            syntheticLeftNoise = new TasBindingSource(TasAction.Left);
            syntheticAttackNoise = new TasBindingSource(TasAction.Attack);
            syntheticLeftNoise.SetValue(1f);
            syntheticAttackNoise.SetValue(1f);
            if (!actions.left.AddBinding(syntheticLeftNoise)
                || !actions.attack.AddBinding(syntheticAttackNoise))
            {
                RemoveSyntheticPhysicalNoise();
                throw new InvalidOperationException(
                    "Could not install deterministic physical-noise bindings.");
            }
        }

        private void RemoveSyntheticPhysicalNoise()
        {
            var actions = InputHandler.Instance?.inputActions;
            if (actions != null)
            {
                if (syntheticLeftNoise != null)
                {
                    actions.left.RemoveBinding(syntheticLeftNoise);
                }

                if (syntheticAttackNoise != null)
                {
                    actions.attack.RemoveBinding(syntheticAttackNoise);
                }
            }

            syntheticLeftNoise = null;
            syntheticAttackNoise = null;
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

        private MovieDocument CreateRouteMovie(
            int initialNeutralTicks,
            int movementTicks,
            int finalNeutralTicks,
            string baselineId,
            string baselineHash)
        {
            var samples = new List<InputSample>();
            var previous = TasAction.None;
            ulong tick = 0;
            AddSamples(initialNeutralTicks, TasAction.None);
            AddSamples(movementTicks, TasAction.Left);
            AddSamples(finalNeutralTicks, TasAction.None);

            var recorder = new InputRecorder();
            foreach (var sample in samples)
            {
                if (!recorder.Append(sample))
                {
                    throw new InvalidOperationException(
                        "Generated route recorder developed a gap.");
                }
            }

            var header = new MovieHeader(
                1,
                "1.5.78.11833",
                "1.5.78.11833-77",
                manifestSha256,
                baselineId,
                baselineHash,
                "input");
            var recorded = recorder.CreateMovie(header, "generated-route.hktas");
            var runs = recorded.Commands.OfType<FrameRunCommand>().ToArray();
            if (runs.Length != 3)
            {
                throw new InvalidOperationException(
                    "Generated route must canonicalize to exactly three runs.");
            }

            var span = new MovieSourceSpan("generated-route.hktas", 1, 1, 1);
            return new MovieDocument(
                "generated-route.hktas",
                header,
                new MovieCommand[]
                {
                    new MarkerCommand("p0 start", span),
                    runs[0],
                    new CheckpointCommand("movement-start", span),
                    runs[1],
                    new CheckpointCommand("movement-end", span),
                    runs[2],
                    new AssertCommand(
                        "scene.name",
                        "==",
                        RouteScene,
                        true,
                        span)
                });

            void AddSamples(int count, TasAction held)
            {
                for (var index = 0; index < count; index++)
                {
                    samples.Add(
                        InputSample.FromHeld(
                            tick,
                            held,
                            previous));
                    previous = held;
                    tick++;
                }
            }
        }

        private MovieDocument CreateVerificationRouteMovie(
            string baselineId,
            string baselineHash,
            bool injectDivergence)
        {
            const string sourceName = "verification-route.hktas";
            var span = new MovieSourceSpan(sourceName, 1, 1, 1);
            var header = new MovieHeader(
                1,
                "1.5.78.11833",
                "1.5.78.11833-77",
                manifestSha256,
                baselineId,
                baselineHash,
                "input");

            // Keep the verification route at a normalized grounded idle. T03
            // established that rendered input ticks do not have a fixed ratio
            // to physics ticks; using movement here would make the route itself
            // a scheduler probe. Divergence changes only the execution input
            // at movie tick 490, while the declared movie identity stays fixed.
            return new MovieDocument(
                sourceName,
                header,
                new MovieCommand[]
                {
                    new MarkerCommand("verification start", span),
                    new FrameRunCommand(
                        490,
                        TasAction.None,
                        0,
                        0,
                        false,
                        span),
                    new CheckpointCommand("pre-divergence", span),
                    new FrameRunCommand(
                        1,
                        injectDivergence
                            ? TasAction.Attack
                            : TasAction.None,
                        0,
                        0,
                        false,
                        span),
                    new CheckpointCommand("divergence-probe", span),
                    new FrameRunCommand(
                        120,
                        TasAction.None,
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

        private TickStamp CurrentLateStamp()
        {
            return new TickStamp(
                0,
                visualTick,
                fixedTick,
                controller?.SceneEpoch ?? 0,
                TickPhase.LateUpdateEnd);
        }

        private static void RequireCapture(
            SnapshotCaptureResult capture,
            string operation)
        {
            if (!capture.Success)
            {
                throw new InvalidOperationException(
                    operation
                    + " capture failed at "
                    + capture.FailedProbeId
                    + ": "
                    + capture.Error);
            }
        }

        private static PlaybackStopReason ExpectedStopReason(
            PlaybackProbeProfile profile)
        {
            switch (profile)
            {
                case PlaybackProbeProfile.Original:
                case PlaybackProbeProfile.Edited:
                case PlaybackProbeProfile.Physical:
                case PlaybackProbeProfile.Verify:
                case PlaybackProbeProfile.Divergence:
                    return PlaybackStopReason.Completed;
                case PlaybackProbeProfile.Manual:
                    return PlaybackStopReason.Manual;
                case PlaybackProbeProfile.Emergency:
                    return PlaybackStopReason.Emergency;
                case PlaybackProbeProfile.Scene:
                    return PlaybackStopReason.SceneChanged;
                case PlaybackProbeProfile.Fault:
                    return PlaybackStopReason.AdapterFault;
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }

        private long ExpandedMovieTicks()
        {
            if (movie == null)
            {
                throw new InvalidOperationException(
                    "Movie is unavailable while completing verification.");
            }

            return movie.Commands
                .OfType<FrameRunCommand>()
                .Sum(value => value.FrameCount);
        }

        private void WriteSnapshot(string name, SnapshotCaptureResult result)
        {
            WriteAtomic(
                Path.Combine(outputDirectory, name),
                result.CanonicalBytes!);
            WriteAtomic(
                Path.Combine(outputDirectory, name + ".sha256"),
                new UTF8Encoding(false).GetBytes(result.Sha256 + "\n"));
        }

        private void WriteBindingBefore(
            IReadOnlyList<BindingActionSnapshot> snapshots)
        {
            WriteAtomic(
                Path.Combine(outputDirectory, "binding-before.json"),
                SerializeBindings(snapshots, null));
        }

        private void WriteBindingRestore(BindingRestoreReport report)
        {
            WriteAtomic(
                Path.Combine(outputDirectory, "binding-restore.json"),
                SerializeBindings(report.After, report));
        }

        private static byte[] SerializeBindings(
            IReadOnlyList<BindingActionSnapshot> snapshots,
            BindingRestoreReport? report)
        {
            var builder = new StringBuilder(4096);
            builder.Append('{');
            if (report != null)
            {
                AppendBoolean(builder, "attempted", report.Attempted);
                AppendBoolean(builder, "equivalent", report.Equivalent);
                AppendString(builder, "message", report.Message);
            }

            AppendPropertyPrefix(builder, "actions");
            builder.Append('[');
            for (var actionIndex = 0; actionIndex < snapshots.Count; actionIndex++)
            {
                if (actionIndex > 0)
                {
                    builder.Append(',');
                }

                var action = snapshots[actionIndex];
                builder.Append('{');
                AppendString(builder, "action", action.Action.ToString());
                AppendString(builder, "actionName", action.ActionName);
                AppendPropertyPrefix(builder, "bindings");
                builder.Append('[');
                for (var bindingIndex = 0;
                     bindingIndex < action.Bindings.Count;
                     bindingIndex++)
                {
                    if (bindingIndex > 0)
                    {
                        builder.Append(',');
                    }

                    var binding = action.Bindings[bindingIndex];
                    builder.Append('{');
                    AppendNumber(builder, "index", binding.Index);
                    AppendString(builder, "name", binding.Name);
                    AppendString(builder, "sourceType", binding.SourceType);
                    builder.Append('}');
                }

                builder.Append(']');
                builder.Append('}');
            }

            builder.Append(']');
            builder.Append('}');
            return new UTF8Encoding(false).GetBytes(builder.ToString());
        }

        private void WriteResult(string error)
        {
            var builder = new StringBuilder(2048);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "profile", options.ProfileId);
            AppendString(builder, "runId", options.RunId);
            AppendString(builder, "stopReason", stopReason);
            AppendString(builder, "error", error ?? string.Empty);
            AppendString(
                builder,
                "playbackStopReason",
                actualStopReason?.ToString() ?? string.Empty);
            AppendNumber(builder, "observationCount", observationCount);
            AppendNumber(builder, "mismatchCount", mismatchCount);
            AppendNumber(builder, "eventCount", eventCount);
            AppendBoolean(
                builder,
                "physicalNoiseDetected",
                physicalNoiseDetected);
            AppendBoolean(
                builder,
                "bindingRestoreEquivalent",
                restoreReport?.Equivalent == true);
            AppendString(
                builder,
                "baselineSha256",
                baselineCapture?.Sha256 ?? string.Empty);
            AppendString(
                builder,
                "endpointSha256",
                endpointCapture?.Sha256 ?? string.Empty);
            AppendBoolean(
                builder,
                "endpointMilestonePass",
                endpointMilestonePass);
            AppendString(
                builder,
                "endpointMilestoneError",
                endpointMilestoneError);
            AppendFloatFromSnapshot(
                builder,
                "baselineX",
                baselineCapture,
                "hero.position.x");
            AppendFloatFromSnapshot(
                builder,
                "baselineY",
                baselineCapture,
                "hero.position.y");
            AppendFloatFromSnapshot(
                builder,
                "endpointX",
                endpointCapture,
                "hero.position.x");
            AppendFloatFromSnapshot(
                builder,
                "endpointY",
                endpointCapture,
                "hero.position.y");
            AppendString(
                builder,
                "movieId",
                movie == null
                    ? string.Empty
                    : new MovieCanonicalWriter().ComputeMovieId(movie));
            AppendString(
                builder,
                "verificationRunSignature",
                verificationSession?.RunSignature ?? string.Empty);
            AppendString(
                builder,
                "verificationRunJson",
                verificationSession?.RunJsonPath ?? string.Empty);
            AppendNumber(
                builder,
                "suppressedHealthManagerCount",
                suppressedHealthManagerCount);
            AppendString(
                builder,
                "shadowBaselineId",
                shadowJournal.BaselineId ?? string.Empty);
            AppendString(
                builder,
                "shadowBaselineSha256",
                shadowJournal.BaselineSha256 ?? string.Empty);
            AppendString(
                builder,
                "shadowDirectory",
                shadowJournal.CurrentDirectory ?? string.Empty);
            AppendNumber(
                builder,
                "shadowLastCommittedMovieTick",
                shadowJournal.LastCommittedMovieTick);
            AppendNumber(
                builder,
                "shadowLastPersistedMovieTick",
                shadowJournal.LastPersistedMovieTick);
            AppendBoolean(builder, "shadowHasGap", shadowJournal.HasGap);
            AppendBoolean(builder, "runPass", runPass);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
            WriteAtomic(
                Path.Combine(outputDirectory, "verdict.md"),
                new UTF8Encoding(false).GetBytes(
                    "# T06 Playback Probe Verdict\n\n"
                    + "- Profile: `" + options.ProfileId + "`\n"
                    + "- Stop: `" + (actualStopReason?.ToString() ?? stopReason) + "`\n"
                    + "- Observations: " + observationCount.ToString(CultureInfo.InvariantCulture) + "\n"
                    + "- Mismatches: " + mismatchCount.ToString(CultureInfo.InvariantCulture) + "\n"
                    + "- Binding restore: " + (restoreReport?.Equivalent == true) + "\n"
                    + "- Endpoint milestone: " + endpointMilestonePass + "\n"
                    + "- Shadow gap: " + shadowJournal.HasGap + "\n"
                    + "- Run pass: " + runPass + "\n"));
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

        private void NormalizeRouteBaseline()
        {
            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "Hero disappeared before route baseline normalization.");
            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "Hero Rigidbody2D disappeared before route baseline normalization.");
            body.velocity = Vector2.zero;
            body.position = new Vector2(RouteStartX, RouteStartY);
            hero.gameObject.transform.position =
                new Vector3(RouteStartX, RouteStartY, hero.gameObject.transform.position.z);
            Physics2D.SyncTransforms();
        }

        private bool EvaluateEndpointMilestone(out string error)
        {
            if (baselineCapture?.Snapshot == null
                || endpointCapture?.Snapshot == null)
            {
                error = "Baseline or endpoint snapshot is unavailable.";
                return false;
            }

            var baseline = baselineCapture.Snapshot;
            var endpoint = endpointCapture.Snapshot;
            var baselineX = ReadFloat(baseline, "hero.position.x");
            var baselineY = ReadFloat(baseline, "hero.position.y");
            var endpointX = ReadFloat(endpoint, "hero.position.x");
            var endpointY = ReadFloat(endpoint, "hero.position.y");
            var endpointVelocityX = ReadFloat(endpoint, "hero.velocity.x");
            var endpointVelocityY = ReadFloat(endpoint, "hero.velocity.y");
            var displacement = baselineX - endpointX;
            var verificationProfile =
                options.Profile == PlaybackProbeProfile.Verify
                || options.Profile == PlaybackProbeProfile.Divergence;
            var movementPass = verificationProfile
                ? Math.Abs(endpointX - baselineX) <= 0.01f
                : displacement >= 1f && displacement <= 1.7f;
            var pass = string.Equals(
                           ReadDisplay(endpoint, "scene.name"),
                           RouteScene,
                           StringComparison.Ordinal)
                       && string.Equals(
                           ReadDisplay(endpoint, "hero.actorState"),
                           "idle",
                           StringComparison.Ordinal)
                       && ReadBoolean(endpoint, "hero.cState.onGround")
                       && !ReadBoolean(endpoint, "hero.cState.jumping")
                       && !ReadBoolean(endpoint, "hero.cState.falling")
                       && !ReadBoolean(endpoint, "hero.cState.dashing")
                       && !ReadBoolean(endpoint, "hero.cState.attacking")
                       && movementPass
                       && Math.Abs(endpointY - baselineY) <= 0.01f
                       && Math.Abs(endpointVelocityX) <= 0.001f
                       && Math.Abs(endpointVelocityY) <= 0.001f
                       && string.Equals(
                           ReadDisplay(endpoint, "player.health"),
                           ReadDisplay(baseline, "player.health"),
                           StringComparison.Ordinal);
            error = pass
                ? string.Empty
                : verificationProfile
                    ? "Expected a grounded idle endpoint at the normalized "
                      + "verification position in "
                      + RouteScene
                      + "."
                    : "Expected a grounded idle endpoint in "
                      + RouteScene
                      + " after 1..1.7 units of leftward movement.";
            return pass;
        }

        private void DisableVerificationHazards()
        {
            if (options.Profile != PlaybackProbeProfile.Verify
                && options.Profile != PlaybackProbeProfile.Divergence)
            {
                return;
            }

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

        private static string ReadDisplay(
            SemanticSnapshot snapshot,
            string key)
        {
            if (!snapshot.Values.TryGetValue(key, out var value))
            {
                throw new InvalidOperationException(
                    "Endpoint milestone key is missing: " + key);
            }

            return value.DisplayValue;
        }

        private static float ReadFloat(
            SemanticSnapshot snapshot,
            string key)
        {
            if (!float.TryParse(
                    ReadDisplay(snapshot, key),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                throw new InvalidOperationException(
                    "Endpoint milestone float is invalid: " + key);
            }

            return value;
        }

        private static bool ReadBoolean(
            SemanticSnapshot snapshot,
            string key)
        {
            if (!bool.TryParse(ReadDisplay(snapshot, key), out var value))
            {
                throw new InvalidOperationException(
                    "Endpoint milestone boolean is invalid: " + key);
            }

            return value;
        }

        private static void AppendFloatFromSnapshot(
            StringBuilder builder,
            string propertyName,
            SnapshotCaptureResult? capture,
            string semanticKey)
        {
            AppendPropertyPrefix(builder, propertyName);
            if (capture?.Snapshot == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append(
                ReadFloat(capture.Snapshot, semanticKey)
                    .ToString("R", CultureInfo.InvariantCulture));
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
            var directory = Path.GetDirectoryName(destinationPath)
                            ?? throw new InvalidOperationException(
                                "Playback evidence path has no containing directory.");
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

    [DefaultExecutionOrder(-32000)]
    internal sealed class RuntimePlaybackProbeRunner : MonoBehaviour
    {
        private RuntimePlaybackProbeExperiment? owner;

        internal void Initialize(RuntimePlaybackProbeExperiment value)
        {
            owner = value;
        }

        private void Update()
        {
            owner?.OnUpdate();
        }

        private void FixedUpdate()
        {
            owner?.OnFixedUpdate();
        }

        private void LateUpdate()
        {
            owner?.OnLateUpdate();
        }
    }

    internal enum PlaybackProbeProfile
    {
        Original,
        Edited,
        Physical,
        Manual,
        Emergency,
        Scene,
        Fault,
        Shadow,
        Verify,
        Divergence
    }

    internal sealed class PlaybackProbeOptions
    {
        private PlaybackProbeOptions(
            PlaybackProbeProfile profile,
            string runId,
            int autoLoadSlot,
            bool exitOnComplete)
        {
            Profile = profile;
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
            ExitOnComplete = exitOnComplete;
        }

        public PlaybackProbeProfile Profile { get; }
        public string ProfileId => Profile.ToString().ToUpperInvariant();
        public string RunId { get; }
        public int AutoLoadSlot { get; }
        public bool ExitOnComplete { get; }

        public static PlaybackProbeParseResult Parse(string[] arguments)
        {
            var profileText = ReadValue(arguments, "--hktas-playback-probe=");
            if (profileText == null)
            {
                return PlaybackProbeParseResult.NotRequested();
            }

            if (!Enum.TryParse(
                    profileText,
                    true,
                    out PlaybackProbeProfile profile))
            {
                return PlaybackProbeParseResult.Invalid(
                    "profile must be ORIGINAL, EDITED, PHYSICAL, MANUAL, "
                    + "EMERGENCY, SCENE, FAULT, SHADOW, VERIFY, or DIVERGENCE");
            }

            var runId = ReadValue(arguments, "--hktas-playback-probe-run=")
                        ?? profileText.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!IsSafeIdentifier(runId))
            {
                return PlaybackProbeParseResult.Invalid("run id is invalid");
            }

            var slotText = ReadValue(arguments, "--hktas-playback-probe-slot=") ?? "2";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return PlaybackProbeParseResult.Invalid("slot must be in [1, 4]");
            }

            return PlaybackProbeParseResult.Valid(
                new PlaybackProbeOptions(
                    profile,
                    runId,
                    slot,
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-playback-probe-exit",
                            StringComparison.OrdinalIgnoreCase))));
        }

        private static string? ReadValue(string[] arguments, string prefix)
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

    internal sealed class PlaybackProbeParseResult
    {
        private PlaybackProbeParseResult(
            bool requested,
            PlaybackProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public PlaybackProbeOptions? Options { get; }
        public string? Error { get; }

        public static PlaybackProbeParseResult NotRequested()
        {
            return new PlaybackProbeParseResult(false, null, null);
        }

        public static PlaybackProbeParseResult Valid(PlaybackProbeOptions options)
        {
            return new PlaybackProbeParseResult(true, options, null);
        }

        public static PlaybackProbeParseResult Invalid(string error)
        {
            return new PlaybackProbeParseResult(true, null, error);
        }
    }
}
