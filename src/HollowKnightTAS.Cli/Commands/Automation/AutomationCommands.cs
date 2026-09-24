using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Automation.Client;
using HollowKnightTAS.Core.Automation;

namespace HollowKnightTAS.Cli.Commands.Automation
{
    internal static class AutomationCommands
    {
        public const string StatusUsage =
            "  HollowKnightTAS.Cli automation status [--bootstrap=<path>]";
        public const string StateUsage =
            "  HollowKnightTAS.Cli automation state [--bootstrap=<path>]";
        public const string CallUsage =
            "  HollowKnightTAS.Cli automation call <command-id> <scope> "
            + "[key=value ...] [--lease=<id>] [--expected-mode=<mode>] "
            + "[--expected-tick=<tick>] [--bootstrap=<path>]";
        public const string WatchUsage =
            "  HollowKnightTAS.Cli automation watch "
            + "[--from=<tick>] [--count=<1..200>] "
            + "[--iterations=<1..100000> | "
            + "--duration-seconds=<1..86400>] "
            + "[--bootstrap=<path>]";

        internal readonly struct WatchOptions
        {
            public WatchOptions(
                long fromMovieTick,
                int count,
                int? iterations,
                TimeSpan? duration)
            {
                FromMovieTick = fromMovieTick;
                Count = count;
                Iterations = iterations;
                Duration = duration;
            }

            public long FromMovieTick { get; }

            public int Count { get; }

            public int? Iterations { get; }

            public TimeSpan? Duration { get; }
        }

        public static int Status(
            string[] args,
            TextWriter output)
        {
            return RunAsync(
                    async client =>
                    {
                        var result = await client.ExecuteAsync(
                            client.CreateCommand(
                                AutomationCommandIds.GetStatus,
                                AutomationScope.ObserveStatus));
                        WriteResult(output, result);
                        return result.Success ? 0 : 3;
                    },
                    args)
                .GetAwaiter()
                .GetResult();
        }

        public static int State(
            string[] args,
            TextWriter output)
        {
            return RunAsync(
                    async client =>
                    {
                        var state =
                            await client.GetSemanticStateAsync();
                        output.WriteLine(state.ToFriendlyJson());
                        return 0;
                    },
                    args)
                .GetAwaiter()
                .GetResult();
        }

        public static int Startup(string[] args, TextWriter output)
        {
            return RunAsync(async client =>
            {
                var result = await client.ExecuteAsync(client.CreateCommand(
                    AutomationCommandIds.GetStartupProfile, AutomationScope.ObserveStatus));
                if (result.Success)
                    output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Data));
                else
                    WriteResult(output, result);
                return result.Success ? 0 : 3;
            }, args).GetAwaiter().GetResult();
        }

        public static int Call(
            string[] args,
            TextWriter output)
        {
            var positional = args
                .Where(value => !value.StartsWith("--", StringComparison.Ordinal))
                .ToArray();
            if (positional.Length < 2)
            {
                throw new ArgumentException(CallUsage);
            }

            var commandId = positional[0];
            var scope = positional[1];
            var arguments = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var item in positional.Skip(2))
            {
                var separator = item.IndexOf('=');
                if (separator < 1)
                {
                    throw new ArgumentException(
                        "Automation arguments must use key=value.");
                }

                var key = item.Substring(0, separator);
                var value = item.Substring(separator + 1);
                if (!arguments.TryAdd(key, value))
                {
                    throw new ArgumentException(
                        "Duplicate automation argument: " + key);
                }
            }

            if (commandId == AutomationCommandIds.BeginFullRunReplay
                && arguments.TryGetValue("movieFile", out var movieFile))
            {
                if (arguments.ContainsKey("movieBase64"))
                    throw new ArgumentException("Use movieFile or movieBase64, not both.");
                var moviePath = Path.GetFullPath(movieFile);
                var length = new FileInfo(moviePath).Length;
                if (length <= 0 || length > HollowKnightTAS.Core.Movie.MovieProtocolV2.MaximumSourceUtf8Bytes)
                    throw new ArgumentException("Full-run movie file size is invalid.");
                var bytes = File.ReadAllBytes(moviePath);
                if (bytes.LongLength != length)
                    throw new IOException("Full-run movie changed while reading.");
                arguments.Remove("movieFile");
                arguments.Add("movieBase64", Convert.ToBase64String(bytes));
            }

            var lease = ReadOption(args, "--lease=")
                        ?? string.Empty;
            var expectedMode =
                ReadOption(args, "--expected-mode=")
                ?? string.Empty;
            long? expectedTick = null;
            var tickText = ReadOption(args, "--expected-tick=");
            if (tickText != null)
            {
                if (!long.TryParse(
                        tickText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                    || parsed < 0)
                {
                    throw new ArgumentException(
                        "--expected-tick must be non-negative.");
                }

                expectedTick = parsed;
            }

            return RunAsync(
                    async client =>
                    {
                        var activeLease = lease;
                        var autoLease =
                            string.IsNullOrEmpty(activeLease)
                            && !AutomationScope.IsReadOnly(scope)
                            && commandId
                               != AutomationCommandIds.AcquireControl
                            && commandId
                               != AutomationCommandIds.RenewControl
                            && commandId
                               != AutomationCommandIds.ReleaseControl;
                        if (autoLease)
                        {
                            var acquired =
                                await client.ExecuteAsync(
                                    client.CreateCommand(
                                        AutomationCommandIds
                                            .AcquireControl,
                                        scope,
                                        new Dictionary<
                                            string,
                                            string>
                                        {
                                            ["scopes"] = scope,
                                            ["ttlSeconds"] = "30"
                                        }));
                            if (!acquired.Success
                                || !acquired.Data.TryGetValue(
                                    "leaseId",
                                    out activeLease))
                            {
                                WriteResult(output, acquired);
                                return 3;
                            }
                        }

                        try
                        {
                            var result =
                                await client.ExecuteAsync(
                                    client.CreateCommand(
                                        commandId,
                                        scope,
                                        arguments,
                                        activeLease,
                                        expectedMode,
                                        expectedTick));
                            WriteResult(output, result);
                            return result.Success ? 0 : 3;
                        }
                        finally
                        {
                            if (autoLease
                                && !string.IsNullOrEmpty(
                                    activeLease))
                            {
                                await client.ExecuteAsync(
                                    client.CreateCommand(
                                        AutomationCommandIds
                                            .ReleaseControl,
                                        scope,
                                        leaseId: activeLease));
                            }
                        }
                    },
                    args)
                .GetAwaiter()
                .GetResult();
        }

        public static int Watch(
            string[] args,
            TextWriter output)
        {
            var options = ParseWatchOptions(args);
            return RunAsync(
                    async client =>
                    {
                        var completed = 0;
                        var stopwatch = options.Duration.HasValue
                            ? Stopwatch.StartNew()
                            : null;
                        await foreach (var result in
                                       client.SubscribeTimelineAsync(
                                           options.FromMovieTick,
                                           options.Count,
                                           TimeSpan.FromMilliseconds(
                                               250)))
                        {
                            WriteResult(output, result);
                            completed++;
                            if (HasWatchCompleted(
                                    options,
                                    completed,
                                    stopwatch?.Elapsed
                                    ?? TimeSpan.Zero))
                            {
                                break;
                            }
                        }

                        return 0;
                    },
                    args)
                .GetAwaiter()
                .GetResult();
        }

        internal static WatchOptions ParseWatchOptions(
            string[] args)
        {
            var fromText = ReadOption(args, "--from=");
            var from = string.Equals(
                fromText,
                "-1",
                StringComparison.Ordinal)
                ? -1L
                : fromText == null
                    ? 0L
                    : ParseLongOptionValue(
                        fromText,
                        "--from=",
                        0,
                        long.MaxValue);
            var count = checked((int)ReadLongOption(
                args,
                "--count=",
                1,
                200,
                100));
            var iterationsText = ReadOption(
                args,
                "--iterations=");
            var durationText = ReadOption(
                args,
                "--duration-seconds=");
            if (iterationsText != null
                && durationText != null)
            {
                throw new ArgumentException(
                    "--iterations and --duration-seconds "
                    + "are mutually exclusive.");
            }

            if (durationText != null)
            {
                var durationSeconds = ParseLongOptionValue(
                    durationText,
                    "--duration-seconds=",
                    1,
                    86400);
                return new WatchOptions(
                    from,
                    count,
                    null,
                    TimeSpan.FromSeconds(durationSeconds));
            }

            var iterations = iterationsText == null
                ? 1
                : checked((int)ParseLongOptionValue(
                    iterationsText,
                    "--iterations=",
                    1,
                    100000));
            return new WatchOptions(
                from,
                count,
                iterations,
                null);
        }

        internal static bool HasWatchCompleted(
            WatchOptions options,
            int completed,
            TimeSpan elapsed)
        {
            return (options.Iterations.HasValue
                    && completed >= options.Iterations.Value)
                   || (options.Duration.HasValue
                       && elapsed >= options.Duration.Value);
        }

        private static async Task<int> RunAsync(
            Func<AutomationClient, Task<int>> operation,
            string[] args)
        {
            await using var client = new AutomationClient();
            var options = new AutomationConnectOptions();
            var path = ReadOption(args, "--bootstrap=");
            if (path != null)
            {
                options.BootstrapPath = Path.GetFullPath(path);
            }

            await client.ConnectAsync(
                options,
                CancellationToken.None);
            return await operation(client);
        }

        private static void WriteResult(
            TextWriter output,
            AutomationResultEnvelope result)
        {
            output.WriteLine(
                new UTF8Encoding(false, true).GetString(
                    result.ToPayload()));
        }

        private static string? ReadOption(
            IEnumerable<string> args,
            string prefix)
        {
            var matches = args
                .Where(
                    value => value.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new ArgumentException(
                    "Duplicate option " + prefix);
            }

            return matches.Length == 0
                ? null
                : matches[0].Substring(prefix.Length);
        }

        private static long ReadLongOption(
            string[] args,
            string prefix,
            long minimum,
            long maximum,
            long defaultValue)
        {
            var text = ReadOption(args, prefix);
            if (text == null)
            {
                return defaultValue;
            }

            return ParseLongOptionValue(
                text,
                prefix,
                minimum,
                maximum);
        }

        private static long ParseLongOptionValue(
            string text,
            string prefix,
            long minimum,
            long maximum)
        {
            if (!long.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentException(
                    prefix
                    + " must be in ["
                    + minimum
                    + ","
                    + maximum
                    + "].");
            }

            return value;
        }
    }
}
