using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Playback;
using InControl;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityGarbageCollector =
    UnityEngine.Scripting.GarbageCollector;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class RuntimeInspectorProbeExperiment : IDisposable
    {
        private const string RouteScene = "GG_Vengefly";
        private const string LifecycleScene = "GG_Workshop";
        private const int WarmupSeconds = 10;
        private const int BaselineSeconds = 30;
        private const int AllocationWindowSeconds = 10;

        private readonly RuntimeInspector inspector;
        private readonly RuntimeReplayJournal journal;
        private readonly InspectorProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logError;
        private readonly string outputDirectory;
        private readonly string screenshotPath;
        private readonly List<long> baselineAllocations =
            new List<long>(4096);
        private readonly List<long> enabledAllocations =
            new List<long>(65536);
        private readonly List<string> stableKeys =
            new List<string>();
        private readonly List<string> displayKeys =
            new List<string>();
        private RuntimeInspectorProbeRunner? runner;
        private UnityGarbageCollector.Mode originalGcMode;
        private FunctionalPhase functionalPhase;
        private PerformancePhase performancePhase;
        private long phaseStartMovieTick;
        private long observedFrameSequence = -1;
        private long pausedWrittenCount;
        private long disabledSampleCount;
        private long disabledRenderCount;
        private long overlayRenderBaseline;
        private long parityFrameSequence = -1;
        private int routeSceneEpoch;
        private bool loadRequested;
        private bool routeSceneRequested;
        private bool routeStarted;
        private bool lifecycleSceneRequested;
        private bool sceneClearObserved;
        private bool sceneRebound;
        private bool directParity;
        private bool parityFrameValid;
        private bool overlayGroupChanged;
        private bool overlayRendered;
        private bool colliderRendered;
        private bool exportSameSource;
        private bool exportPauseHeld;
        private bool exportResumed;
        private bool disableStoppedSampling;
        private bool disableStoppedRendering;
        private bool screenshotRequested;
        private bool screenshotWritten;
        private bool gcModeCaptured;
        private bool allocationWindowActive;
        private bool enabledAllocationWindowStarted;
        private bool stopped;
        private bool exitIssued;
        private bool runPass;
        private float startedAtRealtime;
        private float performancePhaseStartedAt;
        private string stopReason = "NotStopped";
        private string error = string.Empty;
        private RuntimeInspectorStatistics? finalStatistics;
        private double baselineAllocationAverage;
        private double baselineAllocationP95;
        private double enabledAllocationAverage;
        private double enabledAllocationP95;
        private double attributableAllocationAverage;
        private double attributableAllocationP95;
        private long previousAllocationHeapBytes;

        private RuntimeInspectorProbeExperiment(
            string sessionDirectory,
            RuntimeInspector inspector,
            RuntimeReplayJournal journal,
            InspectorProbeOptions options,
            Action<string> logInfo,
            Action<string> logError)
        {
            this.inspector = inspector;
            this.journal = journal;
            this.options = options;
            this.logInfo = logInfo;
            this.logError = logError;
            outputDirectory = Path.Combine(
                Path.GetFullPath(sessionDirectory),
                "inspector",
                "probe",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
            var screenshotDirectory = Path.Combine(
                outputDirectory,
                "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            screenshotPath = Path.Combine(
                screenshotDirectory,
                "overlay.png");
        }

        public static RuntimeInspectorProbeExperiment? TryStart(
            string sessionDirectory,
            RuntimeInspector? inspector,
            RuntimeReplayJournal journal,
            Action<string> logInfo,
            Action<string> logError)
        {
            var parse = InspectorProbeOptions.Parse(
                Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T11 Inspector probe arguments: "
                    + parse.Error);
            }

            if (inspector == null)
            {
                throw new InvalidOperationException(
                    "T11 Inspector probe requested but Inspector "
                    + "did not start.");
            }

            var experiment = new RuntimeInspectorProbeExperiment(
                sessionDirectory,
                inspector,
                journal,
                parse.Options,
                logInfo,
                logError);
            experiment.Start();
            return experiment;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                Finish(
                    false,
                    "ApplicationQuit",
                    "Application quit before T11 completion.");
            }

            USceneManager.activeSceneChanged -= OnActiveSceneChanged;
            inspector.FrameSampled -= OnFrameSampled;
            RestoreGcMode();

            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        internal void OnUpdate()
        {
            try
            {
                if (stopped)
                {
                    if (options.ExitOnComplete && !exitIssued)
                    {
                        exitIssued = true;
                        Application.Quit();
                    }

                    return;
                }

                var timeout =
                    options.Profile == InspectorProbeProfile.Performance
                        ? options.PerformanceSeconds
                          + WarmupSeconds
                          + BaselineSeconds
                          + 180
                        : 180;
                if (Time.realtimeSinceStartup - startedAtRealtime
                    > timeout)
                {
                    Finish(
                        false,
                        "RunTimeout",
                        "T11 Inspector probe exceeded its timeout.");
                    return;
                }

                if (!loadRequested
                    && Time.realtimeSinceStartup - startedAtRealtime >= 2f
                    && string.Equals(
                        USceneManager.GetActiveScene().name,
                        "Menu_Title",
                        StringComparison.Ordinal)
                    && GameManager.instance != null)
                {
                    loadRequested = true;
                    GameManager.instance.LoadGameFromUI(
                        options.AutoLoadSlot);
                    return;
                }

                if (!routeStarted)
                {
                    AdvanceToRoute();
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        internal void OnLateUpdate()
        {
            if (stopped || !routeStarted)
            {
                return;
            }

            try
            {
                if (options.Profile
                    == InspectorProbeProfile.Functional)
                {
                    AdvanceFunctional();
                }
                else
                {
                    AdvancePerformance();
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
            USceneManager.activeSceneChanged += OnActiveSceneChanged;
            inspector.FrameSampled += OnFrameSampled;
            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeInspectorProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner =
                gameObject.AddComponent<RuntimeInspectorProbeRunner>();
            runner.Initialize(this);
            logInfo(
                "T11 Inspector probe created profile="
                + options.ProfileId
                + " runId="
                + options.RunId);
        }

        private void AdvanceToRoute()
        {
            var manager = GameManager.instance;
            var hero = HeroController.SilentInstance;
            if (manager == null
                || manager.gameState != GameState.PLAYING
                || manager.IsInSceneTransition
                || hero == null
                || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!string.Equals(
                    USceneManager.GetActiveScene().name,
                    RouteScene,
                    StringComparison.Ordinal))
            {
                if (!routeSceneRequested)
                {
                    routeSceneRequested = true;
                    BeginTransition(RouteScene);
                }

                return;
            }

            if (inspector.CurrentFrame == null
                || inspector.SceneProviderCount < 4
                || inspector.RegisteredFsms.Count == 0
                || inspector.RegisteredEnemies.Count == 0
                || inspector.RegisteredColliders.Count < 2)
            {
                return;
            }

            routeStarted = true;
            routeSceneEpoch = inspector.SceneEpoch;
            inspector.SetEnabled(true);
            inspector.SetOverlayVisible(true);
            inspector.SetExportPaused(false);
            if (options.Profile == InspectorProbeProfile.Performance)
            {
                originalGcMode = UnityGarbageCollector.GCMode;
                gcModeCaptured = true;
                inspector.SetExportPaused(true);
                inspector.SetEnabled(false);
                performancePhase = PerformancePhase.Warmup;
                performancePhaseStartedAt =
                    Time.realtimeSinceStartup;
            }
            else
            {
                functionalPhase = FunctionalPhase.Validate;
            }
        }

        private void AdvanceFunctional()
        {
            var movieTick = journal.LastCommittedMovieTick;
            switch (functionalPhase)
            {
                case FunctionalPhase.Validate:
                {
                    var frame = inspector.CurrentFrame;
                    if (frame == null
                        || frame.Sequence == observedFrameSequence
                        || frame.Sequence != parityFrameSequence
                        || !HasRequiredGroups(frame))
                    {
                        return;
                    }

                    observedFrameSequence = frame.Sequence;
                    directParity = parityFrameValid;
                    if (!directParity)
                    {
                        throw new InvalidOperationException(
                            parityFrameDetail);
                    }

                    stableKeys.AddRange(
                        inspector.VerificationStableKeys);
                    displayKeys.AddRange(inspector.DisplayOnlyKeys);
                    if (!stableKeys.Any(
                            value => value.StartsWith(
                                "component/",
                                StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException(
                            "No static hierarchy VerificationKey was "
                            + "registered.");
                    }

                    if (displayKeys.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "No display-only dynamic object was registered.");
                    }

                    var beforeGroup = inspector.OverlayGroup;
                    inspector.CycleOverlayGroup();
                    overlayGroupChanged = !string.Equals(
                        beforeGroup,
                        inspector.OverlayGroup,
                        StringComparison.Ordinal);
                    overlayRenderBaseline =
                        inspector.OverlayRenderCount;
                    if (!inspector.ExportCurrentFrameNow())
                    {
                        throw new InvalidOperationException(
                            "Could not export the current immutable frame.");
                    }

                    inspector.FlushExporter(TimeSpan.FromSeconds(2));
                    var lastLine = ReadLastLineShared(
                        inspector.WatchExportPath);
                    exportSameSource =
                        lastLine != null
                        && string.Equals(
                            lastLine,
                            WatchFrameJson.Serialize(frame),
                            StringComparison.Ordinal);
                    ScreenCapture.CaptureScreenshot(screenshotPath);
                    screenshotRequested = true;
                    phaseStartMovieTick = movieTick;
                    functionalPhase = FunctionalPhase.WaitOverlay;
                    break;
                }
                case FunctionalPhase.WaitOverlay:
                    if (movieTick - phaseStartMovieTick < 15)
                    {
                        return;
                    }

                    overlayRendered =
                        inspector.OverlayRenderCount
                        > overlayRenderBaseline
                        && inspector.OverlayLastRenderedFrameSequence >= 0;
                    colliderRendered =
                        inspector.ColliderRenderCount > 0;
                    screenshotWritten =
                        File.Exists(screenshotPath)
                        && new FileInfo(screenshotPath).Length > 0;
                    if (!screenshotWritten)
                    {
                        return;
                    }

                    inspector.FlushExporter(TimeSpan.FromSeconds(2));
                    pausedWrittenCount =
                        inspector.ExporterWrittenCount;
                    inspector.SetExportPaused(true);
                    phaseStartMovieTick = movieTick;
                    functionalPhase = FunctionalPhase.WaitPausedExport;
                    break;
                case FunctionalPhase.WaitPausedExport:
                    if (movieTick - phaseStartMovieTick < 30)
                    {
                        return;
                    }

                    inspector.FlushExporter(TimeSpan.FromSeconds(2));
                    exportPauseHeld =
                        inspector.ExporterWrittenCount
                        == pausedWrittenCount;
                    inspector.SetExportPaused(false);
                    phaseStartMovieTick = movieTick;
                    functionalPhase = FunctionalPhase.WaitResumedExport;
                    break;
                case FunctionalPhase.WaitResumedExport:
                    if (movieTick - phaseStartMovieTick < 30)
                    {
                        return;
                    }

                    inspector.FlushExporter(TimeSpan.FromSeconds(2));
                    exportResumed =
                        inspector.ExporterWrittenCount
                        > pausedWrittenCount;
                    var beforeDisable =
                        inspector.CaptureStatistics();
                    disabledSampleCount = beforeDisable.SampleCount;
                    disabledRenderCount =
                        inspector.OverlayRenderCount;
                    inspector.SetEnabled(false);
                    phaseStartMovieTick = movieTick;
                    functionalPhase = FunctionalPhase.WaitDisabled;
                    break;
                case FunctionalPhase.WaitDisabled:
                    if (movieTick - phaseStartMovieTick < 30)
                    {
                        return;
                    }

                    var disabledStats =
                        inspector.CaptureStatistics();
                    disableStoppedSampling =
                        disabledStats.SampleCount
                        == disabledSampleCount;
                    disableStoppedRendering =
                        inspector.OverlayRenderCount
                        == disabledRenderCount;
                    inspector.SetEnabled(true);
                    observedFrameSequence =
                        inspector.CurrentFrame?.Sequence ?? -1;
                    functionalPhase = FunctionalPhase.WaitReenabled;
                    break;
                case FunctionalPhase.WaitReenabled:
                {
                    var frame = inspector.CurrentFrame;
                    if (frame == null
                        || frame.Sequence == observedFrameSequence
                        || frame.Sequence != parityFrameSequence)
                    {
                        return;
                    }

                    if (!parityFrameValid)
                    {
                        throw new InvalidOperationException(
                            parityFrameDetail);
                    }

                    lifecycleSceneRequested = true;
                    BeginTransition(LifecycleScene);
                    functionalPhase = FunctionalPhase.WaitLifecycleScene;
                    break;
                }
                case FunctionalPhase.WaitLifecycleScene:
                    if (!string.Equals(
                            USceneManager.GetActiveScene().name,
                            LifecycleScene,
                            StringComparison.Ordinal)
                        || inspector.SceneEpoch <= routeSceneEpoch
                        || inspector.SceneProviderCount < 2
                        || inspector.CurrentFrame == null)
                    {
                        return;
                    }

                    sceneRebound = true;
                    finalStatistics =
                        inspector.CaptureStatistics();
                    var pass =
                        directParity
                        && overlayGroupChanged
                        && overlayRendered
                        && colliderRendered
                        && exportSameSource
                        && exportPauseHeld
                        && exportResumed
                        && disableStoppedSampling
                        && disableStoppedRendering
                        && screenshotRequested
                        && screenshotWritten
                        && sceneClearObserved
                        && sceneRebound
                        && stableKeys.Count > 0
                        && displayKeys.Count > 0
                        && finalStatistics.ProviderFailureCount == 0
                        && finalStatistics.ExporterDroppedCount == 0;
                    Finish(
                        pass,
                        pass ? "Completed" : "GateFailed",
                        pass
                            ? string.Empty
                            : "T11 functional acceptance conditions failed.");
                    break;
            }
        }

        private void AdvancePerformance()
        {
            var elapsed =
                Time.realtimeSinceStartup - performancePhaseStartedAt;
            switch (performancePhase)
            {
                case PerformancePhase.Warmup:
                    if (elapsed < WarmupSeconds)
                    {
                        return;
                    }

                    baselineAllocations.Clear();
                    BeginAllocationWindow();
                    performancePhase = PerformancePhase.Baseline;
                    performancePhaseStartedAt =
                        Time.realtimeSinceStartup;
                    break;
                case PerformancePhase.Baseline:
                    if (allocationWindowActive)
                    {
                        CaptureAllocationDelta(baselineAllocations);
                        if (elapsed >= AllocationWindowSeconds)
                        {
                            EndAllocationWindow();
                        }
                    }

                    if (elapsed < BaselineSeconds)
                    {
                        return;
                    }

                    inspector.ResetPerformanceStatistics();
                    overlayRenderBaseline =
                        inspector.OverlayRenderCount;
                    enabledAllocations.Clear();
                    inspector.SetEnabled(true);
                    inspector.SetOverlayVisible(true);
                    inspector.SetExportPaused(false);
                    performancePhase = PerformancePhase.Enabled;
                    performancePhaseStartedAt =
                        Time.realtimeSinceStartup;
                    break;
                case PerformancePhase.Enabled:
                    if (!enabledAllocationWindowStarted
                        && elapsed >= WarmupSeconds)
                    {
                        enabledAllocationWindowStarted = true;
                        BeginAllocationWindow();
                    }

                    if (allocationWindowActive)
                    {
                        CaptureAllocationDelta(enabledAllocations);
                        if (elapsed
                            >= WarmupSeconds + AllocationWindowSeconds)
                        {
                            EndAllocationWindow();
                        }
                    }

                    if (elapsed < options.PerformanceSeconds)
                    {
                        return;
                    }

                    inspector.FlushExporter(TimeSpan.FromSeconds(5));
                    finalStatistics =
                        inspector.CaptureStatistics();
                    baselineAllocationAverage =
                        Average(baselineAllocations);
                    baselineAllocationP95 =
                        Percentile(baselineAllocations, 0.95);
                    enabledAllocationAverage =
                        Average(enabledAllocations);
                    enabledAllocationP95 =
                        Percentile(enabledAllocations, 0.95);
                    attributableAllocationAverage = Math.Max(
                        0d,
                        enabledAllocationAverage
                        - baselineAllocationAverage);
                    attributableAllocationP95 = Math.Max(
                        0d,
                        enabledAllocationP95
                        - baselineAllocationP95);
                    var minimumSampleCount = Math.Max(
                        10L,
                        options.PerformanceSeconds / 2L);
                    var pass =
                        finalStatistics.SampleCount
                        >= minimumSampleCount
                        && finalStatistics.P95Milliseconds < 1d
                        && attributableAllocationAverage < 1024d
                        && finalStatistics.ProviderFailureCount == 0
                        && finalStatistics.ExporterDroppedCount == 0
                        && inspector.OverlayRenderCount
                        > overlayRenderBaseline
                        && baselineAllocations.Count > 0
                        && enabledAllocations.Count > 0;
                    Finish(
                        pass,
                        pass ? "Completed" : "GateFailed",
                        pass
                            ? string.Empty
                            : "T11 performance acceptance conditions failed.");
                    performancePhase = PerformancePhase.Complete;
                    break;
            }
        }

        private void BeginAllocationWindow()
        {
            if (!gcModeCaptured)
            {
                throw new InvalidOperationException(
                    "Original Unity GC mode was not captured.");
            }

            if (allocationWindowActive)
            {
                throw new InvalidOperationException(
                    "A managed-allocation window is already active.");
            }

            UnityGarbageCollector.GCMode =
                UnityGarbageCollector.Mode.Enabled;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            UnityGarbageCollector.GCMode =
                UnityGarbageCollector.Mode.Disabled;
            previousAllocationHeapBytes =
                Profiler.GetMonoUsedSizeLong();
            allocationWindowActive = true;
        }

        private void CaptureAllocationDelta(List<long> destination)
        {
            var current = Profiler.GetMonoUsedSizeLong();
            destination.Add(
                Math.Max(0L, current - previousAllocationHeapBytes));
            previousAllocationHeapBytes = current;
        }

        private void EndAllocationWindow()
        {
            if (!allocationWindowActive)
            {
                return;
            }

            UnityGarbageCollector.GCMode = originalGcMode;
            allocationWindowActive = false;
        }

        private void RestoreGcMode()
        {
            if (!gcModeCaptured)
            {
                return;
            }

            UnityGarbageCollector.GCMode = originalGcMode;
            allocationWindowActive = false;
            gcModeCaptured = false;
        }

        private bool ValidateDirectParity(
            WatchFrame frame,
            out string detail)
        {
            try
            {
                if (frame.Failures.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Watch frame contains provider failures.");
                }

                var hero = HeroController.SilentInstance
                           ?? throw new InvalidOperationException(
                               "Hero is unavailable.");
                var body = hero.GetComponent<Rigidbody2D>()
                           ?? throw new InvalidOperationException(
                               "Hero Rigidbody2D is unavailable.");
                var player = PlayerData.instance
                             ?? throw new InvalidOperationException(
                                 "PlayerData is unavailable.");
                var position = hero.transform.position;
                var velocity = body.velocity;
                Require(
                    frame,
                    "hero.position.x",
                    WatchValue.FromFloat32(position.x));
                Require(
                    frame,
                    "hero.position.y",
                    WatchValue.FromFloat32(position.y));
                Require(
                    frame,
                    "hero.velocity.x",
                    WatchValue.FromFloat32(velocity.x));
                Require(
                    frame,
                    "hero.velocity.y",
                    WatchValue.FromFloat32(velocity.y));
                Require(
                    frame,
                    "hero.actorState",
                    WatchValue.FromString(hero.hero_state.ToString()));
                Require(
                    frame,
                    "hero.cState.onGround",
                    WatchValue.FromBoolean(hero.cState.onGround));
                Require(
                    frame,
                    "player.health",
                    WatchValue.FromInt32(player.health));
                Require(
                    frame,
                    "player.maxHealth",
                    WatchValue.FromInt32(player.maxHealth));
                Require(
                    frame,
                    "player.mp",
                    WatchValue.FromInt32(player.MPCharge));

                foreach (var fsm in inspector.RegisteredFsms)
                {
                    Require(
                        frame,
                        fsm.KeyPrefix + "/active",
                        WatchValue.FromBoolean(fsm.Target.Active));
                    Require(
                        frame,
                        fsm.KeyPrefix + "/activeState",
                        WatchValue.FromString(
                            fsm.Target.ActiveStateName
                            ?? string.Empty));
                    if (!frame.Entries.ContainsKey(
                            fsm.KeyPrefix + "/recentEvent"))
                    {
                        throw new InvalidOperationException(
                            "FSM recent-event watch is missing.");
                    }
                }

                foreach (var enemy in inspector.RegisteredEnemies)
                {
                    var enemyBody =
                        enemy.Target.GetComponent<Rigidbody2D>();
                    Require(
                        frame,
                        enemy.KeyPrefix + "/hp",
                        WatchValue.FromInt32(enemy.Target.hp));
                    Require(
                        frame,
                        enemy.KeyPrefix + "/dead",
                        WatchValue.FromBoolean(enemy.Target.isDead));
                    Require(
                        frame,
                        enemy.KeyPrefix + "/position.x",
                        WatchValue.FromFloat32(enemyBody.position.x));
                    Require(
                        frame,
                        enemy.KeyPrefix + "/position.y",
                        WatchValue.FromFloat32(enemyBody.position.y));
                }

                foreach (var collider in inspector.RegisteredColliders)
                {
                    var target = collider.Target;
                    var bounds = target.bounds;
                    Require(
                        frame,
                        collider.KeyPrefix + "/enabled",
                        WatchValue.FromBoolean(target.enabled));
                    Require(
                        frame,
                        collider.KeyPrefix + "/isTrigger",
                        WatchValue.FromBoolean(target.isTrigger));
                    Require(
                        frame,
                        collider.KeyPrefix + "/bounds.center.x",
                        WatchValue.FromFloat32(bounds.center.x));
                    Require(
                        frame,
                        collider.KeyPrefix + "/bounds.center.y",
                        WatchValue.FromFloat32(bounds.center.y));
                    if (!frame.Entries.ContainsKey(
                            collider.KeyPrefix + "/shape"))
                    {
                        throw new InvalidOperationException(
                            "Collider shape watch is missing.");
                    }
                }

                foreach (var key in SemanticSnapshotSchemaV1.Keys)
                {
                    if (!frame.Entries.TryGetValue(key, out var entry)
                        || !InspectorVerificationWatchSchemaV1
                            .IsRegistered(entry.Descriptor))
                    {
                        throw new InvalidOperationException(
                            "T04/T05 stable watch key is missing: "
                            + key);
                    }
                }

                if (frame.Entries.Values.Any(
                        value =>
                            !value.Descriptor.Key.IsVerificationStable
                            && InspectorVerificationWatchSchemaV1
                                .IsRegistered(value.Descriptor)))
                {
                    throw new InvalidOperationException(
                        "Display-only key entered the verification schema.");
                }

                detail = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                detail = exception.Message;
                return false;
            }
        }

        private string parityFrameDetail = string.Empty;

        private void OnFrameSampled(WatchFrame frame)
        {
            parityFrameValid = ValidateDirectParity(
                frame,
                out parityFrameDetail);
            parityFrameSequence = frame.Sequence;
        }

        private static void Require(
            WatchFrame frame,
            string key,
            WatchValue expected)
        {
            if (!frame.Entries.TryGetValue(key, out var entry))
            {
                throw new InvalidOperationException(
                    "Watch key is missing: " + key);
            }

            if (!entry.Value.Equals(expected))
            {
                throw new InvalidOperationException(
                    "Watch value differs from direct component read: "
                    + key
                    + " frame="
                    + entry.Value.DisplayValue
                    + " direct="
                    + expected.DisplayValue);
            }
        }

        private static bool HasRequiredGroups(WatchFrame frame)
        {
            var groups = new HashSet<string>(
                frame.Entries.Values.Select(
                    value => value.Descriptor.Group),
                StringComparer.Ordinal);
            return groups.Contains("tick")
                   && groups.Contains("scene")
                   && groups.Contains("hero")
                   && groups.Contains("fsm")
                   && groups.Contains("enemy")
                   && groups.Contains("collider")
                   && groups.Contains("rng");
        }

        private void BeginTransition(string sceneName)
        {
            var manager = GameManager.instance
                          ?? throw new InvalidOperationException(
                              "GameManager is unavailable.");
            manager.BeginSceneTransition(
                new GameManager.SceneLoadInfo
                {
                    AlwaysUnloadUnusedAssets = true,
                    EntryGateName = string.Equals(
                        sceneName,
                        LifecycleScene,
                        StringComparison.Ordinal)
                            ? "door_dreamReturn"
                            : "door_dreamEnter",
                    EntryDelay = 0f,
                    PreventCameraFadeOut = true,
                    WaitForSceneTransitionCameraFade = false,
                    SceneName = sceneName,
                    Visualization =
                        GameManager.SceneLoadVisualizations.GodsAndGlory
                });
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            if (routeStarted && lifecycleSceneRequested)
            {
                sceneClearObserved =
                    inspector.CurrentFrame == null
                    && inspector.SceneProviderCount == 0;
            }
        }

        private void Finish(
            bool success,
            string reason,
            string failure)
        {
            if (stopped)
            {
                return;
            }

            stopReason = reason;
            error = failure ?? string.Empty;
            RestoreGcMode();
            finalStatistics ??= inspector.CaptureStatistics();
            runPass = success;
            WriteResult();
            stopped = true;
            WriteAtomic(
                Path.Combine(outputDirectory, "probe.done"),
                new UTF8Encoding(false).GetBytes(
                    (runPass ? "PASS" : "FAIL")
                    + " "
                    + stopReason
                    + "\n"));
            logInfo(
                "T11 Inspector probe stopped profile="
                + options.ProfileId
                + " pass="
                + runPass);
        }

        private void Fail(Exception exception)
        {
            logError("T11 Inspector probe failed: " + exception);
            Finish(
                false,
                "Exception",
                exception.GetType().Name + ": " + exception.Message);
        }

        private void WriteResult()
        {
            var stats = finalStatistics
                        ?? inspector.CaptureStatistics();
            var builder = new StringBuilder(16384);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "profile", options.ProfileId);
            AppendString(builder, "runId", options.RunId);
            AppendBoolean(builder, "runPass", runPass);
            AppendString(builder, "stopReason", stopReason);
            AppendString(builder, "error", error);
            AppendBoolean(builder, "directParity", directParity);
            AppendBoolean(
                builder,
                "overlayGroupChanged",
                overlayGroupChanged);
            AppendBoolean(
                builder,
                "overlayRendered",
                overlayRendered);
            AppendBoolean(
                builder,
                "colliderRendered",
                colliderRendered);
            AppendBoolean(
                builder,
                "exportSameSource",
                exportSameSource);
            AppendBoolean(
                builder,
                "exportPauseHeld",
                exportPauseHeld);
            AppendBoolean(
                builder,
                "exportResumed",
                exportResumed);
            AppendBoolean(
                builder,
                "disableStoppedSampling",
                disableStoppedSampling);
            AppendBoolean(
                builder,
                "disableStoppedRendering",
                disableStoppedRendering);
            AppendBoolean(
                builder,
                "sceneClearObserved",
                sceneClearObserved);
            AppendBoolean(builder, "sceneRebound", sceneRebound);
            AppendBoolean(
                builder,
                "screenshotWritten",
                screenshotWritten);
            AppendString(
                builder,
                "screenshotRelativePath",
                "screenshots/overlay.png");
            AppendNumber(
                builder,
                "sampleCount",
                stats.SampleCount);
            AppendDouble(
                builder,
                "sampleP50Milliseconds",
                stats.P50Milliseconds);
            AppendDouble(
                builder,
                "sampleP95Milliseconds",
                stats.P95Milliseconds);
            AppendDouble(
                builder,
                "sampleMaximumMilliseconds",
                stats.MaximumMilliseconds);
            AppendNumber(
                builder,
                "providerFailureCount",
                stats.ProviderFailureCount);
            AppendNumber(
                builder,
                "exporterWrittenCount",
                stats.ExporterWrittenCount);
            AppendNumber(
                builder,
                "exporterDroppedCount",
                stats.ExporterDroppedCount);
            AppendNumber(
                builder,
                "requestedSampleInterval",
                stats.RequestedSampleInterval);
            AppendNumber(
                builder,
                "effectiveSampleInterval",
                stats.EffectiveSampleInterval);
            AppendNumber(
                builder,
                "performanceSeconds",
                options.PerformanceSeconds);
            AppendString(
                builder,
                "allocationMeasurementMethod",
                "mono-used-heap-gc-disabled-delta-v1");
            AppendNumber(
                builder,
                "allocationWindowSeconds",
                AllocationWindowSeconds);
            AppendNumber(
                builder,
                "baselineAllocationFrames",
                baselineAllocations.Count);
            AppendNumber(
                builder,
                "enabledAllocationFrames",
                enabledAllocations.Count);
            AppendDouble(
                builder,
                "baselineGcAllocAverageBytesPerFrame",
                baselineAllocationAverage);
            AppendDouble(
                builder,
                "baselineGcAllocP95BytesPerFrame",
                baselineAllocationP95);
            AppendDouble(
                builder,
                "enabledGcAllocAverageBytesPerFrame",
                enabledAllocationAverage);
            AppendDouble(
                builder,
                "enabledGcAllocP95BytesPerFrame",
                enabledAllocationP95);
            AppendDouble(
                builder,
                "inspectorAttributableAverageBytesPerFrame",
                attributableAllocationAverage);
            AppendDouble(
                builder,
                "inspectorAttributableP95BytesPerFrame",
                attributableAllocationP95);
            AppendStringArray(
                builder,
                "verificationStableKeys",
                stableKeys);
            AppendStringArray(
                builder,
                "displayOnlyKeys",
                displayKeys);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                new UTF8Encoding(false, true).GetBytes(
                    builder.ToString()));
            WriteAtomic(
                Path.Combine(outputDirectory, "verdict.md"),
                new UTF8Encoding(false).GetBytes(
                    "# T11 Inspector Probe\n\n"
                    + "- Profile: `" + options.ProfileId + "`\n"
                    + "- Pass: `" + runPass + "`\n"
                    + "- Sample p95 ms: `"
                    + stats.P95Milliseconds.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + "`\n"
                    + "- Attributable average B/frame: `"
                    + attributableAllocationAverage.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + "`\n"));
        }

        private static double Average(IReadOnlyList<long> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double total = 0d;
            foreach (var value in values)
            {
                total += value;
            }

            return total / values.Count;
        }

        private static string? ReadLastLineShared(string path)
        {
            string? last = null;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(
                       stream,
                       new UTF8Encoding(false, true),
                       false,
                       4096,
                       false))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    last = line;
                }
            }

            return last;
        }

        private static double Percentile(
            IReadOnlyList<long> values,
            double percentile)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var ordered = values.OrderBy(value => value).ToArray();
            var index = (int)Math.Ceiling(
                            ordered.Length * percentile)
                        - 1;
            return ordered[
                Math.Max(0, Math.Min(ordered.Length - 1, index))];
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendDouble(
            StringBuilder builder,
            string name,
            double value)
        {
            AppendPrefix(builder, name);
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AppendStringArray(
            StringBuilder builder,
            string name,
            IEnumerable<string> values)
        {
            AppendPrefix(builder, name);
            builder.Append('[');
            var index = 0;
            foreach (var value in values
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                if (index++ > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(builder, value);
            }

            builder.Append(']');
        }

        private static void AppendPrefix(
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

        private static void WriteAtomic(string path, byte[] bytes)
        {
            var temporary = path
                            + ".tmp-"
                            + Guid.NewGuid().ToString("N");
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
    }

    internal enum FunctionalPhase : byte
    {
        Validate = 0,
        WaitOverlay = 1,
        WaitPausedExport = 2,
        WaitResumedExport = 3,
        WaitDisabled = 4,
        WaitReenabled = 5,
        WaitLifecycleScene = 6
    }

    internal enum PerformancePhase : byte
    {
        Warmup = 0,
        Baseline = 1,
        Enabled = 2,
        Complete = 3
    }

    internal enum InspectorProbeProfile : byte
    {
        Functional = 1,
        Performance = 2
    }

    internal sealed class InspectorProbeOptions
    {
        private InspectorProbeOptions(
            InspectorProbeProfile profile,
            string runId,
            int autoLoadSlot,
            int performanceSeconds,
            bool exitOnComplete)
        {
            Profile = profile;
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
            PerformanceSeconds = performanceSeconds;
            ExitOnComplete = exitOnComplete;
        }

        public InspectorProbeProfile Profile { get; }
        public string ProfileId =>
            Profile.ToString().ToUpperInvariant();
        public string RunId { get; }
        public int AutoLoadSlot { get; }
        public int PerformanceSeconds { get; }
        public bool ExitOnComplete { get; }

        public static InspectorProbeParseResult Parse(
            string[] arguments)
        {
            var profileText = ReadValue(
                arguments,
                "--hktas-inspector-probe=");
            if (profileText == null)
            {
                return InspectorProbeParseResult.NotRequested();
            }

            if (!Enum.TryParse(
                    profileText,
                    true,
                    out InspectorProbeProfile profile))
            {
                return InspectorProbeParseResult.Invalid(
                    "profile must be FUNCTIONAL or PERFORMANCE");
            }

            var runId = ReadValue(
                            arguments,
                            "--hktas-inspector-probe-run=")
                        ?? profileText.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!IsIdentifier(runId))
            {
                return InspectorProbeParseResult.Invalid(
                    "run ID contains unsupported characters");
            }

            var slotText = ReadValue(
                               arguments,
                               "--hktas-inspector-probe-slot=")
                           ?? "2";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return InspectorProbeParseResult.Invalid(
                    "slot must be in [1, 4]");
            }

            var durationText = ReadValue(
                                   arguments,
                                   "--hktas-inspector-performance-seconds=")
                               ?? "600";
            if (!int.TryParse(
                    durationText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                out var duration)
                || duration < 60
                || duration > 7200)
            {
                return InspectorProbeParseResult.Invalid(
                    "performance duration must be in [60, 7200]");
            }

            return InspectorProbeParseResult.Valid(
                new InspectorProbeOptions(
                    profile,
                    runId,
                    slot,
                    duration,
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-inspector-probe-exit",
                            StringComparison.OrdinalIgnoreCase))));
        }

        private static string? ReadValue(
            IEnumerable<string> arguments,
            string prefix)
        {
            return arguments.FirstOrDefault(
                    value => value.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                ?.Substring(prefix.Length);
        }

        private static bool IsIdentifier(string value)
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

    internal sealed class InspectorProbeParseResult
    {
        private InspectorProbeParseResult(
            bool requested,
            InspectorProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public InspectorProbeOptions? Options { get; }
        public string? Error { get; }

        public static InspectorProbeParseResult NotRequested()
        {
            return new InspectorProbeParseResult(false, null, null);
        }

        public static InspectorProbeParseResult Valid(
            InspectorProbeOptions options)
        {
            return new InspectorProbeParseResult(true, options, null);
        }

        public static InspectorProbeParseResult Invalid(string error)
        {
            return new InspectorProbeParseResult(true, null, error);
        }
    }

    [DefaultExecutionOrder(31000)]
    internal sealed class RuntimeInspectorProbeRunner : MonoBehaviour
    {
        private RuntimeInspectorProbeExperiment? owner;

        public void Initialize(
            RuntimeInspectorProbeExperiment value)
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
