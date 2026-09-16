using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Runtime.Timing;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.State
{
    public sealed class RuntimeSnapshotProbeExperiment : IDisposable
    {
        private const string SceneTarget = "GG_Vengefly";
        private readonly SnapshotProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logDebug;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly string outputDirectory;
        private readonly RuntimeSnapshotCapture capture = new RuntimeSnapshotCapture();
        private readonly InControlClockProbe inputClock;
        private readonly SceneEpochTracker sceneTracker;
        private readonly List<string> stableHashes = new List<string>(100);

        private RuntimeSnapshotProbeRunner? runner;
        private SnapshotCaptureResult? baseline;
        private bool attached;
        private bool stopped;
        private bool exitRequested;
        private bool exitIssued;
        private bool loadRequested;
        private bool frozen;
        private bool sceneTransitionRequested;
        private bool sceneChangedObserved;
        private bool runPass;
        private int originalTimeScaleBits;
        private int warmupLateUpdates;
        private int stableCaptureCount;
        private int targetSceneLateUpdates;
        private long visualTick;
        private long fixedTick;
        private float startedAtRealtime;
        private float attachedAtRealtime;
        private string stopReason = "NotStopped";

        private RuntimeSnapshotProbeExperiment(
            string sessionDirectory,
            SnapshotProbeOptions options,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            this.options = options;
            this.logInfo = logInfo;
            this.logDebug = logDebug;
            this.logWarning = logWarning;
            this.logError = logError;
            outputDirectory = Path.Combine(
                sessionDirectory,
                "state",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
            inputClock = new InControlClockProbe((_, __) => { });
            sceneTracker = new SceneEpochTracker(
                target => logDebug("T05 scene-load-requested target=" + target),
                OnActiveSceneChanged);
        }

        public string OutputDirectory => outputDirectory;
        public bool IsStopped => stopped;

        public static RuntimeSnapshotProbeExperiment? TryStart(
            string sessionDirectory,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            var parse = SnapshotProbeOptions.Parse(Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T05 state probe arguments: " + parse.Error);
            }

            var experiment = new RuntimeSnapshotProbeExperiment(
                sessionDirectory,
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
                Stop(false, "ApplicationQuit", "Application quit before completion.");
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
                        Stop(false, "StartupTimeout", "A playable Hero was not available.");
                    }

                    return;
                }

                if (Time.realtimeSinceStartup - attachedAtRealtime > 120f)
                {
                    Stop(false, "RunTimeout", "State probe exceeded 120 seconds.");
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        internal void OnFixedUpdate()
        {
            if (!stopped)
            {
                fixedTick++;
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
                switch (options.Profile)
                {
                    case SnapshotProbeProfile.Stable:
                        AdvanceStableProfile();
                        break;
                    case SnapshotProbeProfile.Health:
                        AdvanceHealthProfile();
                        break;
                    case SnapshotProbeProfile.Scene:
                        AdvanceSceneProfile();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
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
            inputClock.Start();
            sceneTracker.Start();
            var gameObject = new GameObject("HollowKnightTAS.RuntimeSnapshotProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<RuntimeSnapshotProbeRunner>();
            runner.Initialize(this);
            logInfo(
                "T05 state probe created profile="
                + options.ProfileId
                + " runId="
                + options.RunId);
        }

        private void TryAttach()
        {
            var hero = HeroController.SilentInstance;
            var gameManager = GameManager.instance;
            if (hero == null
                || gameManager == null
                || gameManager.gameState != GameState.PLAYING
                || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            attached = true;
            attachedAtRealtime = Time.realtimeSinceStartup;
            visualTick = 0;
            fixedTick = 0;
            sceneTracker.ResetForRecording();
            WriteAtomic(
                Path.Combine(outputDirectory, "probe.ready"),
                new UTF8Encoding(false).GetBytes(
                    options.ProfileId + " " + options.RunId + "\n"));
            logDebug(
                "T05 state probe attached scene="
                + sceneTracker.CurrentScene
                + " inputTick="
                + inputClock.CurrentTick.ToString(CultureInfo.InvariantCulture));
        }

        private void AdvanceStableProfile()
        {
            if (!PrepareFrozenState())
            {
                return;
            }

            var result = CaptureCurrent();
            RequireSuccess(result, "stable capture " + stableCaptureCount);
            if (baseline == null)
            {
                baseline = result;
                WriteSnapshot("stable-baseline.snapshot", result);
            }

            stableHashes.Add(result.Sha256!);
            stableCaptureCount++;
            if (stableCaptureCount < 100)
            {
                return;
            }

            var distinctHashes = stableHashes
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctHashes.Length != 1)
            {
                throw new InvalidOperationException(
                    "The 100 frozen captures produced "
                    + distinctHashes.Length
                    + " distinct hashes.");
            }

            RunHeroXMutation();
            RunInjectedFailure();
            runPass = true;
            Stop(true, "Completed", string.Empty);
        }

        private void AdvanceHealthProfile()
        {
            if (!PrepareFrozenState())
            {
                return;
            }

            var playerData = PlayerData.instance
                             ?? throw new InvalidOperationException(
                                 "PlayerData.instance is unavailable.");
            if (playerData.health <= 0)
            {
                throw new InvalidOperationException(
                    "Health mutation requires positive current health.");
            }

            var before = CaptureCurrent();
            RequireSuccess(before, "health baseline");
            var originalHealth = playerData.health;
            SnapshotCaptureResult after;
            try
            {
                playerData.TakeHealth(1);
                after = CaptureCurrent();
                RequireSuccess(after, "health changed");
            }
            finally
            {
                playerData.health = originalHealth;
            }

            var diff = SemanticSnapshotDiffer.Compare(
                before.Snapshot!,
                after.Snapshot!);
            WriteSnapshot("health-before.snapshot", before);
            WriteSnapshot("health-after.snapshot", after);
            WriteDiff("health.diff.json", diff);
            if (!diff.Entries.Any(
                    value => string.Equals(
                        value.Key,
                        "player.health",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Health mutation diff did not contain player.health.");
            }

            runPass = true;
            Stop(true, "Completed", string.Empty);
        }

        private void AdvanceSceneProfile()
        {
            if (!sceneTransitionRequested)
            {
                baseline = CaptureCurrent();
                RequireSuccess(baseline, "scene baseline");
                WriteSnapshot("scene-before.snapshot", baseline);
                sceneTransitionRequested = true;
                GameManager.instance.BeginSceneTransition(
                    new GameManager.SceneLoadInfo
                    {
                        AlwaysUnloadUnusedAssets = true,
                        EntryGateName = "door_dreamEnter",
                        EntryDelay = 0f,
                        PreventCameraFadeOut = true,
                        WaitForSceneTransitionCameraFade = false,
                        SceneName = SceneTarget,
                        Visualization =
                            GameManager.SceneLoadVisualizations.GodsAndGlory
                    });
                return;
            }

            if (!sceneChangedObserved
                || !string.Equals(
                    USceneManager.GetActiveScene().name,
                    SceneTarget,
                    StringComparison.Ordinal)
                || GameManager.instance == null
                || GameManager.instance.gameState != GameState.PLAYING
                || HeroController.SilentInstance == null
                || !HeroController.SilentInstance.gameObject.activeInHierarchy)
            {
                return;
            }

            targetSceneLateUpdates++;
            if (targetSceneLateUpdates < 10)
            {
                return;
            }

            var after = CaptureCurrent();
            RequireSuccess(after, "scene changed");
            WriteSnapshot("scene-after.snapshot", after);
            var diff = SemanticSnapshotDiffer.Compare(
                baseline!.Snapshot!,
                after.Snapshot!);
            WriteDiff("scene.diff.json", diff);
            if (!diff.Entries.Any(
                    value => string.Equals(
                        value.Key,
                        "scene.name",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Scene transition diff did not contain scene.name.");
            }

            if (sceneTracker.CurrentEpoch <= 0)
            {
                throw new InvalidOperationException(
                    "Scene transition did not increment scene epoch.");
            }

            runPass = true;
            Stop(true, "Completed", string.Empty);
        }

        private bool PrepareFrozenState()
        {
            if (!frozen)
            {
                originalTimeScaleBits = SingleBits.FromSingle(Time.timeScale);
                Time.timeScale = 0f;
                frozen = true;
            }

            warmupLateUpdates++;
            return warmupLateUpdates >= 3;
        }

        private void RunHeroXMutation()
        {
            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "HeroController disappeared before X mutation.");
            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "Hero Rigidbody2D disappeared before X mutation.");
            var originalTransform = hero.gameObject.transform.position;
            var originalBody = body.position;
            SnapshotCaptureResult changed;
            try
            {
                var changedBody = new Vector2(originalBody.x + 0.25f, originalBody.y);
                body.position = changedBody;
                hero.gameObject.transform.position = new Vector3(
                    originalTransform.x + 0.25f,
                    originalTransform.y,
                    originalTransform.z);
                changed = CaptureCurrent();
                RequireSuccess(changed, "hero X changed");
            }
            finally
            {
                body.position = originalBody;
                hero.gameObject.transform.position = originalTransform;
            }

            WriteSnapshot("hero-x-after.snapshot", changed);
            var diff = SemanticSnapshotDiffer.Compare(
                baseline!.Snapshot!,
                changed.Snapshot!);
            WriteDiff("hero-x.diff.json", diff);
            if (diff.Entries.Count != 1
                || !string.Equals(
                    diff.Entries[0].Key,
                    "hero.position.x",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Controlled Hero X mutation must produce exactly one semantic diff.");
            }
        }

        private void RunInjectedFailure()
        {
            var failureCapture = new RuntimeSnapshotCapture(
                new ISemanticProbe[]
                {
                    new SceneProbe(),
                    new HeroProbe(),
                    new PlayerDataProbe(),
                    new ThrowingSemanticProbe()
                });
            var failure = failureCapture.Capture(CurrentStamp());
            if (failure.Success
                || failure.Snapshot != null
                || failure.CanonicalBytes != null
                || failure.Sha256 != null
                || !string.Equals(
                    failure.FailedProbeId,
                    ThrowingSemanticProbe.Identifier,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Injected capture failure generated state or omitted its probe ID.");
            }

            WriteCaptureFailure(failure);
            logWarning(
                "T05 capture-error probe="
                + failure.FailedProbeId
                + " error="
                + failure.Error);
        }

        private SnapshotCaptureResult CaptureCurrent()
        {
            return capture.Capture(CurrentStamp());
        }

        private TickStamp CurrentStamp()
        {
            return new TickStamp(
                inputClock.CurrentTick,
                visualTick,
                fixedTick,
                sceneTracker.CurrentEpoch,
                TickPhase.LateUpdateEnd);
        }

        private static void RequireSuccess(
            SnapshotCaptureResult result,
            string operation)
        {
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    operation
                    + " failed at "
                    + result.FailedProbeId
                    + ": "
                    + result.Error);
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current, int epoch)
        {
            if (string.Equals(current.name, SceneTarget, StringComparison.Ordinal))
            {
                sceneChangedObserved = true;
            }

            logDebug(
                "T05 scene-changed previous="
                + previous.name
                + " current="
                + current.name
                + " epoch="
                + epoch.ToString(CultureInfo.InvariantCulture));
        }

        private void Fail(Exception exception)
        {
            logError("T05 state probe failed: " + exception);
            Stop(false, exception.GetType().Name, exception.Message);
        }

        private void Stop(bool pass, string reason, string error)
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
                if (frozen)
                {
                    Time.timeScale = SingleBits.ToSingle(originalTimeScaleBits);
                    frozen = false;
                }

                inputClock.Dispose();
                sceneTracker.Dispose();
                WriteResult(error);
            }
            catch (Exception exception)
            {
                logError("T05 state probe cleanup failed: " + exception);
                runPass = false;
            }

            logInfo(
                "T05 state probe stopped profile="
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

        private void WriteSnapshot(string name, SnapshotCaptureResult result)
        {
            WriteAtomic(
                Path.Combine(outputDirectory, name),
                result.CanonicalBytes!);
            WriteAtomic(
                Path.Combine(outputDirectory, name + ".sha256"),
                new UTF8Encoding(false).GetBytes(result.Sha256 + "\n"));
        }

        private void WriteDiff(string name, SemanticDiff diff)
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            AppendBoolean(builder, "areEqual", diff.AreEqual);
            AppendNumber(builder, "count", diff.Entries.Count);
            AppendPropertyPrefix(builder, "entries");
            builder.Append('[');
            for (var index = 0; index < diff.Entries.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var entry = diff.Entries[index];
                builder.Append('{');
                AppendString(builder, "key", entry.Key);
                AppendString(builder, "expectedKind", entry.ExpectedKind?.ToString() ?? "missing");
                AppendString(builder, "actualKind", entry.ActualKind?.ToString() ?? "missing");
                AppendString(builder, "expectedBits", entry.ExpectedBits);
                AppendString(builder, "actualBits", entry.ActualBits);
                AppendString(builder, "expectedDisplay", entry.ExpectedDisplay);
                AppendString(builder, "actualDisplay", entry.ActualDisplay);
                builder.Append('}');
            }

            builder.Append(']');
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, name),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
        }

        private void WriteCaptureFailure(SnapshotCaptureResult failure)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendString(builder, "event", "capture-error");
            AppendString(builder, "probeId", failure.FailedProbeId ?? string.Empty);
            AppendString(builder, "error", failure.Error ?? string.Empty);
            AppendBoolean(builder, "snapshotPresent", failure.Snapshot != null);
            AppendBoolean(builder, "canonicalBytesPresent", failure.CanonicalBytes != null);
            AppendBoolean(builder, "sha256Present", failure.Sha256 != null);
            AppendStamp(builder, failure.Stamp);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "capture-failure.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
        }

        private void WriteResult(string error)
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "profile", options.ProfileId);
            AppendString(builder, "runId", options.RunId);
            AppendString(builder, "stopReason", stopReason);
            AppendString(builder, "error", error ?? string.Empty);
            AppendString(builder, "initialScene", baseline?.Snapshot?.Values["scene.name"].DisplayValue ?? string.Empty);
            AppendString(builder, "finalScene", USceneManager.GetActiveScene().name ?? string.Empty);
            AppendNumber(builder, "stableCaptureCount", stableCaptureCount);
            AppendNumber(builder, "distinctStableHashCount", stableHashes.Distinct(StringComparer.Ordinal).Count());
            AppendNumber(builder, "sceneEpoch", sceneTracker.CurrentEpoch);
            AppendBoolean(builder, "sceneChangedObserved", sceneChangedObserved);
            AppendBoolean(builder, "timeScaleRestored", !frozen);
            AppendBoolean(builder, "runPass", runPass);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
            WriteAtomic(
                Path.Combine(outputDirectory, "verdict.md"),
                new UTF8Encoding(false).GetBytes(
                    "# T05 State Probe Verdict\n\n"
                    + "- Profile: `" + options.ProfileId + "`\n"
                    + "- Run: `" + options.RunId + "`\n"
                    + "- Stop reason: `" + stopReason + "`\n"
                    + "- Stable captures: " + stableCaptureCount.ToString(CultureInfo.InvariantCulture) + "\n"
                    + "- Scene epoch: " + sceneTracker.CurrentEpoch.ToString(CultureInfo.InvariantCulture) + "\n"
                    + "- Run pass: " + runPass + "\n"));
        }

        private static void AppendStamp(StringBuilder builder, TickStamp stamp)
        {
            AppendNumber(builder, "inputTick", unchecked((long)stamp.InputTick));
            AppendNumber(builder, "visualTick", stamp.VisualTick);
            AppendNumber(builder, "fixedTick", stamp.FixedTick);
            AppendNumber(builder, "sceneEpoch", stamp.SceneEpoch);
            AppendString(builder, "phase", stamp.Phase.ToString());
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
            var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
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

        private sealed class ThrowingSemanticProbe : ISemanticProbe
        {
            public const string Identifier = "injected-failure";
            public string ProbeId => Identifier;

            public void Capture(SemanticSnapshotBuilder builder)
            {
                throw new InvalidOperationException(
                    "Deterministic T05 injected failure.");
            }
        }
    }

    [DefaultExecutionOrder(-32000)]
    internal sealed class RuntimeSnapshotProbeRunner : MonoBehaviour
    {
        private RuntimeSnapshotProbeExperiment? owner;

        internal void Initialize(RuntimeSnapshotProbeExperiment value)
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

    internal enum SnapshotProbeProfile
    {
        Stable,
        Health,
        Scene
    }

    internal sealed class SnapshotProbeOptions
    {
        private SnapshotProbeOptions(
            SnapshotProbeProfile profile,
            string runId,
            int autoLoadSlot,
            bool exitOnComplete)
        {
            Profile = profile;
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
            ExitOnComplete = exitOnComplete;
        }

        public SnapshotProbeProfile Profile { get; }
        public string ProfileId => Profile.ToString().ToUpperInvariant();
        public string RunId { get; }
        public int AutoLoadSlot { get; }
        public bool ExitOnComplete { get; }

        public static SnapshotProbeParseResult Parse(string[] arguments)
        {
            var profileText = ReadValue(arguments, "--hktas-state-probe=");
            if (profileText == null)
            {
                return SnapshotProbeParseResult.NotRequested();
            }

            if (!Enum.TryParse(
                    profileText,
                    true,
                    out SnapshotProbeProfile profile))
            {
                return SnapshotProbeParseResult.Invalid(
                    "profile must be STABLE, HEALTH, or SCENE");
            }

            var runId = ReadValue(arguments, "--hktas-state-probe-run=")
                        ?? profileText.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!IsSafeIdentifier(runId))
            {
                return SnapshotProbeParseResult.Invalid(
                    "run id must contain only letters, digits, dot, dash, or underscore");
            }

            var slotText = ReadValue(arguments, "--hktas-state-probe-slot=") ?? "2";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return SnapshotProbeParseResult.Invalid("slot must be in [1, 4]");
            }

            return SnapshotProbeParseResult.Valid(
                new SnapshotProbeOptions(
                    profile,
                    runId,
                    slot,
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-state-probe-exit",
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

    internal sealed class SnapshotProbeParseResult
    {
        private SnapshotProbeParseResult(
            bool requested,
            SnapshotProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public SnapshotProbeOptions? Options { get; }
        public string? Error { get; }

        public static SnapshotProbeParseResult NotRequested()
        {
            return new SnapshotProbeParseResult(false, null, null);
        }

        public static SnapshotProbeParseResult Valid(SnapshotProbeOptions options)
        {
            return new SnapshotProbeParseResult(true, options, null);
        }

        public static SnapshotProbeParseResult Invalid(string error)
        {
            return new SnapshotProbeParseResult(true, null, error);
        }
    }
}
