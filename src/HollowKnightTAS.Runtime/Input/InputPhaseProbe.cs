using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using HollowKnightTAS.Core.Diagnostics;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Serialization;
using InControl;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Input
{
    public sealed class InputPhaseProbe : IDisposable
    {
        private const int SchemaVersion = 1;
        private readonly string sessionId;
        private readonly InputProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logDebug;
        private readonly Action<string> logWarning;
        private readonly Action<string> logError;
        private readonly IReadOnlyList<InputSample> fixture;
        private readonly string outputDirectory;
        private readonly JsonLinesEventSink eventSink;
        private readonly Dictionary<ulong, InputSample> expectedByActualTick = new();
        private readonly Dictionary<ulong, int> heroCountsByActualTick = new();
        private readonly HashSet<ulong> heroMismatchTicks = new();
        private readonly List<ulong> actualTicksByLogicalIndex = new();

        private InputPhaseProbeRunner? runner;
        private EmergencyActionSet? emergencyActions;
        private HeroInputAdapterBase? adapter;
        private BindingRestoreReport? restoreReport;
        private HeroController? attachedHero;
        private bool originalAcceptingInput;
        private bool hooksRegistered;
        private bool attached;
        private bool stopped;
        private bool exitRequested;
        private bool exitIssued;
        private bool loadRequested;
        private bool sceneChangeRequested;
        private bool completionPending;
        private bool physicalNoiseDetected;
        private string attachedScene = string.Empty;
        private string stopReason = "NotStopped";
        private float startedAtRealtime;
        private float attachedAtRealtime;
        private long sequence;
        private int pendingLogicalIndex = -1;
        private int nextLogicalIndex;
        private int inputObservationCount;
        private int inputMismatchCount;
        private int heroObservationCount;
        private int heroMismatchCount;

        private InputPhaseProbe(
            string sessionId,
            string sessionDirectory,
            InputProbeOptions options,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            this.sessionId = sessionId;
            this.options = options;
            this.logInfo = logInfo;
            this.logDebug = logDebug;
            this.logWarning = logWarning;
            this.logError = logError;
            fixture = InputFixture.CreateFixedEdgeV1();
            outputDirectory = Path.Combine(
                sessionDirectory,
                "input-phase",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
            eventSink = new JsonLinesEventSink(
                Path.Combine(outputDirectory, "phase-events.jsonl"),
                8192,
                TimeSpan.FromSeconds(2),
                budgetDirectory: Path.GetDirectoryName(sessionDirectory));
        }

        public string OutputDirectory => outputDirectory;
        public bool IsStopped => stopped;

        public static InputPhaseProbe? TryStart(
            string sessionId,
            string sessionDirectory,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logWarning,
            Action<string> logError)
        {
            var parse = InputProbeOptions.Parse(Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid T02 input probe arguments: " + parse.Error);
            }

            var probe = new InputPhaseProbe(
                sessionId,
                sessionDirectory,
                parse.Options,
                logInfo,
                logDebug,
                logWarning,
                logError);
            probe.Start();
            return probe;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                Stop("ApplicationQuit");
            }

            if (runner != null && !exitIssued)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        internal void OnVisualUpdate(long visualFrame, long fixedStep)
        {
            if (exitRequested && !exitIssued)
            {
                exitIssued = true;
                logDebug("T02 probe requested a normal application exit.");
                Application.Quit();
                return;
            }

            if (stopped)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - startedAtRealtime;
            if (!attached && elapsed > 90f)
            {
                Stop("StartupTimeout");
                return;
            }

            if (attached
                && completionPending
                && Time.realtimeSinceStartup - attachedAtRealtime > 15f)
            {
                Stop("HeroObservationTimeout");
                return;
            }

            if (!loadRequested
                && options.AutoLoadSlot > 0
                && elapsed >= 2f
                && string.Equals(
                    USceneManager.GetActiveScene().name,
                    "Menu_Title",
                    StringComparison.Ordinal)
                && GameManager.instance != null)
            {
                loadRequested = true;
                Emit(
                    "load-requested",
                    Fields(
                        ("slot", options.AutoLoadSlot.ToString(CultureInfo.InvariantCulture)),
                        ("visualFrame", visualFrame.ToString(CultureInfo.InvariantCulture))));
                GameManager.instance.LoadGameFromUI(options.AutoLoadSlot);
                return;
            }

            if (!attached)
            {
                TryAttach();
                return;
            }

            if (options.CaseName == InputProbeOptions.SceneChangeCase
                && !sceneChangeRequested
                && nextLogicalIndex >= 20
                && GameManager.instance != null)
            {
                sceneChangeRequested = true;
                Emit(
                    "scene-change-requested",
                    Fields(("logicalTick", nextLogicalIndex.ToString(CultureInfo.InvariantCulture))));
                runner?.StartCoroutine(
                    GameManager.instance.ReturnToMainMenu(
                        GameManager.ReturnToMainMenuSaveModes.DontSave));
            }
        }

        private void Start()
        {
            startedAtRealtime = Time.realtimeSinceStartup;
            emergencyActions = new EmergencyActionSet();
            InputManager.OnUpdate += OnInputManagerUpdated;
            ModHooks.HeroUpdateHook += OnHeroUpdate;
            USceneManager.activeSceneChanged += OnActiveSceneChanged;
            if (options.CandidateId == "B")
            {
                On.InControl.InputManager.UpdateInternal += OnInputManagerUpdateInternal;
            }

            hooksRegistered = true;
            var gameObject = new GameObject("HollowKnightTAS.InputPhaseProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<InputPhaseProbeRunner>();
            runner.Initialize(this);

            Emit(
                "probe-created",
                Fields(
                    ("candidate", options.CandidateId),
                    ("case", options.CaseName),
                    ("fixture", InputFixture.FixedEdgeV1Name),
                    ("runId", options.RunId)));
            logInfo(
                "T02 input phase probe created candidate="
                + options.CandidateId
                + " case="
                + options.CaseName
                + " runId="
                + options.RunId);
        }

        private void TryAttach()
        {
            var hero = HeroController.instance;
            var inputHandler = InputHandler.Instance;
            var gameManager = GameManager.instance;
            if (hero == null
                || inputHandler == null
                || inputHandler.inputActions == null
                || gameManager == null
                || gameManager.gameState != GameState.PLAYING
                || !hero.gameObject.activeInHierarchy)
            {
                return;
            }

            attachedHero = hero;
            originalAcceptingInput = hero.acceptingInput;
            if (options.FreezeHero)
            {
                hero.acceptingInput = false;
            }

            adapter = CreateAdapter(options.CandidateId);
            try
            {
                adapter.Attach(inputHandler.inputActions);
                attached = true;
                attachedAtRealtime = Time.realtimeSinceStartup;
                attachedScene = USceneManager.GetActiveScene().name;
                WriteAtomic(
                    Path.Combine(outputDirectory, "binding-before.json"),
                    SerializeBindingSnapshots(adapter.BindingBefore));
                WriteAtomic(
                    Path.Combine(outputDirectory, "probe.ready"),
                    new UTF8Encoding(false).GetBytes(
                        options.CandidateId + " " + options.CaseName + "\n"));
                Emit(
                    "probe-attached",
                    Fields(
                        ("acceptingInputBefore", originalAcceptingInput ? "true" : "false"),
                        ("candidate", options.CandidateId),
                        ("freezeHero", options.FreezeHero ? "true" : "false"),
                        ("scene", attachedScene)));

                if (options.CandidateId == "A")
                {
                    TryPrepareNext();
                }
            }
            catch
            {
                hero.acceptingInput = originalAcceptingInput;
                adapter?.Dispose();
                adapter = null;
                attachedHero = null;
                throw;
            }
        }

        private void OnInputManagerUpdateInternal(
            On.InControl.InputManager.orig_UpdateInternal original)
        {
            if (!stopped && attached && options.CandidateId == "B")
            {
                TryPrepareNext();
            }

            original();
        }

        private void OnInputManagerUpdated(ulong inputTick, float deltaTime)
        {
            if (stopped)
            {
                return;
            }

            if (emergencyActions?.EmergencyStop.WasPressed == true)
            {
                Stop("EmergencyStop");
                return;
            }

            if (!attached || adapter == null || pendingLogicalIndex < 0)
            {
                return;
            }

            try
            {
                var logicalIndex = pendingLogicalIndex;
                var expected = fixture[logicalIndex];
                var observed = adapter.Observe(inputTick);
                var matches = observed.Matches(expected);
                var physical = adapter.ObservePhysical();
                if (physical.Any(item => item.Value >= 0.5f))
                {
                    physicalNoiseDetected = true;
                }

                expectedByActualTick[inputTick] = expected;
                while (actualTicksByLogicalIndex.Count <= logicalIndex)
                {
                    actualTicksByLogicalIndex.Add(0);
                }

                actualTicksByLogicalIndex[logicalIndex] = inputTick;
                inputObservationCount++;
                if (!matches)
                {
                    inputMismatchCount++;
                }

                Emit(
                    "input-observed",
                    Fields(
                        ("actualTick", inputTick.ToString(CultureInfo.InvariantCulture)),
                        ("deltaTime", deltaTime.ToString("R", CultureInfo.InvariantCulture)),
                        ("expectedAxisX", expected.AxisX.ToString(CultureInfo.InvariantCulture)),
                        ("expectedAxisY", expected.AxisY.ToString(CultureInfo.InvariantCulture)),
                        ("expectedHeld", expected.Held.ToString()),
                        ("expectedPressed", expected.Pressed.ToString()),
                        ("expectedReleased", expected.Released.ToString()),
                        ("logicalTick", logicalIndex.ToString(CultureInfo.InvariantCulture)),
                        ("matches", matches ? "true" : "false"),
                        ("observedAxisX", observed.AxisX.ToString(CultureInfo.InvariantCulture)),
                        ("observedAxisY", observed.AxisY.ToString(CultureInfo.InvariantCulture)),
                        ("observedHeld", observed.Held.ToString()),
                        ("observedPressed", observed.Pressed.ToString()),
                        ("observedReleased", observed.Released.ToString()),
                        ("physical", FormatPhysical(physical))));

                pendingLogicalIndex = -1;
                nextLogicalIndex = logicalIndex + 1;
                if (nextLogicalIndex >= fixture.Count)
                {
                    completionPending = true;
                    return;
                }

                if (options.CandidateId == "A")
                {
                    TryPrepareNext();
                }
            }
            catch (Exception exception)
            {
                HandleAdapterException(exception);
            }
        }

        private void OnHeroUpdate()
        {
            if (stopped || !attached || adapter == null)
            {
                return;
            }

            try
            {
                var actualTick = InputManager.CurrentTick;
                if (expectedByActualTick.TryGetValue(actualTick, out var expected))
                {
                    var observed = adapter.Observe(actualTick);
                    var matches = observed.Matches(expected);
                    heroObservationCount++;
                    if (!matches)
                    {
                        heroMismatchCount++;
                        heroMismatchTicks.Add(actualTick);
                    }

                    heroCountsByActualTick.TryGetValue(actualTick, out var count);
                    heroCountsByActualTick[actualTick] = count + 1;
                    EmitHeroObservation(actualTick, expected, observed, matches);

                    if (completionPending
                        && actualTicksByLogicalIndex.Count == fixture.Count
                        && actualTick == actualTicksByLogicalIndex[fixture.Count - 1]
                        && options.CaseName != InputProbeOptions.SceneChangeCase)
                    {
                        Stop("Completed");
                        return;
                    }
                }

                if (options.CandidateId == "C" && pendingLogicalIndex < 0)
                {
                    TryPrepareNext();
                }
            }
            catch (Exception exception)
            {
                HandleAdapterException(exception);
            }
        }

        private void TryPrepareNext()
        {
            if (stopped
                || !attached
                || adapter == null
                || pendingLogicalIndex >= 0
                || nextLogicalIndex >= fixture.Count)
            {
                return;
            }

            try
            {
                if (options.CaseName == InputProbeOptions.AdapterExceptionCase
                    && nextLogicalIndex == 20)
                {
                    throw new ControlledInputProbeException(
                        "Injected adapter exception at logical tick 20.");
                }

                var sample = fixture[nextLogicalIndex];
                adapter.Prepare(sample);
                pendingLogicalIndex = nextLogicalIndex;
                Emit(
                    "phase-prepare",
                    Fields(
                        ("candidate", options.CandidateId),
                        ("expectedHeld", sample.Held.ToString()),
                        ("logicalTick", nextLogicalIndex.ToString(CultureInfo.InvariantCulture)),
                        ("phase", GetPreparePhase(options.CandidateId))));
            }
            catch (Exception exception)
            {
                HandleAdapterException(exception);
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            if (stopped || !attached)
            {
                return;
            }

            if (!string.Equals(previous.name, current.name, StringComparison.Ordinal))
            {
                Emit(
                    "scene-changed",
                    Fields(("from", previous.name), ("to", current.name)));
                Stop("SceneChange");
            }
        }

        private void HandleAdapterException(Exception exception)
        {
            if (stopped)
            {
                return;
            }

            logError("T02 adapter exception: " + exception);
            Emit(
                "adapter-exception",
                Fields(
                    ("exceptionType", exception.GetType().Name),
                    ("message", SanitizeField(exception.Message))));
            Stop("AdapterException");
        }

        private void Stop(string reason)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            stopReason = reason;
            UnregisterHooks();

            if (attachedHero != null && options.FreezeHero)
            {
                attachedHero.acceptingInput = originalAcceptingInput;
            }

            if (adapter != null)
            {
                restoreReport = adapter.DetachAndRestore();
            }
            else
            {
                restoreReport = new BindingRestoreReport(
                    false,
                    true,
                    "Adapter was not attached.",
                    Array.Empty<BindingActionSnapshot>(),
                    Array.Empty<BindingActionSnapshot>());
            }

            WriteAtomic(
                Path.Combine(outputDirectory, "binding-after.json"),
                SerializeBindingSnapshots(restoreReport.After));

            var semanticPass = CalculateSemanticPass();
            var runPass = CalculateRunPass(reason, semanticPass, restoreReport.Equivalent);
            Emit(
                "binding-restored",
                Fields(
                    ("equivalent", restoreReport.Equivalent ? "true" : "false"),
                    ("message", restoreReport.Message)));
            Emit(
                "probe-stop",
                Fields(
                    ("inputMismatches", inputMismatchCount.ToString(CultureInfo.InvariantCulture)),
                    ("reason", reason),
                    ("runPass", runPass ? "true" : "false"),
                    ("semanticPass", semanticPass ? "true" : "false")));

            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                SerializeResult(semanticPass, runPass));
            WriteAtomic(
                Path.Combine(outputDirectory, "verdict.md"),
                new UTF8Encoding(false).GetBytes(
                    CreateVerdictMarkdown(semanticPass, runPass)));

            try
            {
                eventSink.Flush(TimeSpan.FromSeconds(2));
            }
            finally
            {
                eventSink.Dispose();
            }

            emergencyActions?.Destroy();
            emergencyActions = null;
            attached = false;
            adapter = null;
            attachedHero = null;
            exitRequested = options.ExitOnComplete;

            var log = runPass ? logInfo : logWarning;
            log(
                "T02 probe stopped candidate="
                + options.CandidateId
                + " case="
                + options.CaseName
                + " reason="
                + reason
                + " pass="
                + runPass);
        }

        private bool CalculateSemanticPass()
        {
            if (inputObservationCount != fixture.Count || inputMismatchCount != 0)
            {
                return false;
            }

            for (var logicalIndex = 0; logicalIndex < fixture.Count; logicalIndex++)
            {
                var sample = fixture[logicalIndex];
                if ((sample.Pressed | sample.Released) == TasAction.None)
                {
                    continue;
                }

                if (actualTicksByLogicalIndex.Count <= logicalIndex)
                {
                    return false;
                }

                var actualTick = actualTicksByLogicalIndex[logicalIndex];
                if (!heroCountsByActualTick.TryGetValue(actualTick, out var count)
                    || count != 1
                    || heroMismatchTicks.Contains(actualTick))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CalculateRunPass(
            string reason,
            bool semanticPass,
            bool bindingsEquivalent)
        {
            if (!bindingsEquivalent)
            {
                return false;
            }

            switch (options.CaseName)
            {
                case InputProbeOptions.NormalCase:
                    return reason == "Completed" && semanticPass;
                case InputProbeOptions.PhysicalNoiseCase:
                    return reason == "Completed"
                           && semanticPass
                           && physicalNoiseDetected;
                case InputProbeOptions.EmergencyStopCase:
                    return reason == "EmergencyStop";
                case InputProbeOptions.SceneChangeCase:
                    return reason == "SceneChange";
                case InputProbeOptions.AdapterExceptionCase:
                    return reason == "AdapterException";
                default:
                    return false;
            }
        }

        private void EmitHeroObservation(
            ulong actualTick,
            InputSample expected,
            CapturedHeroInput observed,
            bool matches)
        {
            var hero = attachedHero;
            var body = hero == null ? null : hero.GetComponent<Rigidbody2D>();
            var fields = Fields(
                ("actualTick", actualTick.ToString(CultureInfo.InvariantCulture)),
                ("actorState", hero == null ? "missing" : hero.hero_state.ToString()),
                ("cState", FormatHeroState(hero)),
                ("expectedHeld", expected.Held.ToString()),
                ("expectedPressed", expected.Pressed.ToString()),
                ("expectedReleased", expected.Released.ToString()),
                ("fixedStep", (runner?.FixedStep ?? 0).ToString(CultureInfo.InvariantCulture)),
                ("matches", matches ? "true" : "false"),
                ("observedHeld", observed.Held.ToString()),
                ("observedPressed", observed.Pressed.ToString()),
                ("observedReleased", observed.Released.ToString()),
                ("position", hero == null ? "missing" : FormatVector(hero.transform.position)),
                ("velocity", body == null ? "missing" : FormatVector(body.velocity)),
                ("visualFrame", (runner?.VisualFrame ?? 0).ToString(CultureInfo.InvariantCulture)));
            Emit("hero-observed", fields);
        }

        private void Emit(
            string eventType,
            IReadOnlyDictionary<string, string> fields)
        {
            if (stopped && eventType != "binding-restored" && eventType != "probe-stop")
            {
                return;
            }

            eventSink.Emit(
                new StructuredEvent(
                    SchemaVersion,
                    sessionId,
                    System.Threading.Interlocked.Increment(ref sequence),
                    eventType,
                    DateTimeOffset.UtcNow,
                    fields));
        }

        private void UnregisterHooks()
        {
            if (!hooksRegistered)
            {
                return;
            }

            hooksRegistered = false;
            InputManager.OnUpdate -= OnInputManagerUpdated;
            ModHooks.HeroUpdateHook -= OnHeroUpdate;
            USceneManager.activeSceneChanged -= OnActiveSceneChanged;
            if (options.CandidateId == "B")
            {
                On.InControl.InputManager.UpdateInternal -= OnInputManagerUpdateInternal;
            }
        }

        private byte[] SerializeResult(bool semanticPass, bool runPass)
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            AppendInt(builder, "schemaVersion", SchemaVersion, false);
            AppendString(builder, "sessionId", sessionId);
            AppendString(builder, "runId", options.RunId);
            AppendString(builder, "candidate", options.CandidateId);
            AppendString(builder, "case", options.CaseName);
            AppendString(builder, "stopReason", stopReason);
            AppendInt(builder, "inputObservationCount", inputObservationCount);
            AppendInt(builder, "inputMismatchCount", inputMismatchCount);
            AppendInt(builder, "heroObservationCount", heroObservationCount);
            AppendInt(builder, "heroMismatchCount", heroMismatchCount);
            AppendInt(builder, "edgeTickCount", CountEdgeTicks());
            AppendInt(builder, "edgeTickPassCount", CountPassingEdgeTicks());
            AppendBoolean(builder, "physicalNoiseDetected", physicalNoiseDetected);
            AppendBoolean(
                builder,
                "bindingEquivalent",
                restoreReport?.Equivalent == true);
            AppendBoolean(builder, "semanticPass", semanticPass);
            AppendBoolean(builder, "runPass", runPass);
            builder.Append('}');
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
        }

        private string CreateVerdictMarkdown(bool semanticPass, bool runPass)
        {
            var builder = new StringBuilder(1024);
            builder.AppendLine("# T02 Input Phase Probe Verdict");
            builder.AppendLine();
            builder.AppendLine("- Candidate: `" + options.CandidateId + "`");
            builder.AppendLine("- Case: `" + options.CaseName + "`");
            builder.AppendLine("- Run: `" + options.RunId + "`");
            builder.AppendLine("- Stop reason: `" + stopReason + "`");
            builder.AppendLine("- Input observations: " + inputObservationCount);
            builder.AppendLine("- Input mismatches: " + inputMismatchCount);
            builder.AppendLine("- Hero observations: " + heroObservationCount);
            builder.AppendLine("- Hero mismatches: " + heroMismatchCount);
            builder.AppendLine("- Physical noise detected: " + physicalNoiseDetected);
            builder.AppendLine(
                "- Binding restore equivalent: " + (restoreReport?.Equivalent == true));
            builder.AppendLine("- Semantic pass: " + semanticPass);
            builder.AppendLine("- Run pass: " + runPass);
            return builder.ToString();
        }

        private int CountEdgeTicks()
        {
            return fixture.Count(
                sample => (sample.Pressed | sample.Released) != TasAction.None);
        }

        private int CountPassingEdgeTicks()
        {
            var count = 0;
            for (var index = 0; index < fixture.Count; index++)
            {
                if ((fixture[index].Pressed | fixture[index].Released) == TasAction.None
                    || actualTicksByLogicalIndex.Count <= index)
                {
                    continue;
                }

                var actualTick = actualTicksByLogicalIndex[index];
                if (heroCountsByActualTick.TryGetValue(actualTick, out var heroCount)
                    && heroCount == 1
                    && !heroMismatchTicks.Contains(actualTick))
                {
                    count++;
                }
            }

            return count;
        }

        private static HeroInputAdapterBase CreateAdapter(string candidateId)
        {
            switch (candidateId)
            {
                case "A":
                    return new HeroInputAdapter();
                case "B":
                    return new InputAdapterCandidateB();
                case "C":
                    return new InputAdapterCandidateC();
                default:
                    throw new ArgumentOutOfRangeException(nameof(candidateId));
            }
        }

        private static string GetPreparePhase(string candidateId)
        {
            switch (candidateId)
            {
                case "A":
                    return "post-input-update-for-next-tick";
                case "B":
                    return "pre-input-update-same-tick";
                case "C":
                    return "hero-update-for-next-input-tick";
                default:
                    return "unknown";
            }
        }

        private static string FormatPhysical(
            IReadOnlyDictionary<TasAction, float> values)
        {
            return string.Join(
                ",",
                values.Select(
                    item => item.Key
                            + "="
                            + item.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        private static string FormatHeroState(HeroController? hero)
        {
            if (hero == null || hero.cState == null)
            {
                return "missing";
            }

            return string.Join(
                ",",
                new[]
                {
                    "acceptingInput=" + (hero.acceptingInput ? "true" : "false"),
                    "attacking=" + (hero.cState.attacking ? "true" : "false"),
                    "dashing=" + (hero.cState.dashing ? "true" : "false"),
                    "jumping=" + (hero.cState.jumping ? "true" : "false"),
                    "onGround=" + (hero.cState.onGround ? "true" : "false")
                });
        }

        private static string FormatVector(Vector3 value)
        {
            return value.x.ToString("R", CultureInfo.InvariantCulture)
                   + ","
                   + value.y.ToString("R", CultureInfo.InvariantCulture)
                   + ","
                   + value.z.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatVector(Vector2 value)
        {
            return value.x.ToString("R", CultureInfo.InvariantCulture)
                   + ","
                   + value.y.ToString("R", CultureInfo.InvariantCulture);
        }

        private static IReadOnlyDictionary<string, string> Fields(
            params (string Key, string Value)[] fields)
        {
            return fields.ToDictionary(
                item => item.Key,
                item => item.Value ?? string.Empty,
                StringComparer.Ordinal);
        }

        private static byte[] SerializeBindingSnapshots(
            IEnumerable<BindingActionSnapshot> snapshots)
        {
            var builder = new StringBuilder(4096);
            builder.Append("{\"schemaVersion\":1,\"actions\":[");
            var firstAction = true;
            foreach (var action in snapshots.OrderBy(item => (ushort)item.Action))
            {
                if (!firstAction)
                {
                    builder.Append(',');
                }

                builder.Append('{');
                AppendString(builder, "action", action.Action.ToString(), false);
                AppendString(builder, "actionName", action.ActionName);
                builder.Append(",\"bindings\":[");
                for (var index = 0; index < action.Bindings.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    var binding = action.Bindings[index];
                    builder.Append('{');
                    AppendInt(builder, "index", binding.Index, false);
                    AppendString(builder, "name", binding.Name);
                    AppendString(builder, "sourceType", binding.SourceType);
                    AppendString(builder, "deviceClass", binding.DeviceClass);
                    AppendString(builder, "deviceStyle", binding.DeviceStyle);
                    builder.Append('}');
                }

                builder.Append("]}");
                firstAction = false;
            }

            builder.Append("]}");
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value,
            bool comma = true)
        {
            if (comma)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendInt(
            StringBuilder builder,
            string name,
            int value,
            bool comma = true)
        {
            if (comma)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            builder.Append(',');
            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
            builder.Append(value ? "true" : "false");
        }

        private static string SanitizeField(string value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
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

        private sealed class EmergencyActionSet : PlayerActionSet
        {
            public EmergencyActionSet()
            {
                EmergencyStop = CreatePlayerAction("HKTAS Emergency Stop");
                EmergencyStop.AddDefaultBinding(Key.F8);
            }

            public PlayerAction EmergencyStop { get; }
        }

        private sealed class ControlledInputProbeException : Exception
        {
            public ControlledInputProbeException(string message)
                : base(message)
            {
            }
        }
    }

    internal sealed class InputPhaseProbeRunner : MonoBehaviour
    {
        private InputPhaseProbe? owner;

        public long VisualFrame { get; private set; }
        public long FixedStep { get; private set; }

        public void Initialize(InputPhaseProbe probe)
        {
            owner = probe;
        }

        private void Update()
        {
            VisualFrame++;
            owner?.OnVisualUpdate(VisualFrame, FixedStep);
        }

        private void FixedUpdate()
        {
            FixedStep++;
        }
    }

    internal sealed class InputProbeOptions
    {
        public const string NormalCase = "normal";
        public const string PhysicalNoiseCase = "physical-noise";
        public const string EmergencyStopCase = "emergency-stop";
        public const string SceneChangeCase = "scene-change";
        public const string AdapterExceptionCase = "adapter-exception";

        private InputProbeOptions(
            string candidateId,
            string runId,
            string caseName,
            int autoLoadSlot,
            bool exitOnComplete,
            bool freezeHero)
        {
            CandidateId = candidateId;
            RunId = runId;
            CaseName = caseName;
            AutoLoadSlot = autoLoadSlot;
            ExitOnComplete = exitOnComplete;
            FreezeHero = freezeHero;
        }

        public string CandidateId { get; }
        public string RunId { get; }
        public string CaseName { get; }
        public int AutoLoadSlot { get; }
        public bool ExitOnComplete { get; }
        public bool FreezeHero { get; }

        public static InputProbeParseResult Parse(string[] arguments)
        {
            var candidate = ReadValue(arguments, "--hktas-input-probe=");
            if (candidate == null)
            {
                return InputProbeParseResult.NotRequested();
            }

            candidate = candidate.ToUpperInvariant();
            if (candidate != "A" && candidate != "B" && candidate != "C")
            {
                return InputProbeParseResult.Invalid("candidate must be A, B, or C");
            }

            var runId = ReadValue(arguments, "--hktas-input-probe-run=")
                        ?? candidate.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N");
            if (!IsSafeIdentifier(runId))
            {
                return InputProbeParseResult.Invalid(
                    "run id must contain only letters, digits, dot, dash, or underscore");
            }

            var caseName = (ReadValue(arguments, "--hktas-input-probe-case=")
                            ?? NormalCase).ToLowerInvariant();
            if (caseName != NormalCase
                && caseName != PhysicalNoiseCase
                && caseName != EmergencyStopCase
                && caseName != SceneChangeCase
                && caseName != AdapterExceptionCase)
            {
                return InputProbeParseResult.Invalid("unknown probe case");
            }

            var slotText = ReadValue(arguments, "--hktas-input-probe-slot=") ?? "2";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return InputProbeParseResult.Invalid("slot must be in [1, 4]");
            }

            return InputProbeParseResult.Valid(
                new InputProbeOptions(
                    candidate,
                    runId,
                    caseName,
                    slot,
                    HasFlag(arguments, "--hktas-input-probe-exit"),
                    !HasFlag(arguments, "--hktas-input-probe-live-hero")));
        }

        private static string? ReadValue(string[] arguments, string prefix)
        {
            return arguments
                .FirstOrDefault(
                    value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?.Substring(prefix.Length);
        }

        private static bool HasFlag(string[] arguments, string flag)
        {
            return arguments.Any(
                value => string.Equals(value, flag, StringComparison.OrdinalIgnoreCase));
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

    internal sealed class InputProbeParseResult
    {
        private InputProbeParseResult(
            bool requested,
            InputProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public InputProbeOptions? Options { get; }
        public string? Error { get; }

        public static InputProbeParseResult NotRequested()
        {
            return new InputProbeParseResult(false, null, null);
        }

        public static InputProbeParseResult Valid(InputProbeOptions options)
        {
            return new InputProbeParseResult(true, options, null);
        }

        public static InputProbeParseResult Invalid(string error)
        {
            return new InputProbeParseResult(true, null, error);
        }
    }
}
