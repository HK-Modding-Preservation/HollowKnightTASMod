using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.Rng;
using InControl;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Inspector
{
    public sealed class RuntimeInspectorStatistics
    {
        internal RuntimeInspectorStatistics(
            long sampleCount,
            double p50Milliseconds,
            double p95Milliseconds,
            double maximumMilliseconds,
            long providerFailureCount,
            long exporterWrittenCount,
            long exporterDroppedCount,
            long overlayRenderCount,
            long colliderRenderCount,
            int requestedSampleInterval,
            int effectiveSampleInterval)
        {
            SampleCount = sampleCount;
            P50Milliseconds = p50Milliseconds;
            P95Milliseconds = p95Milliseconds;
            MaximumMilliseconds = maximumMilliseconds;
            ProviderFailureCount = providerFailureCount;
            ExporterWrittenCount = exporterWrittenCount;
            ExporterDroppedCount = exporterDroppedCount;
            OverlayRenderCount = overlayRenderCount;
            ColliderRenderCount = colliderRenderCount;
            RequestedSampleInterval = requestedSampleInterval;
            EffectiveSampleInterval = effectiveSampleInterval;
        }

        public long SampleCount { get; }
        public double P50Milliseconds { get; }
        public double P95Milliseconds { get; }
        public double MaximumMilliseconds { get; }
        public long ProviderFailureCount { get; }
        public long ExporterWrittenCount { get; }
        public long ExporterDroppedCount { get; }
        public long OverlayRenderCount { get; }
        public long ColliderRenderCount { get; }
        public int RequestedSampleInterval { get; }
        public int EffectiveSampleInterval { get; }
    }

    public sealed class RuntimeInspector : IDisposable
    {
        private const int MaximumDurationSamples = 65536;

        private readonly RuntimeReplayJournal journal;
        private readonly RuntimeRngProbe? rngProbe;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly Action<
            string,
            IReadOnlyDictionary<string, string>> emit;
        private readonly WatchRegistry registry = new WatchRegistry();
        private readonly OverlayRenderer overlay;
        private readonly ColliderOverlayRenderer colliderOverlay =
            new ColliderOverlayRenderer();
        private readonly WatchJsonLinesExporter? exporter;
        private readonly List<ColliderWatchProvider> colliderProviders =
            new List<ColliderWatchProvider>();
        private readonly List<FsmWatchProvider> fsmProviders =
            new List<FsmWatchProvider>();
        private readonly List<EnemyWatchProvider> enemyProviders =
            new List<EnemyWatchProvider>();
        private readonly List<string> sceneProviderIds =
            new List<string>();
        private readonly List<long> sampleDurationTicks =
            new List<long>();
        private readonly string outputDirectory;
        private readonly int requestedSampleInterval;
        private readonly int exportEverySamples;
        private RuntimeInspectorRunner? runner;
        private Func<long>? movieTickSource;
        private WatchFrame? currentFrame;
        private long visualTick;
        private long fixedTick;
        private long lastSampleMovieTick = long.MinValue;
        private long lastPreInputVisualTick = long.MinValue;
        private long sampleCount;
        private long providerFailureCount;
        private long registrationSequence;
        private int effectiveSampleInterval;
        private int sceneEpoch;
        private int sceneBindDelay;
        private bool sceneBindPending;
        private bool sceneHookRegistered;
        private bool baselineHookRegistered;
        private bool runtimePumpRequested;
        private bool enabled;
        private bool disposed;

        public RuntimeInspector(
            string sessionDirectory,
            RuntimeReplayJournal journal,
            RuntimeRngProbe? rngProbe,
            bool enabled,
            bool overlayVisible,
            bool exportEnabled,
            int sampleEveryMovieTicks,
            int exportEverySamples,
            int exportQueueCapacity,
            TimeSpan flushTimeout,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError,
            Action<
                string,
                IReadOnlyDictionary<string, string>> emit)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
            {
                throw new ArgumentException(
                    "A session directory is required.",
                    nameof(sessionDirectory));
            }

            if (sampleEveryMovieTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleEveryMovieTicks));
            }

            if (exportEverySamples <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exportEverySamples));
            }

            this.journal = journal
                           ?? throw new ArgumentNullException(
                               nameof(journal));
            this.rngProbe = rngProbe;
            this.enabled = enabled;
            this.logInfo = logInfo
                           ?? throw new ArgumentNullException(
                               nameof(logInfo));
            this.logWarning = logWarning
                              ?? throw new ArgumentNullException(
                                  nameof(logWarning));
            this.logError = logError
                            ?? throw new ArgumentNullException(
                                nameof(logError));
            this.emit = emit
                        ?? throw new ArgumentNullException(nameof(emit));
            requestedSampleInterval = sampleEveryMovieTicks;
            effectiveSampleInterval = sampleEveryMovieTicks;
            this.exportEverySamples = exportEverySamples;
            outputDirectory = Path.Combine(
                Path.GetFullPath(sessionDirectory),
                "inspector");
            Directory.CreateDirectory(outputDirectory);
            overlay = new OverlayRenderer(overlayVisible);
            if (exportEnabled)
            {
                exporter = new WatchJsonLinesExporter(
                    Path.Combine(outputDirectory, "watches.jsonl"),
                    exportQueueCapacity,
                    flushTimeout,
                    OnExporterPressure,
                    budgetDirectory: Path.GetDirectoryName(Path.GetFullPath(sessionDirectory)));
            }

            registry.Register(
                new TickWatchProvider(sampleEveryMovieTicks));
            registry.Register(
                new HeroWatchProvider(sampleEveryMovieTicks));
            registry.Register(
                new BossPracticeWatchProvider(sampleEveryMovieTicks));
            if (rngProbe != null)
            {
                registry.Register(
                    new RngWatchProvider(
                        rngProbe,
                        sampleEveryMovieTicks));
            }

            runtimePumpRequested = enabled || overlayVisible || exportEnabled;
            journal.BaselineEstablished += OnBaselineEstablished;
            baselineHookRegistered = true;
            if (runtimePumpRequested && journal.IsAvailable)
            {
                EnsureRuntimePump();
            }

            logInfo(
                "T11 Inspector started enabled="
                + enabled
                + " interval="
                + sampleEveryMovieTicks
                + " export="
                + exportEnabled);
        }

        public bool Enabled => enabled;
        public bool OverlayVisible => overlay.Visible;
        public bool ExportPaused => exporter?.Paused ?? true;
        public WatchFrame? CurrentFrame => currentFrame;
        public int SceneEpoch => sceneEpoch;
        public int SceneProviderCount => sceneProviderIds.Count;
        public int RegisteredColliderCount => colliderProviders.Count;
        public string OverlayGroup => overlay.CurrentGroup;
        public long OverlayLastRenderedFrameSequence =>
            overlay.LastRenderedFrameSequence;
        public long OverlayRenderCount => overlay.RenderCount;
        public long ColliderRenderCount => colliderOverlay.RenderCount;
        public long ExporterWrittenCount =>
            exporter?.WrittenCount ?? 0;
        public long ExporterDroppedCount =>
            exporter?.DroppedCount ?? 0;
        internal IReadOnlyList<ColliderWatchProvider>
            RegisteredColliders => colliderProviders;
        internal IReadOnlyList<FsmWatchProvider>
            RegisteredFsms => fsmProviders;
        internal IReadOnlyList<EnemyWatchProvider>
            RegisteredEnemies => enemyProviders;
        public string WatchExportPath =>
            Path.Combine(outputDirectory, "watches.jsonl");
        public string PerformancePath =>
            Path.Combine(outputDirectory, "performance.json");

        internal event Action<WatchFrame>? FrameSampled;

        public void SetMovieTickSource(Func<long>? source)
        {
            movieTickSource = source;
        }

        public IReadOnlyList<string> VerificationStableKeys =>
            currentFrame?.Entries.Values
                .Where(
                    value =>
                        value.Descriptor.Key.IsVerificationStable)
                .Select(value => value.Descriptor.Key.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
            ?? Array.Empty<string>();

        public IReadOnlyList<string> DisplayOnlyKeys =>
            currentFrame?.Entries.Values
                .Where(
                    value =>
                        !value.Descriptor.Key.IsVerificationStable)
                .Select(value => value.Descriptor.Key.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
            ?? Array.Empty<string>();

        public void SetEnabled(bool value)
        {
            if (disposed || enabled == value)
            {
                return;
            }

            enabled = value;
            if (enabled)
            {
                runtimePumpRequested = true;
                if (journal.IsAvailable)
                {
                    EnsureRuntimePump();
                }
            }

            emit(
                "inspector-enabled-changed",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["enabled"] = enabled ? "true" : "false"
                });
        }

        public void SetOverlayVisible(bool value)
        {
            overlay.SetVisible(value);
            if (value)
            {
                runtimePumpRequested = true;
                if (journal.IsAvailable)
                {
                    EnsureRuntimePump();
                }
            }
        }

        public void CycleOverlayGroup()
        {
            overlay.CycleGroup();
        }

        public void SetExportPaused(bool value)
        {
            exporter?.SetPaused(value);
            if (!value && exporter != null)
            {
                runtimePumpRequested = true;
                if (journal.IsAvailable)
                {
                    EnsureRuntimePump();
                }
            }

            emit(
                "inspector-export-pause-changed",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["paused"] = value ? "true" : "false"
                });
        }

        public RuntimeInspectorStatistics CaptureStatistics()
        {
            var ordered = sampleDurationTicks
                .OrderBy(value => value)
                .ToArray();
            return new RuntimeInspectorStatistics(
                sampleCount,
                PercentileMilliseconds(ordered, 0.50),
                PercentileMilliseconds(ordered, 0.95),
                ordered.Length == 0
                    ? 0d
                    : TicksToMilliseconds(ordered[ordered.Length - 1]),
                providerFailureCount,
                exporter?.WrittenCount ?? 0,
                exporter?.DroppedCount ?? 0,
                overlay.RenderCount,
                colliderOverlay.RenderCount,
                requestedSampleInterval,
                effectiveSampleInterval);
        }

        public void ResetPerformanceStatistics()
        {
            sampleDurationTicks.Clear();
            sampleCount = 0;
            providerFailureCount = 0;
        }

        public void FlushExporter(TimeSpan timeout)
        {
            exporter?.Flush(timeout);
        }

        public bool ExportCurrentFrameNow()
        {
            return exporter != null
                   && currentFrame != null
                   && exporter.TryExport(currentFrame);
        }

        internal void OnUpdate()
        {
            visualTick++;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F6))
            {
                overlay.SetVisible(!overlay.Visible);
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.F7))
            {
                overlay.CycleGroup();
            }
        }

        internal void OnFixedUpdate()
        {
            fixedTick++;
        }

        internal WatchFrame CaptureOnDemand(long movieTick)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(RuntimeInspector));
            var hero = HeroController.SilentInstance;
            var manager = GameManager.instance;
            if (hero == null || !hero.gameObject.activeInHierarchy
                || manager == null || manager.gameState != GlobalEnums.GameState.PLAYING
                || manager.IsInSceneTransition)
                throw new InvalidOperationException("Combat state requires a loaded gameplay scene.");

            // Called on the Unity command boundary. Read providers directly;
            // do not run Unity updates, advance ticks, or export another trace.
            AdvanceSceneBinding();
            var stamp = new TickStamp(InputManager.CurrentTick, visualTick,
                fixedTick, sceneEpoch, TickPhase.LateUpdateEnd);
            var frame = registry.SampleFresh(stamp, movieTick);
            currentFrame = frame;
            return frame;
        }

        internal void OnLateUpdate()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                AdvanceSceneBinding();
                if (!enabled)
                {
                    return;
                }

                var movieTick = movieTickSource?.Invoke()
                                ?? journal.LastCommittedMovieTick;
                var hero = HeroController.SilentInstance;
                var manager = GameManager.instance;
                // A freshly loaded seated fixture has no committed input yet,
                // so the journal reports movieTick=-1. Publish exactly one
                // pre-input frame instead of making the most important TAS
                // starting state invisible. The lastSampleMovieTick guard
                // below suppresses repeats until the first committed tick.
                if (hero == null
                    || !hero.gameObject.activeInHierarchy
                    || manager == null
                    || manager.gameState != GlobalEnums.GameState.PLAYING
                    || manager.IsInSceneTransition)
                {
                    return;
                }

                if (movieTick < 0)
                {
                    if (lastPreInputVisualTick != long.MinValue
                        && visualTick - lastPreInputVisualTick
                        < effectiveSampleInterval)
                    {
                        return;
                    }
                }
                else if (lastSampleMovieTick != long.MinValue
                         && movieTick >= lastSampleMovieTick
                         && movieTick - lastSampleMovieTick
                         < effectiveSampleInterval)
                {
                    return;
                }

                var stamp = new TickStamp(
                    InputManager.CurrentTick,
                    visualTick,
                    fixedTick,
                    sceneEpoch,
                    TickPhase.LateUpdateEnd);
                var started = Stopwatch.GetTimestamp();
                var frame = registry.Sample(stamp, movieTick);
                var elapsed = Stopwatch.GetTimestamp() - started;
                AddDuration(elapsed);
                sampleCount++;
                providerFailureCount = checked(
                    providerFailureCount + frame.Failures.Count);
                currentFrame = frame;
                if (movieTick < 0)
                {
                    lastPreInputVisualTick = visualTick;
                }
                else
                {
                    lastSampleMovieTick = movieTick;
                }
                NotifyFrameSampled(frame);
                if (exporter != null
                    && sampleCount % exportEverySamples == 0)
                {
                    exporter.TryExport(frame);
                }
            }
            catch (Exception exception)
            {
                logError(
                    "T11 Inspector sampling fault: "
                    + exception);
                emit(
                    "inspector-fault",
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["detail"] =
                            exception.GetType().Name
                            + ":"
                            + exception.Message
                    });
                SetEnabled(false);
            }
        }

        private void NotifyFrameSampled(WatchFrame frame)
        {
            try
            {
                FrameSampled?.Invoke(frame);
            }
            catch (Exception exception)
            {
                logWarning(
                    "T11 Inspector frame observer fault: "
                    + exception.Message);
                emit(
                    "inspector-observer-fault",
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["detail"] =
                            exception.GetType().Name
                            + ":"
                            + exception.Message
                    });
            }
        }

        internal void OnGui()
        {
            if (disposed || !enabled)
            {
                return;
            }

            overlay.Draw(currentFrame);
            colliderOverlay.Draw(
                colliderProviders,
                overlay.Visible);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (baselineHookRegistered)
            {
                journal.BaselineEstablished -= OnBaselineEstablished;
                baselineHookRegistered = false;
            }

            if (sceneHookRegistered)
            {
                USceneManager.activeSceneChanged -= OnActiveSceneChanged;
                sceneHookRegistered = false;
            }

            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }

            WritePerformance();
            try
            {
                exporter?.Flush(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                logWarning(
                    "T11 watch export flush failed: "
                    + exception.Message);
            }

            exporter?.Dispose();
            ClearSceneProviders();
            registry.Dispose();
            colliderOverlay.Dispose();
            logInfo("T11 Inspector stopped.");
        }

        private void OnBaselineEstablished(string baselineId, string sha256)
        {
            if (runtimePumpRequested)
            {
                EnsureRuntimePump();
            }
        }

        private void EnsureRuntimePump()
        {
            if (disposed || runner != null)
            {
                return;
            }

            if (!sceneHookRegistered)
            {
                USceneManager.activeSceneChanged += OnActiveSceneChanged;
                sceneHookRegistered = true;
            }

            sceneBindPending = true;
            sceneBindDelay = 2;
            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeInspector");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimeInspectorRunner>();
            runner.Initialize(this);
        }

        private void AdvanceSceneBinding()
        {
            if (!sceneBindPending)
            {
                return;
            }

            if (sceneBindDelay > 0)
            {
                sceneBindDelay--;
                return;
            }

            var scene = USceneManager.GetActiveScene();
            var hero = HeroController.SilentInstance;
            if (!scene.IsValid()
                || string.IsNullOrEmpty(scene.name)
                || hero == null
                || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            BindExplicitTargets(hero);
            sceneBindPending = false;
        }

        private void BindExplicitTargets(HeroController hero)
        {
            ClearSceneProviders();
            var interval = requestedSampleInterval;
            var fsms = hero.GetComponents<PlayMakerFSM>()
                .Where(value => value != null)
                .OrderBy(value => value.FsmName, StringComparer.Ordinal)
                .ToArray();
            if (fsms.Length > 0)
            {
                var fsm = fsms[0];
                var provider = new FsmWatchProvider(
                    "scene.hero-fsm",
                    fsm,
                    RuntimeWatchIdentity.StableHeroPrefix("fsm", 0),
                    true,
                    interval);
                RegisterSceneProvider(provider);
                fsmProviders.Add(provider);
            }

            var colliders = hero.GetComponents<Collider2D>()
                .Where(value => value != null)
                .ToArray();
            if (colliders.Length > 0)
            {
                var provider = new ColliderWatchProvider(
                    "scene.hero-collider",
                    colliders[0],
                    RuntimeWatchIdentity.StableHeroPrefix(
                        "collider",
                        0),
                    true,
                    interval);
                RegisterSceneProvider(provider);
                colliderProviders.Add(provider);
            }

            var activeScene = USceneManager.GetActiveScene();
            var staticCollider = UnityEngine.Object
                .FindObjectsOfType<Collider2D>()
                .Where(
                    value => value != null
                             && value.gameObject.activeInHierarchy
                             && value.gameObject.scene == activeScene
                             && !ReferenceEquals(
                                 value.gameObject,
                                 hero.gameObject)
                             && value.GetComponentInParent<HealthManager>()
                             == null)
                .OrderBy(
                    value => RuntimeWatchIdentity.BuildHierarchyPath(
                        value.transform),
                    StringComparer.Ordinal)
                .ThenBy(
                    value => value.GetType().FullName,
                    StringComparer.Ordinal)
                .FirstOrDefault();
            if (staticCollider != null)
            {
                var provider = new ColliderWatchProvider(
                    "scene.static-collider",
                    staticCollider,
                    RuntimeWatchIdentity.StableComponentPrefix(
                        staticCollider),
                    true,
                    interval);
                RegisterSceneProvider(provider);
                colliderProviders.Add(provider);
            }

            var enemy = UnityEngine.Object
                .FindObjectsOfType<HealthManager>()
                .Where(
                    value => value != null
                             && value.gameObject.activeInHierarchy
                             && value.gameObject.scene == activeScene
                             && value.GetComponent<Rigidbody2D>() != null)
                .OrderBy(
                    value => RuntimeWatchIdentity.BuildHierarchyPath(
                        value.transform),
                    StringComparer.Ordinal)
                .FirstOrDefault();
            if (enemy != null)
            {
                registrationSequence++;
                var provider = new EnemyWatchProvider(
                        "scene.enemy",
                        enemy,
                        enemy.GetComponent<Rigidbody2D>(),
                        RuntimeWatchIdentity.DisplayPrefix(
                            sceneEpoch,
                            "enemy",
                            registrationSequence),
                        false,
                        interval);
                RegisterSceneProvider(provider);
                enemyProviders.Add(provider);
            }

            emit(
                "inspector-scene-bound",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["colliderCount"] =
                        colliderProviders.Count.ToString(
                            CultureInfo.InvariantCulture),
                    ["providerCount"] =
                        sceneProviderIds.Count.ToString(
                            CultureInfo.InvariantCulture),
                    ["scene"] = activeScene.name
                });
        }

        private void RegisterSceneProvider(IWatchProvider provider)
        {
            registry.Register(provider);
            sceneProviderIds.Add(provider.ProviderId);
        }

        private void ClearSceneProviders()
        {
            foreach (var providerId in sceneProviderIds)
            {
                registry.Unregister(providerId);
            }

            sceneProviderIds.Clear();
            colliderProviders.Clear();
            fsmProviders.Clear();
            enemyProviders.Clear();
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            sceneEpoch++;
            ClearSceneProviders();
            registry.InvalidateAll();
            currentFrame = null;
            lastSampleMovieTick = long.MinValue;
            lastPreInputVisualTick = long.MinValue;
            sceneBindPending = true;
            sceneBindDelay = 2;
            emit(
                "inspector-scene-cleared",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["current"] = current.name ?? string.Empty,
                    ["previous"] = previous.name ?? string.Empty,
                    ["sceneEpoch"] =
                        sceneEpoch.ToString(
                            CultureInfo.InvariantCulture)
                });
        }

        private void OnExporterPressure(long dropped)
        {
            var previous = effectiveSampleInterval;
            effectiveSampleInterval = Math.Min(
                1000,
                Math.Max(
                    effectiveSampleInterval + 1,
                    effectiveSampleInterval * 2));
            logWarning(
                "T11 exporter pressure; sample interval "
                + previous
                + " -> "
                + effectiveSampleInterval
                + ", dropped="
                + dropped);
            emit(
                "inspector-downsampled",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dropped"] = dropped.ToString(
                        CultureInfo.InvariantCulture),
                    ["newInterval"] =
                        effectiveSampleInterval.ToString(
                            CultureInfo.InvariantCulture),
                    ["previousInterval"] =
                        previous.ToString(CultureInfo.InvariantCulture)
                });
        }

        private void AddDuration(long ticks)
        {
            if (sampleDurationTicks.Count == MaximumDurationSamples)
            {
                sampleDurationTicks.RemoveAt(0);
            }

            sampleDurationTicks.Add(ticks);
        }

        private void WritePerformance()
        {
            try
            {
                var stats = CaptureStatistics();
                var builder = new StringBuilder(1024);
                builder.Append("{\"schemaVersion\":1");
                AppendNumber(builder, "sampleCount", stats.SampleCount);
                AppendDouble(
                    builder,
                    "p50Milliseconds",
                    stats.P50Milliseconds);
                AppendDouble(
                    builder,
                    "p95Milliseconds",
                    stats.P95Milliseconds);
                AppendDouble(
                    builder,
                    "maximumMilliseconds",
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
                    "overlayRenderCount",
                    stats.OverlayRenderCount);
                AppendNumber(
                    builder,
                    "colliderRenderCount",
                    stats.ColliderRenderCount);
                AppendNumber(
                    builder,
                    "requestedSampleInterval",
                    stats.RequestedSampleInterval);
                AppendNumber(
                    builder,
                    "effectiveSampleInterval",
                    stats.EffectiveSampleInterval);
                builder.Append('}');
                WriteAtomic(
                    PerformancePath,
                    new UTF8Encoding(false, true).GetBytes(
                        builder.ToString()));
            }
            catch (Exception exception)
            {
                logError(
                    "T11 performance evidence write failed: "
                    + exception.Message);
            }
        }

        private static double PercentileMilliseconds(
            IReadOnlyList<long> ordered,
            double percentile)
        {
            if (ordered.Count == 0)
            {
                return 0d;
            }

            var index = (int)Math.Ceiling(
                            percentile * ordered.Count)
                        - 1;
            index = Math.Max(0, Math.Min(ordered.Count - 1, index));
            return TicksToMilliseconds(ordered[index]);
        }

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
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

        private static void AppendPrefix(
            StringBuilder builder,
            string name)
        {
            builder.Append(',');
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

    [DefaultExecutionOrder(30000)]
    internal sealed class RuntimeInspectorRunner : MonoBehaviour
    {
        private RuntimeInspector? owner;

        public void Initialize(RuntimeInspector value)
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

        private void OnGUI()
        {
            if (Media.RuntimeVideoCapture.HideTasOverlays) return;
            owner?.OnGui();
        }
    }
}
