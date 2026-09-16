using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Rng;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Core.Verification;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;
using InControl;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Rng
{
    public sealed class RuntimeRngProbeExperiment : IDisposable
    {
        private const string RouteScene = "GG_Vengefly";
        private const int MatchingSeed = 123456789;
        private const int DivergentSeed = 987654321;

        private readonly RuntimeRngProbe rngProbe;
        private readonly RuntimeReplayJournal journal;
        private readonly RngProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly string outputDirectory;
        private readonly RuntimeSnapshotCapture snapshotCapture =
            new RuntimeSnapshotCapture();
        private readonly List<RngProbeMilestone> milestones =
            new List<RngProbeMilestone>();
        private RuntimeRngProbeExperimentRunner? runner;
        private UnityEngine.Random.State originalRandomState;
        private RngStateFingerprint originalFingerprint;
        private UnityRandomCodecRoundTripReport? codecReport;
        private IReadOnlyDictionary<string, long>? baselineCallSiteCounts;
        private long baselineGlobalCallCount;
        private long controlledEndGlobalCallCount;
        private long routeStartMovieTick = -1;
        private long visualTick;
        private long fixedTick;
        private int sceneEpoch;
        private int routeWarmupLateUpdates;
        private int originalHealth;
        private bool loadRequested;
        private bool sceneRequested;
        private bool routeStarted;
        private bool helperIssued;
        private bool damageIssued;
        private bool semanticDivergenceIssued;
        private bool randomStateCaptured;
        private bool healthCaptured;
        private bool cleanupRandomEquivalent;
        private bool cleanupHealthEquivalent;
        private bool stopped;
        private bool exitRequested;
        private bool exitIssued;
        private bool runPass;
        private float startedAtRealtime;
        private string stopReason = "NotStopped";
        private string error = string.Empty;

        private RuntimeRngProbeExperiment(
            string sessionDirectory,
            RuntimeRngProbe rngProbe,
            RuntimeReplayJournal journal,
            RngProbeOptions options,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError)
        {
            this.rngProbe = rngProbe;
            this.journal = journal;
            this.options = options;
            this.logInfo = logInfo;
            this.logWarning = logWarning;
            this.logError = logError;
            outputDirectory = Path.Combine(
                Path.GetFullPath(sessionDirectory),
                "rng",
                "probe",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
        }

        public static RuntimeRngProbeExperiment? TryStart(
            string sessionDirectory,
            RuntimeRngProbe? rngProbe,
            RuntimeReplayJournal journal,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError)
        {
            var parse = RngProbeOptions.Parse(
                Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T10 RNG probe arguments: " + parse.Error);
            }

            if (rngProbe == null)
            {
                throw new InvalidOperationException(
                    "T10 RNG probe requested but diagnostics did not start.");
            }

            rngProbe.EnableDiagnostics();

            var experiment = new RuntimeRngProbeExperiment(
                sessionDirectory,
                rngProbe,
                journal,
                parse.Options,
                logInfo,
                logWarning,
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
                    "Application quit before T10 completion.");
            }

            USceneManager.activeSceneChanged -= OnActiveSceneChanged;
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
                var elapsed =
                    Time.realtimeSinceStartup - startedAtRealtime;
                if (elapsed > 150f)
                {
                    Finish(
                        false,
                        "RunTimeout",
                        "T10 RNG probe exceeded 150 seconds.");
                    return;
                }

                if (!loadRequested
                    && elapsed >= 2f
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

        internal void OnFixedUpdate()
        {
            fixedTick++;
        }

        internal void OnLateUpdate()
        {
            if (stopped)
            {
                return;
            }

            try
            {
                if (!routeStarted)
                {
                    return;
                }

                var localTick = checked(
                    journal.LastCommittedMovieTick
                    - routeStartMovieTick);
                if (localTick >= 60
                    && milestones.All(
                        value => !string.Equals(
                            value.Id,
                            "endpoint",
                            StringComparison.Ordinal)))
                {
                    CaptureMilestone("endpoint", localTick);
                    var pass = EvaluatePass();
                    Finish(
                        pass,
                        pass ? "Completed" : "GateFailed",
                        pass
                            ? string.Empty
                            : "T10 runtime acceptance conditions failed.");
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
            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeRngProbeExperiment");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner =
                gameObject.AddComponent<
                    RuntimeRngProbeExperimentRunner>();
            runner.Initialize(this);
            logInfo(
                "T10 RNG probe created profile="
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
                || !hero.gameObject.activeInHierarchy
                || journal.LastCommittedMovieTick < 0)
            {
                return;
            }

            var scene = USceneManager.GetActiveScene().name;
            if (!string.Equals(
                    scene,
                    RouteScene,
                    StringComparison.Ordinal))
            {
                if (!sceneRequested)
                {
                    sceneRequested = true;
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
                                GameManager.SceneLoadVisualizations
                                    .GodsAndGlory
                        });
                }

                return;
            }

            routeWarmupLateUpdates++;
            if (routeWarmupLateUpdates < 30)
            {
                return;
            }

            BeginRoute();
        }

        private void BeginRoute()
        {
            if (!rngProbe.StateAvailable
                || !rngProbe.Capability.HooksAvailable)
            {
                throw new InvalidOperationException(
                    "Target RNG codec or whitelist did not resolve exactly.");
            }

            codecReport = rngProbe.VerifyCodecRoundTrip(0x13579BDF);
            if (!codecReport.Success)
            {
                throw new InvalidOperationException(
                    "Unity RNG codec round-trip self-test failed.");
            }

            originalRandomState = UnityEngine.Random.state;
            originalFingerprint = rngProbe.CaptureCurrent();
            randomStateCaptured = true;
            originalHealth = PlayerData.instance.health;
            healthCaptured = true;
            if (options.Profile == RngProbeProfile.NoHook)
            {
                rngProbe.DisableHooksForDiagnosticControl();
            }

            baselineGlobalCallCount = rngProbe.GlobalCallCount;
            baselineCallSiteCounts =
                rngProbe.SnapshotCallSiteCounts();
            UnityEngine.Random.InitState(
                options.Profile == RngProbeProfile.Diverge
                    ? DivergentSeed
                    : MatchingSeed);
            routeStartMovieTick = journal.LastCommittedMovieTick;
            routeStarted = true;
            CaptureMilestone("seeded-start", 0);
            helperIssued = true;
            Helper.GetRandomVector2InRange(
                new Vector2(-3f, -2f),
                new Vector2(5f, 7f));
            CaptureMilestone("after-helper", 0);
            damageIssued = true;
            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "Hero disappeared before damage call.");
            hero.TakeDamage(
                null,
                CollisionSide.bottom,
                1,
                1);
            CaptureMilestone("after-damage", 0);
            CaptureMilestone("pre-semantic-divergence", 0);
            semanticDivergenceIssued = true;
            if (options.Profile == RngProbeProfile.Diverge)
            {
                PlayerData.instance.health =
                    PlayerData.instance.health == 0
                        ? 1
                        : PlayerData.instance.health - 1;
            }

            CaptureMilestone("semantic-divergence", 0);
            controlledEndGlobalCallCount = rngProbe.GlobalCallCount;
        }

        private void CaptureMilestone(string id, long localTick)
        {
            var capture = snapshotCapture.Capture(
                new TickStamp(
                    InputManager.CurrentTick,
                    visualTick,
                    fixedTick,
                    sceneEpoch,
                    TickPhase.LateUpdateEnd));
            if (!capture.Success || capture.CanonicalBytes == null)
            {
                throw new InvalidOperationException(
                    "T10 semantic capture failed at "
                    + id
                    + ": "
                    + capture.FailedProbeId
                    + " "
                    + capture.Error);
            }

            var normalized =
                VerificationSnapshotNormalizer.Normalize(
                    capture.CanonicalBytes);
            var semantic = SemanticSnapshotCanonicalizer.Deserialize(
                normalized);
            var counts = RelativeCounts(
                rngProbe.SnapshotCallSiteCounts());
            milestones.Add(
                new RngProbeMilestone(
                    id,
                    localTick,
                    rngProbe.CaptureCurrent().Sha256,
                    SemanticSnapshotHasher.ComputeSha256(semantic),
                    checked(
                        rngProbe.GlobalCallCount
                        - baselineGlobalCallCount),
                    counts,
                    USceneManager.GetActiveScene().name));
        }

        private bool EvaluatePass()
        {
            var calls = ProbeCalls(controlledOnly: true);
            var helperCount = calls.Count(
                value => string.Equals(
                    value.CallSiteId,
                    "helper.random-vector2",
                    StringComparison.Ordinal));
            var damageCount = calls.Count(
                value => string.Equals(
                    value.CallSiteId,
                    "hero.take-damage",
                    StringComparison.Ordinal));
            var hookExpectation = options.Profile
                                  == RngProbeProfile.NoHook
                ? calls.Count == 0
                : helperCount >= 1 && damageCount >= 1;
            return codecReport?.Success == true
                   && rngProbe.Capability.Codec.Ready
                   && rngProbe.Capability.CallSites.Ready
                   && string.IsNullOrEmpty(rngProbe.Fault)
                   && rngProbe.StateRecordsDropped == 0
                   && rngProbe.CallRecordsDropped == 0
                   && milestones.Count == 6
                   && helperIssued
                   && damageIssued
                   && semanticDivergenceIssued
                   && hookExpectation;
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
            RestoreProbeState();
            runPass = success
                      && cleanupRandomEquivalent
                      && cleanupHealthEquivalent;
            if (success && !runPass)
            {
                stopReason = "CleanupMismatch";
                error =
                    "RNG state or PlayerData health was not restored exactly.";
            }
            WriteResult();
            stopped = true;
            exitRequested = options.ExitOnComplete;
            WriteAtomic(
                Path.Combine(outputDirectory, "probe.done"),
                new UTF8Encoding(false).GetBytes(
                    (runPass ? "PASS" : "FAIL")
                    + " "
                    + stopReason
                    + "\n"));
            logInfo(
                "T10 RNG probe stopped profile="
                + options.ProfileId
                + " pass="
                + runPass);
        }

        private void Fail(Exception exception)
        {
            logError("T10 RNG probe failed: " + exception);
            Finish(
                false,
                "Exception",
                exception.GetType().Name + ": " + exception.Message);
        }

        private void RestoreProbeState()
        {
            if (healthCaptured)
            {
                PlayerData.instance.health = originalHealth;
                cleanupHealthEquivalent =
                    PlayerData.instance.health == originalHealth;
            }

            if (randomStateCaptured)
            {
                UnityEngine.Random.state = originalRandomState;
                cleanupRandomEquivalent =
                    originalFingerprint.Equals(
                        rngProbe.CaptureCurrent());
            }
        }

        private IReadOnlyList<RngCallRecord> ProbeCalls(
            bool controlledOnly)
        {
            return rngProbe.SnapshotCalls()
                .Where(
                    value => value.GlobalCallIndex
                             > baselineGlobalCallCount
                             && (!controlledOnly
                                 || value.GlobalCallIndex
                                 <= controlledEndGlobalCallCount))
                .ToList();
        }

        private IReadOnlyDictionary<string, long> RelativeCounts(
            IReadOnlyDictionary<string, long> current)
        {
            var result = new SortedDictionary<string, long>(
                StringComparer.Ordinal);
            foreach (var item in current)
            {
                var baseline = baselineCallSiteCounts != null
                               && baselineCallSiteCounts.TryGetValue(
                                   item.Key,
                                   out var value)
                    ? value
                    : 0;
                result.Add(item.Key, checked(item.Value - baseline));
            }

            return result;
        }

        private void WriteResult()
        {
            var calls = ProbeCalls(controlledOnly: true);
            var endpoint = milestones.LastOrDefault(
                value => string.Equals(
                    value.Id,
                    "endpoint",
                    StringComparison.Ordinal));
            var rngTrace = ComputeTraceHash(
                milestones
                    .Where(
                        value => !string.Equals(
                            value.Id,
                            "endpoint",
                            StringComparison.Ordinal))
                    .Select(
                    value => value.Id
                             + "|"
                             + value.LocalMovieTick
                             + "|"
                             + value.RngStateSha256));
            var semanticTrace = ComputeTraceHash(
                milestones.Select(
                    value => value.Id
                             + "|"
                             + value.LocalMovieTick
                             + "|"
                             + value.SemanticSha256));
            var callTrace = ComputeTraceHash(
                calls.Select(
                    value => value.CallSiteId
                             + "|"
                             + (value.GlobalCallIndex
                                - baselineGlobalCallCount)
                             + "|"
                             + value.BeforeStateSha256
                             + "|"
                             + value.AfterStateSha256));
            var builder = new StringBuilder(32768);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "profile", options.ProfileId);
            AppendString(builder, "runId", options.RunId);
            AppendBoolean(builder, "runPass", runPass);
            AppendString(builder, "stopReason", stopReason);
            AppendString(builder, "error", error);
            AppendString(builder, "codecId", rngProbe.CodecId);
            AppendString(builder, "coverage", rngProbe.CoverageId);
            AppendString(
                builder,
                "codecStatus",
                rngProbe.Capability.Codec.Status.ToString());
            AppendString(
                builder,
                "callSiteStatus",
                rngProbe.Capability.CallSites.Status.ToString());
            AppendBoolean(
                builder,
                "hooksAttachedAtEnd",
                rngProbe.HooksAttached);
            AppendBoolean(
                builder,
                "codecStableOneHundred",
                codecReport?.StableOneHundredEncodes == true);
            AppendBoolean(
                builder,
                "codecSeedChangedHash",
                codecReport?.SeedChangedHash == true);
            AppendBoolean(
                builder,
                "codecRestoredExactly",
                codecReport?.RestoredExactly == true);
            AppendBoolean(
                builder,
                "cleanupRandomEquivalent",
                cleanupRandomEquivalent);
            AppendBoolean(
                builder,
                "cleanupHealthEquivalent",
                cleanupHealthEquivalent);
            AppendString(builder, "rngTraceSha256", rngTrace);
            AppendString(
                builder,
                "ambientEndpointRngSha256",
                endpoint?.RngStateSha256 ?? string.Empty);
            AppendString(
                builder,
                "semanticTraceSha256",
                semanticTrace);
            AppendString(builder, "callTraceSha256", callTrace);
            AppendNumber(
                builder,
                "globalCallDelta",
                checked(
                    controlledEndGlobalCallCount
                    - baselineGlobalCallCount));
            AppendString(
                builder,
                "fault",
                rngProbe.Fault);
            AppendNumber(
                builder,
                "stateRecordsDropped",
                rngProbe.StateRecordsDropped);
            AppendNumber(
                builder,
                "callRecordsDropped",
                rngProbe.CallRecordsDropped);
            builder.Append(",\"limitations\":{");
            AppendString(builder, "unityRandomState", "captured");
            AppendString(builder, "whitelistedMethods", "2");
            AppendString(builder, "systemRandom", "not-covered");
            AppendString(builder, "otherModRng", "not-covered");
            builder.Append('}');
            builder.Append(",\"milestones\":[");
            for (var index = 0; index < milestones.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var value = milestones[index];
                builder.Append('{');
                AppendString(builder, "id", value.Id);
                AppendNumber(
                    builder,
                    "localMovieTick",
                    value.LocalMovieTick);
                AppendString(
                    builder,
                    "rngStateSha256",
                    value.RngStateSha256);
                AppendString(
                    builder,
                    "semanticSha256",
                    value.SemanticSha256);
                AppendNumber(
                    builder,
                    "globalCallDelta",
                    value.GlobalCallDelta);
                AppendString(
                    builder,
                    "callSiteCounts",
                    FormatCounts(value.CallSiteCounts));
                AppendString(builder, "scene", value.SceneName);
                builder.Append('}');
            }

            builder.Append(']');
            builder.Append(",\"calls\":[");
            for (var index = 0; index < calls.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var value = calls[index];
                builder.Append('{');
                AppendString(
                    builder,
                    "callSiteId",
                    value.CallSiteId);
                AppendNumber(
                    builder,
                    "relativeGlobalCallIndex",
                    checked(
                        value.GlobalCallIndex
                        - baselineGlobalCallCount));
                AppendString(
                    builder,
                    "beforeStateSha256",
                    value.BeforeStateSha256);
                AppendString(
                    builder,
                    "afterStateSha256",
                    value.AfterStateSha256);
                AppendBoolean(
                    builder,
                    "stateChanged",
                    value.StateChanged);
                builder.Append('}');
            }

            builder.Append("]}");
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                new UTF8Encoding(false, true).GetBytes(
                    builder.ToString()));
            WriteAtomic(
                Path.Combine(outputDirectory, "verdict.md"),
                new UTF8Encoding(false).GetBytes(
                    "# T10 RNG Probe\n\n"
                    + "- Profile: `" + options.ProfileId + "`\n"
                    + "- Codec: `" + rngProbe.CodecId + "`\n"
                    + "- Coverage: `" + rngProbe.CoverageId + "`\n"
                    + "- RNG trace: `" + rngTrace + "`\n"
                    + "- Semantic trace: `" + semanticTrace + "`\n"
                    + "- Calls: `" + calls.Count + "`\n"
                    + "- Pass: `" + runPass + "`\n"));
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            sceneEpoch++;
            if (string.Equals(
                    current.name,
                    RouteScene,
                    StringComparison.Ordinal))
            {
                routeWarmupLateUpdates = 0;
            }
        }

        private static string ComputeTraceHash(
            IEnumerable<string> values)
        {
            return Sha256Utility.ComputeUtf8Hex(
                string.Join("\n", values) + "\n");
        }

        private static string FormatCounts(
            IReadOnlyDictionary<string, long> values)
        {
            return string.Join(
                ",",
                values
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(
                        item => item.Key
                                + "="
                                + item.Value.ToString(
                                    CultureInfo.InvariantCulture)));
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            CanonicalJsonWriter.AppendString(
                builder,
                value ?? string.Empty);
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

    internal sealed class RngProbeMilestone
    {
        public RngProbeMilestone(
            string id,
            long localMovieTick,
            string rngStateSha256,
            string semanticSha256,
            long globalCallDelta,
            IReadOnlyDictionary<string, long> callSiteCounts,
            string sceneName)
        {
            Id = id;
            LocalMovieTick = localMovieTick;
            RngStateSha256 = rngStateSha256;
            SemanticSha256 = semanticSha256;
            GlobalCallDelta = globalCallDelta;
            CallSiteCounts = callSiteCounts;
            SceneName = sceneName;
        }

        public string Id { get; }
        public long LocalMovieTick { get; }
        public string RngStateSha256 { get; }
        public string SemanticSha256 { get; }
        public long GlobalCallDelta { get; }
        public IReadOnlyDictionary<string, long> CallSiteCounts { get; }
        public string SceneName { get; }
    }

    internal enum RngProbeProfile : byte
    {
        Match = 1,
        NoHook = 2,
        Diverge = 3
    }

    internal sealed class RngProbeOptions
    {
        private RngProbeOptions(
            RngProbeProfile profile,
            string runId,
            int autoLoadSlot,
            bool exitOnComplete)
        {
            Profile = profile;
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
            ExitOnComplete = exitOnComplete;
        }

        public RngProbeProfile Profile { get; }
        public string ProfileId => Profile.ToString().ToUpperInvariant();
        public string RunId { get; }
        public int AutoLoadSlot { get; }
        public bool ExitOnComplete { get; }

        public static RngProbeParseResult Parse(string[] arguments)
        {
            var profileText = ReadValue(
                arguments,
                "--hktas-rng-probe=");
            if (profileText == null)
            {
                return RngProbeParseResult.NotRequested();
            }

            if (!Enum.TryParse(
                    profileText,
                    true,
                    out RngProbeProfile profile))
            {
                return RngProbeParseResult.Invalid(
                    "profile must be MATCH, NOHOOK, or DIVERGE");
            }

            var runId = ReadValue(
                            arguments,
                            "--hktas-rng-probe-run=")
                        ?? profileText.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!IsIdentifier(runId))
            {
                return RngProbeParseResult.Invalid(
                    "run ID contains unsupported characters");
            }

            var slotText = ReadValue(
                               arguments,
                               "--hktas-rng-probe-slot=")
                           ?? "2";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return RngProbeParseResult.Invalid(
                    "slot must be in [1, 4]");
            }

            return RngProbeParseResult.Valid(
                new RngProbeOptions(
                    profile,
                    runId,
                    slot,
                    arguments.Any(
                        value => string.Equals(
                            value,
                            "--hktas-rng-probe-exit",
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

    internal sealed class RngProbeParseResult
    {
        private RngProbeParseResult(
            bool requested,
            RngProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public RngProbeOptions? Options { get; }
        public string? Error { get; }

        public static RngProbeParseResult NotRequested()
        {
            return new RngProbeParseResult(false, null, null);
        }

        public static RngProbeParseResult Valid(
            RngProbeOptions options)
        {
            return new RngProbeParseResult(true, options, null);
        }

        public static RngProbeParseResult Invalid(string error)
        {
            return new RngProbeParseResult(true, null, error);
        }
    }

    internal sealed class RuntimeRngProbeExperimentRunner :
        MonoBehaviour
    {
        private RuntimeRngProbeExperiment? owner;

        public void Initialize(RuntimeRngProbeExperiment value)
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
}
