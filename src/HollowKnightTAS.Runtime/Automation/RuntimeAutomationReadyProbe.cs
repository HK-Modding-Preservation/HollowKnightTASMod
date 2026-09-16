using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlobalEnums;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HollowKnightTAS.Runtime.Automation
{
    /// <summary>
    /// Provides a non-visual readiness signal for external automation
    /// acceptance runs. The probe may load a requested save slot through the
    /// vanilla title-UI entry point, then only observes readiness. It never
    /// injects input, completes the Hero control lifecycle, or changes time,
    /// position, animation, physics, or player resources.
    /// </summary>
    public sealed class RuntimeAutomationReadyProbe : IDisposable
    {
        private const int RequiredStableGameplayUpdates = 30;
        private readonly AutomationReadyProbeOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logDebug;
        private readonly Action<string> logError;
        private readonly string outputDirectory;

        private RuntimeAutomationReadyProbeRunner? runner;
        private bool stopped;
        private bool loadRequested;
        private float startedAtRealtime;
        private int stableGameplayUpdates;
        private string stableScene = string.Empty;

        private RuntimeAutomationReadyProbe(
            string sessionDirectory,
            AutomationReadyProbeOptions options,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logError)
        {
            this.options = options;
            this.logInfo = logInfo;
            this.logDebug = logDebug;
            this.logError = logError;
            outputDirectory = Path.Combine(
                sessionDirectory,
                "automation-ready",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
        }

        public string OutputDirectory => outputDirectory;
        public bool IsStopped => stopped;

        public static RuntimeAutomationReadyProbe? TryStart(
            string sessionDirectory,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logError)
        {
            var parse = AutomationReadyProbeOptions.Parse(
                Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }

            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid automation readiness probe arguments: "
                    + parse.Error);
            }

            var probe = new RuntimeAutomationReadyProbe(
                sessionDirectory,
                parse.Options,
                logInfo,
                logDebug,
                logError);
            probe.Start();
            return probe;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                Stop(
                    false,
                    "ApplicationQuit",
                    "Application quit before automation readiness was established.");
            }

            DestroyRunner();
        }

        internal void OnUpdate()
        {
            if (stopped)
            {
                return;
            }

            try
            {
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
                    logDebug(
                        "T15 automation readiness probe loading slot="
                        + options.AutoLoadSlot.ToString(
                            CultureInfo.InvariantCulture));
                    GameManager.instance.LoadGameFromUI(options.AutoLoadSlot);
                    return;
                }

                if (elapsed > 90f)
                {
                    Stop(
                        false,
                        "StartupTimeout",
                        "A stable playable Hero was not available within 90 seconds.");
                    return;
                }

                var hero = HeroController.SilentInstance;
                var gameManager = GameManager.instance;
                var scene = USceneManager.GetActiveScene().name ?? string.Empty;
                if (hero == null
                    || gameManager == null
                    || gameManager.gameState != GameState.PLAYING
                    || !hero.gameObject.activeInHierarchy
                    || string.Equals(
                        scene,
                        "Menu_Title",
                        StringComparison.Ordinal))
                {
                    ResetStability(scene);
                    return;
                }

                if (!string.Equals(
                        stableScene,
                        scene,
                        StringComparison.Ordinal))
                {
                    stableScene = scene;
                    stableGameplayUpdates = 0;
                }

                if (!hero.acceptingInput)
                {
                    stableGameplayUpdates = 0;
                    return;
                }

                stableGameplayUpdates++;
                if (stableGameplayUpdates < RequiredStableGameplayUpdates)
                {
                    return;
                }

                Stop(true, "Ready", string.Empty);
            }
            catch (Exception exception)
            {
                logError(
                    "T15 automation readiness probe failed: "
                    + exception);
                Stop(
                    false,
                    exception.GetType().Name,
                    exception.Message);
            }
        }

        private void Start()
        {
            startedAtRealtime = Time.realtimeSinceStartup;
            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeAutomationReadyProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner =
                gameObject.AddComponent<RuntimeAutomationReadyProbeRunner>();
            runner.Initialize(this);
            logInfo(
                "T15 automation readiness probe created runId="
                + options.RunId
                + " slot="
                + options.AutoLoadSlot.ToString(
                    CultureInfo.InvariantCulture));
        }

        private void ResetStability(string scene)
        {
            stableScene = scene;
            stableGameplayUpdates = 0;
        }

        private void Stop(bool ready, string reason, string error)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            WriteResult(ready, reason, error);
            logInfo(
                "T15 automation readiness probe stopped runId="
                + options.RunId
                + " ready="
                + ready
                + " reason="
                + reason);
            DestroyRunner();
        }

        private void DestroyRunner()
        {
            if (runner == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(runner.gameObject);
            runner = null;
        }

        private void WriteResult(bool ready, string reason, string error)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "runId", options.RunId);
            AppendNumber(builder, "autoLoadSlot", options.AutoLoadSlot);
            AppendString(
                builder,
                "scene",
                USceneManager.GetActiveScene().name ?? string.Empty);
            AppendNumber(
                builder,
                "stableGameplayUpdates",
                stableGameplayUpdates);
            AppendBoolean(builder, "gameplayReady", ready);
            AppendBoolean(builder, "loadRequested", loadRequested);
            AppendBoolean(builder, "inputInjected", false);
            AppendBoolean(builder, "timeScaleChanged", false);
            AppendBoolean(
                builder,
                "heroStateWritten",
                false);
            AppendNumber(
                builder,
                "heroControlReacquireCount",
                0);
            AppendString(builder, "reason", reason);
            AppendString(builder, "error", error ?? string.Empty);
            builder.Append('}');
            WriteAtomic(
                Path.Combine(outputDirectory, "result.json"),
                new UTF8Encoding(false).GetBytes(builder.ToString()));
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append('"');
            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(
                                ((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            int value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(
                value.ToString(CultureInfo.InvariantCulture));
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
            if (builder.Length > 1)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append(name);
            builder.Append("\":");
        }

        private static void WriteAtomic(
            string destinationPath,
            byte[] bytes)
        {
            var temporaryPath =
                destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
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
    internal sealed class RuntimeAutomationReadyProbeRunner :
        MonoBehaviour
    {
        private RuntimeAutomationReadyProbe? owner;

        internal void Initialize(RuntimeAutomationReadyProbe value)
        {
            owner = value;
        }

        private void Update()
        {
            owner?.OnUpdate();
        }
    }

    internal sealed class AutomationReadyProbeOptions
    {
        private AutomationReadyProbeOptions(
            string runId,
            int autoLoadSlot)
        {
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
        }

        public string RunId { get; }
        public int AutoLoadSlot { get; }

        public static AutomationReadyProbeParseResult Parse(
            string[] arguments)
        {
            var runId = ReadValue(
                arguments,
                "--hktas-automation-ready-run=");
            if (runId == null)
            {
                return AutomationReadyProbeParseResult.NotRequested();
            }

            if (!IsSafeIdentifier(runId))
            {
                return AutomationReadyProbeParseResult.Invalid(
                    "run id must contain only letters, digits, dot, dash, or underscore");
            }

            var slotText = ReadValue(
                               arguments,
                               "--hktas-automation-ready-slot=")
                           ?? "4";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return AutomationReadyProbeParseResult.Invalid(
                    "slot must be in [1, 4]");
            }

            return AutomationReadyProbeParseResult.Valid(
                new AutomationReadyProbeOptions(runId, slot));
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

    internal sealed class AutomationReadyProbeParseResult
    {
        private AutomationReadyProbeParseResult(
            bool requested,
            AutomationReadyProbeOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public AutomationReadyProbeOptions? Options { get; }
        public string? Error { get; }

        public static AutomationReadyProbeParseResult NotRequested()
        {
            return new AutomationReadyProbeParseResult(
                false,
                null,
                null);
        }

        public static AutomationReadyProbeParseResult Valid(
            AutomationReadyProbeOptions options)
        {
            return new AutomationReadyProbeParseResult(
                true,
                options,
                null);
        }

        public static AutomationReadyProbeParseResult Invalid(
            string error)
        {
            return new AutomationReadyProbeParseResult(
                true,
                null,
                error);
        }
    }
}
