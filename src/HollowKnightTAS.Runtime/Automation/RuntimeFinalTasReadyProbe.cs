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
    /// Establishes the T16 cold-start fixture without injecting input or
    /// changing Hero/PlayerData state. It invokes the same vanilla save-slot
    /// load entry point as the title UI, then requires a stable Hall of Gods
    /// bench state. Unlike RuntimeAutomationReadyProbe, it never calls
    /// RegainControl because the seated state is part of the fixture.
    /// </summary>
    public sealed class RuntimeFinalTasReadyProbe : IDisposable
    {
        private const int RequiredStableUpdates = 30;
        private readonly FinalTasReadyOptions options;
        private readonly Action<string> logInfo;
        private readonly Action<string> logDebug;
        private readonly Action<string> logError;
        private readonly string outputDirectory;
        private RuntimeFinalTasReadyProbeRunner? runner;
        private bool stopped;
        private bool loadRequested;
        private float startedAtRealtime;
        private int stableUpdates;
        private Vector3 stablePosition;
        private bool hasStablePosition;

        private RuntimeFinalTasReadyProbe(
            string sessionDirectory,
            FinalTasReadyOptions options,
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
                "final-tas-ready",
                options.RunId);
            Directory.CreateDirectory(outputDirectory);
        }

        public string OutputDirectory => outputDirectory;
        public bool IsStopped => stopped;

        public static RuntimeFinalTasReadyProbe? TryStart(
            string sessionDirectory,
            Action<string> logInfo,
            Action<string> logDebug,
            Action<string> logError)
        {
            var parse = FinalTasReadyOptions.Parse(
                Environment.GetCommandLineArgs());
            if (!parse.Requested)
            {
                return null;
            }
            if (parse.Error != null || parse.Options == null)
            {
                throw new InvalidOperationException(
                    "Invalid final TAS readiness arguments: " + parse.Error);
            }

            var probe = new RuntimeFinalTasReadyProbe(
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
                    "Application quit before the seated fixture was ready.");
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
                        "T16 final TAS readiness loading slot="
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
                        "A stable seated GG_Workshop fixture was not available within 90 seconds.");
                    return;
                }

                var hero = HeroController.SilentInstance;
                var manager = GameManager.instance;
                var player = PlayerData.instance;
                var scene = USceneManager.GetActiveScene().name ?? string.Empty;
                if (hero == null
                    || manager == null
                    || player == null
                    || manager.gameState != GameState.PLAYING
                    || !hero.gameObject.activeInHierarchy
                    || !string.Equals(scene, "GG_Workshop", StringComparison.Ordinal)
                    || !player.atBench)
                {
                    ResetStability();
                    return;
                }

                var position = hero.transform.position;
                if (!hasStablePosition
                    || (position - stablePosition).sqrMagnitude > 0.000001f)
                {
                    stablePosition = position;
                    hasStablePosition = true;
                    stableUpdates = 1;
                    return;
                }

                stableUpdates++;
                if (stableUpdates >= RequiredStableUpdates)
                {
                    Stop(true, "Ready", string.Empty);
                }
            }
            catch (Exception exception)
            {
                logError("T16 final TAS readiness failed: " + exception);
                Stop(false, exception.GetType().Name, exception.Message);
            }
        }

        private void Start()
        {
            startedAtRealtime = Time.realtimeSinceStartup;
            var gameObject = new GameObject(
                "HollowKnightTAS.RuntimeFinalTasReadyProbe");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner =
                gameObject.AddComponent<RuntimeFinalTasReadyProbeRunner>();
            runner.Initialize(this);
            logInfo(
                "T16 final TAS readiness created runId="
                + options.RunId
                + " slot="
                + options.AutoLoadSlot.ToString(CultureInfo.InvariantCulture));
        }

        private void ResetStability()
        {
            stableUpdates = 0;
            hasStablePosition = false;
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
                "T16 final TAS readiness stopped runId="
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
            var hero = HeroController.SilentInstance;
            var player = PlayerData.instance;
            var position = hero != null
                ? hero.transform.position
                : Vector3.zero;
            var builder = new StringBuilder(768);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "runId", options.RunId);
            AppendNumber(builder, "autoLoadSlot", options.AutoLoadSlot);
            AppendString(builder, "loadMethod", "GameManager.LoadGameFromUI");
            AppendString(
                builder,
                "scene",
                USceneManager.GetActiveScene().name ?? string.Empty);
            AppendNumber(builder, "stableUpdates", stableUpdates);
            AppendBoolean(builder, "fixtureReady", ready);
            AppendBoolean(builder, "loadRequested", loadRequested);
            AppendBoolean(builder, "atBench", player?.atBench ?? false);
            AppendBoolean(
                builder,
                "nearBench",
                hero != null && hero.cState.nearBench);
            AppendBoolean(
                builder,
                "acceptingInput",
                hero != null && hero.acceptingInput);
            AppendFloat(builder, "heroPositionX", position.x);
            AppendFloat(builder, "heroPositionY", position.y);
            AppendBoolean(builder, "inputInjected", false);
            AppendBoolean(builder, "timeScaleChanged", false);
            AppendBoolean(builder, "heroStateWritten", false);
            AppendBoolean(builder, "playerDataWritten", false);
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
            AppendPrefix(builder, name);
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
            AppendPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendFloat(
            StringBuilder builder,
            string name,
            float value)
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

        private static void AppendPrefix(StringBuilder builder, string name)
        {
            if (builder.Length > 1)
            {
                builder.Append(',');
            }
            builder.Append('"');
            builder.Append(name);
            builder.Append("\":");
        }

        private static void WriteAtomic(string destinationPath, byte[] bytes)
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
    internal sealed class RuntimeFinalTasReadyProbeRunner : MonoBehaviour
    {
        private RuntimeFinalTasReadyProbe? owner;

        internal void Initialize(RuntimeFinalTasReadyProbe value)
        {
            owner = value;
        }

        private void Update()
        {
            owner?.OnUpdate();
        }
    }

    internal sealed class FinalTasReadyOptions
    {
        private FinalTasReadyOptions(string runId, int autoLoadSlot)
        {
            RunId = runId;
            AutoLoadSlot = autoLoadSlot;
        }

        public string RunId { get; }
        public int AutoLoadSlot { get; }

        public static FinalTasReadyParseResult Parse(string[] arguments)
        {
            var runId = ReadValue(arguments, "--hktas-final-tas-ready-run=");
            if (runId == null)
            {
                return FinalTasReadyParseResult.NotRequested();
            }
            if (!IsSafeIdentifier(runId))
            {
                return FinalTasReadyParseResult.Invalid(
                    "run id must contain only letters, digits, dot, dash, or underscore");
            }

            var slotText =
                ReadValue(arguments, "--hktas-final-tas-ready-slot=") ?? "4";
            if (!int.TryParse(
                    slotText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 1
                || slot > 4)
            {
                return FinalTasReadyParseResult.Invalid("slot must be in [1, 4]");
            }

            return FinalTasReadyParseResult.Valid(
                new FinalTasReadyOptions(runId, slot));
        }

        private static string? ReadValue(string[] arguments, string prefix)
        {
            return arguments.FirstOrDefault(
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

    internal sealed class FinalTasReadyParseResult
    {
        private FinalTasReadyParseResult(
            bool requested,
            FinalTasReadyOptions? options,
            string? error)
        {
            Requested = requested;
            Options = options;
            Error = error;
        }

        public bool Requested { get; }
        public FinalTasReadyOptions? Options { get; }
        public string? Error { get; }

        public static FinalTasReadyParseResult NotRequested()
        {
            return new FinalTasReadyParseResult(false, null, null);
        }

        public static FinalTasReadyParseResult Valid(FinalTasReadyOptions options)
        {
            return new FinalTasReadyParseResult(true, options, null);
        }

        public static FinalTasReadyParseResult Invalid(string error)
        {
            return new FinalTasReadyParseResult(true, null, error);
        }
    }
}
