using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Core.Movie;

internal static class Program
{
    private const string DefaultGame = @"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe";
    private const string DefaultBundle = @"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed\Mods\HollowKnightTAS\Companion\win-x64\ClockStartup";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static Dictionary<string, object?>? currentReport;

    // Keep the observation wire names local so this harness can be built while
    // an installed Core bundle is one build behind the newly installed Runtime.
    // The values are the formal Core IpcMessageTypes names.
    private const string GetWorldSnapshot = "getWorldSnapshot";
    private const string GetObjectDetails = "getObjectDetails";
    private const string WorldSnapshot = "worldSnapshot";
    private const string ObjectDetails = "objectDetails";

    public static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        Directory.CreateDirectory(options.Output);
        var started = Stopwatch.StartNew();
        var report = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mode"] = options.Mode,
            ["fixture"] = options.Fixture,
            ["output"] = options.Output,
            ["startedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["queries"] = new List<object>(),
            ["status"] = new List<object>(),
            ["resources"] = new List<object>(),
            ["scenes"] = new List<object>(),
            ["checks"] = new List<string>()
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var resources = new ResourceMonitor(report, linked.Token);
        VerifiedGameLaunchHandle? handle = null;
        StartupBootController? boot = null;
        FullRunMovieCoordinator? movies = null;
        RuntimeSessionClient? runtime = null;
        SingleInstanceCoordinator? instance = null;
        SessionRegistry? sessions = null;
        ControlPipeServer? control = null;
        currentReport = report;
        try
        {
            // Own the formal current-user Companion mutex before the game starts.
            // The installed Companion then forwards Runtime registration to this
            // pipe, so this harness never handles or prints the registration token.
            instance = new SingleInstanceCoordinator();
            if (!instance.IsPrimary)
                throw new InvalidOperationException(
                    "A Companion instance already owns the current-user control pipe. "
                    + "Close it or temporarily disable AutoStartCompanion before running the harness.");
            sessions = new SessionRegistry(
                "world-observation-harness-" + Guid.NewGuid().ToString("N"));
            control = new ControlPipeServer(
                instance.ControlPipeName, sessions, canExitCompanion: () => false);
            control.Start();

            var profile = VerifiedStartupProfile.Load(options.Bundle, options.Game);
            boot = new StartupBootController();
            movies = new FullRunMovieCoordinator(boot);
            var gate = movies.PrepareLaunch();
            var launcher = new VerifiedGameLauncher(profile);
            handle = await launcher.LaunchInteractiveAsync(
                "world-observe-" + Guid.NewGuid().ToString("N"),
                TimeSpan.FromSeconds(60), linked.Token, gate);
            resources.Start(handle);
            await WaitForGateAsync(gate, handle, linked.Token);

            var movie = LoadMovie(options.Fixture);
            movies.ArmReplay(movie, 1);
            var first = await movies.StepAsync(0, linked.Token);
            Require(first.Mode != "Fault" && gate.IsWaiting, "startup frame 1 reached");
            AppendStatus(report, "frame-1", gate, movies);

            // A directly supplied bootstrap channel remains supported for a
            // formal launcher invocation. In the ordinary installed setup the
            // Companion forwards the same registration over ControlPipeServer.
            var bootstrap = await BootstrapRegistrationReader.TryReadAsync(args, linked.Token);
            if (bootstrap != null)
            {
                if (!await sessions.RegisterAsync(bootstrap, linked.Token))
                    throw new InvalidOperationException(
                        "Formal bootstrap registration failed: "
                        + sessions.LastRegistrationError);
            }
            runtime = await WaitForRuntimeSessionAsync(
                sessions, handle.ProcessId, linked.Token);
            report["sessionId"] = runtime.SessionId;
            report["gameProcessId"] = runtime.GameProcessId;
            report["runtimeAssemblySha256"] = runtime.RuntimeAssemblySha256;
            report["coreAssemblySha256"] = runtime.CoreAssemblySha256;

            await RecordStatusAsync(report, runtime, "frame-1", linked.Token);
            if (options.Mode == "observe")
            {
                await ObserveFrameAsync(report, runtime, movies, gate, 1, "title", true, linked.Token);
                await MoveToFrameAsync(runtime, movies, gate, 1500, linked.Token);
                await RecordStatusAsync(report, runtime, "frame-1500", linked.Token);
                await ObserveFrameAsync(report, runtime, movies, gate, 1500, "sanctum", true, linked.Token);
                await MoveToFrameAsync(runtime, movies, gate, 8500, linked.Token);
                await RecordStatusAsync(report, runtime, "frame-8500", linked.Token);
                await ObserveFrameAsync(report, runtime, movies, gate, 8500, "boss", true, linked.Token);
            }
            else
            {
                await MoveToFrameAsync(runtime, movies, gate, 1500, linked.Token);
                await RecordStatusAsync(report, runtime, "frame-1500", linked.Token);
                await MoveToFrameAsync(runtime, movies, gate, 8500, linked.Token);
                await RecordStatusAsync(report, runtime, "frame-8500", linked.Token);
            }

            await MoveToFrameAsync(runtime, movies, gate, movie.Runs.Sum(run => (long)run.RepeatCount), linked.Token);
            var completed = await RecordStatusAsync(report, runtime, "completed", linked.Token);
            Require(gate.IsFullRunFinished, "full-run completed");
            Require(gate.FullRunFaultCode == 0, "full-run fault=0");
            Require(string.Equals(ReadField(completed, "mode"), "Completed", StringComparison.OrdinalIgnoreCase),
                "final status Completed");
            Require(ReadInt64(completed, "mismatchCount", -1) == 0, "final mismatch=0");
            movies.VerifyOriginalSavesUnchanged();
            AddCheck(report, "original saves unchanged before quit");
            await RuntimeCommandAsync(runtime, IpcMessageTypes.QuitGame,
                IpcMessageTypes.CommandAccepted,
                new Dictionary<string, string> { ["requestId"] = RequestId() }, linked.Token);
            await WaitForExitAsync(handle, linked.Token);
            report["success"] = true;
            report["elapsedSeconds"] = started.Elapsed.TotalSeconds;
            WriteReport(options.Output, report);
            return 0;
        }
        catch (Exception exception)
        {
            report["success"] = false;
            report["error"] = exception.GetType().Name + ": " + exception.Message;
            report["elapsedSeconds"] = started.Elapsed.TotalSeconds;
            WriteReport(options.Output, report);
            return 3;
        }
        finally
        {
            try { if (movies != null) movies.VerifyOriginalSavesUnchanged(); } catch { }
            runtime?.Dispose();
            control?.Dispose();
            sessions?.Dispose();
            instance?.Dispose();
            boot?.Dispose();
            // The launch handle is the only process handle this harness owns.
            // Keeping supervision until the normal quit lets Dispose terminate it on failure.
            handle?.Dispose();
            resources.Stop();
            currentReport = null;
        }
    }

    private static async Task<RuntimeSessionClient> WaitForRuntimeSessionAsync(
        SessionRegistry sessions, int processId, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow <= deadline)
        {
            token.ThrowIfCancellationRequested();
            var session = sessions.Sessions.FirstOrDefault(
                item => item.GameProcessId == processId && item.IsConnected);
            if (session != null) return session;
            await Task.Delay(100, token);
        }

        throw new TimeoutException(
            "No connected Runtime session was registered through the formal Companion control pipe.");
    }

    private static MovieV2Document LoadMovie(string path)
    {
        using var reader = File.OpenText(Path.GetFullPath(path));
        var parsed = new MovieV2Codec().Parse(reader, path);
        if (!parsed.Success || parsed.Document == null)
            throw new InvalidDataException("Fixture is not a canonical v2 movie.");
        return parsed.Document;
    }

    private static async Task WaitForGateAsync(StartupBootGate gate,
        VerifiedGameLaunchHandle handle, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!gate.IsAcknowledged)
        {
            token.ThrowIfCancellationRequested();
            if (handle.HasExited) throw new InvalidOperationException("Owned game exited before gate acknowledgement.");
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Full-run startup gate acknowledgement timed out.");
            await Task.Delay(50, token);
        }
        Require(gate.NativeCompletedFrames == 0 && gate.SaveGuardArmed == 1,
            "protected native frame zero and save guard");
    }

    private static async Task MoveToFrameAsync(RuntimeSessionClient runtime,
        FullRunMovieCoordinator movies, StartupBootGate gate, long target,
        CancellationToken token)
    {
        if (gate.IsFullRunFinished) return;
        var status = await RuntimeCommandAsync(runtime, IpcMessageTypes.FullRunStatus,
            IpcMessageTypes.FullRunState,
            new Dictionary<string, string> { ["requestId"] = RequestId() }, token);
        var current = ReadInt64(status, "movieFrame", -1);
        if (current == target) return;
        if (current < 0 || target <= current)
            throw new InvalidOperationException($"Cannot advance from movie={current} to target={target}.");
        var expectedNative = ReadInt64(status, "nativeFrame", gate.NativeCompletedFrames);
        await RuntimeCommandAsync(runtime, IpcMessageTypes.FullRunSeek,
            IpcMessageTypes.CommandAccepted,
            new Dictionary<string, string>
            {
                ["requestId"] = RequestId(),
                ["targetFrame"] = target.ToString(CultureInfo.InvariantCulture),
                ["expectedNativeFrame"] = expectedNative.ToString(CultureInfo.InvariantCulture)
            }, token);
        var boundary = await movies.RunAsync(gate.NativeCompletedFrames, token);
        Require(boundary.Mode != "Fault" && gate.FullRunFaultCode == 0,
            "target frame " + target + " reached without native fault");
        var completed = await RuntimeCommandAsync(runtime, IpcMessageTypes.FullRunStatus,
            IpcMessageTypes.FullRunState,
            new Dictionary<string, string> { ["requestId"] = RequestId() }, token);
        Require(ReadInt64(completed, "movieFrame", -1) == target,
            "movie frame " + target + " boundary");
    }

    private static async Task ObserveFrameAsync(Dictionary<string, object?> report,
        RuntimeSessionClient runtime, FullRunMovieCoordinator movies, StartupBootGate gate,
        long expectedMovieFrame, string label, bool includeDetails, CancellationToken token)
    {
        var world = await ReadSnapshotAsync(report, runtime, expectedMovieFrame,
            label + "-world", "world", token);
        RecordScene(report, label, world.Snapshot.RootElement);
        var snapshot = world.Snapshot;
        Require(snapshot.RootElement.TryGetProperty("metadata", out _), label + " metadata");
        Require(snapshot.RootElement.TryGetProperty("objects", out var objects), label + " objects");
        Require(objects.GetArrayLength() > 0, label + " has objects");
        // The title/startup frame can legitimately expose only scene metadata
        // while Unity finishes loading the player object. Keep that observed
        // state in the report; later in-world milestones require the hero.
        if (!string.Equals(label, "title", StringComparison.OrdinalIgnoreCase))
            Require(FindObject(objects, "hero") != null, label + " hero object");
        Require(FindObject(objects, "enemy") != null || label != "boss",
            label + " enemy/boss object");
        if (label == "sanctum")
        {
            var colliders = await ReadSnapshotAsync(report, runtime, expectedMovieFrame,
                label + "-colliders", "colliders", token);
            RecordScene(report, label + "-colliders", colliders.Snapshot.RootElement);
            var colliderRoot = colliders.Snapshot;
            Require(colliderRoot.RootElement.TryGetProperty("objects", out var colliderObjects)
                    && colliderObjects.GetArrayLength() > 0,
                label + " collider objects");
            Require(colliderObjects.EnumerateArray().Any(item =>
                item.TryGetProperty("colliders", out var values) && values.GetArrayLength() > 0),
                label + " collider fields");
        }
        if (!includeDetails) return;
        var nativeFrame = ReadInt64(world.Fields, "nativeFrame", -1);
        var hero = FindObject(objects, "hero");
        var enemy = FindObject(objects, "enemy");
        if (hero != null)
            await ObserveDetailsAsync(report, runtime, hero.Value, expectedMovieFrame,
                nativeFrame, label + "-hero", token);
        if (enemy != null)
            await ObserveDetailsAsync(report, runtime, enemy.Value, expectedMovieFrame,
                nativeFrame, label + "-enemy", token);
    }

    private static async Task<SnapshotRead> ReadSnapshotAsync(
        Dictionary<string, object?> report, RuntimeSessionClient runtime,
        long expectedMovieFrame, string label, string view, CancellationToken token)
    {
        var first = await QueryAsync(runtime, GetWorldSnapshot, WorldSnapshot,
            new Dictionary<string, string>
            {
                ["requestId"] = RequestId(), ["snapshotId"] = string.Empty,
                ["view"] = view, ["includeInactive"] = "false",
                ["offset"] = "0", ["limit"] = "64"
            }, label + "-page-0", token);
        AssertFrame(first, expectedMovieFrame, label);
        SaveField(report, label + "-page-0", first, "snapshotJson");
        var snapshotId = ReadField(first, "snapshotId");
        var firstJson = ReadField(first, "snapshotJson");
        using var firstDocument = JsonDocument.Parse(firstJson);
        var root = firstDocument.RootElement;
        Require(root.TryGetProperty("metadata", out var metadata), label + " metadata");
        Require(root.TryGetProperty("objects", out var firstObjects), label + " objects");
        var total = ReadJsonInt64(root, "total", firstObjects.GetArrayLength());
        var allObjects = firstObjects.EnumerateArray().Select(value => value.Clone()).ToList();
        var next = ReadJsonInt64(root, "nextOffset", -1);
        var offset = 0;
        var page = 1;
        while (next >= 0)
        {
            if (next <= offset) throw new InvalidDataException(label + " snapshot pagination did not advance.");
            offset = checked((int)next);
            var fields = await QueryAsync(runtime, GetWorldSnapshot, WorldSnapshot,
                new Dictionary<string, string>
                {
                    ["requestId"] = RequestId(), ["snapshotId"] = snapshotId,
                    ["view"] = view, ["includeInactive"] = "false",
                    ["offset"] = offset.ToString(CultureInfo.InvariantCulture), ["limit"] = "64"
                }, label + "-page-" + page, token);
            AssertFrame(fields, expectedMovieFrame, label + " page " + page);
            Require(ReadField(fields, "snapshotId") == snapshotId, label + " snapshot id changed");
            var pageJson = ReadField(fields, "snapshotJson");
            SaveField(report, label + "-page-" + page, fields, "snapshotJson");
            using var pageDocument = JsonDocument.Parse(pageJson);
            var pageRoot = pageDocument.RootElement;
            Require(ReadJsonInt64(pageRoot, "total", total) == total, label + " total changed");
            if (pageRoot.TryGetProperty("objects", out var pageObjects))
                allObjects.AddRange(pageObjects.EnumerateArray().Select(value => value.Clone()));
            next = ReadJsonInt64(pageRoot, "nextOffset", -1);
            page++;
        }

        Require(allObjects.Count == total, label + " object total");
        var combined = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = root.TryGetProperty("schemaVersion", out var schema) ? schema.Clone() : 1,
            ["snapshotId"] = snapshotId,
            ["nativeFrame"] = ReadInt64(first, "nativeFrame", -1),
            ["movieFrame"] = ReadInt64(first, "movieFrame", -1),
            ["metadata"] = metadata.Clone(), ["total"] = total, ["offset"] = 0,
            ["objects"] = allObjects, ["nextOffset"] = -1
        });
        File.WriteAllText(Path.Combine((string)report["output"]!, label + ".json"), combined);
        return new SnapshotRead(first, JsonDocument.Parse(combined));
    }

    private static void RecordScene(Dictionary<string, object?> report,
        string label, JsonElement snapshotRoot)
    {
        string name = "";
        string path = "";
        if (snapshotRoot.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("activeScene", out var scene))
        {
            if (scene.TryGetProperty("name", out var nameValue)) name = nameValue.GetString() ?? "";
            if (scene.TryGetProperty("path", out var pathValue)) path = pathValue.GetString() ?? "";
        }
        ((List<object>)report["scenes"]!).Add(new Dictionary<string, object?>
        {
            ["label"] = label, ["activeScene"] = name, ["scenePath"] = path,
            ["note"] = "Actual scene observed at this query; inspect this row if the milestone is still loading or differs from the expected label."
        });
    }

    private sealed class SnapshotRead
    {
        public SnapshotRead(Dictionary<string, string> fields, JsonDocument snapshot)
        {
            Fields = fields; Snapshot = snapshot;
        }
        public Dictionary<string, string> Fields { get; }
        public JsonDocument Snapshot { get; }
    }

    private static async Task ObserveDetailsAsync(Dictionary<string, object?> report,
        RuntimeSessionClient runtime, JsonElement objectValue, long expectedMovieFrame,
        long expectedNativeFrame, string label, CancellationToken token)
    {
        if (!objectValue.TryGetProperty("id", out var idValue)
            || idValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(label + " lacks object id.");
        var objectId = idValue.GetString()!;
        var fragments = new List<string>();
        string detailsId = string.Empty;
        string expectedHash = string.Empty;
        var cursor = 0;
        while (true)
        {
            var fields = new Dictionary<string, string>
            {
                ["requestId"] = RequestId(), ["objectId"] = objectId,
                ["cursor"] = cursor.ToString(CultureInfo.InvariantCulture),
                ["maxCharacters"] = "100000"
            };
            fields["expectedNativeFrame"] = expectedNativeFrame.ToString(CultureInfo.InvariantCulture);
            if (detailsId.Length != 0) fields["detailsId"] = detailsId;
            var result = await QueryAsync(runtime, GetObjectDetails,
                ObjectDetails, fields, label + "-part-" + fragments.Count, token);
            AssertFrame(result, expectedMovieFrame, label);
            var pageDetailsId = ReadField(result, "detailsId");
            if (detailsId.Length != 0) Require(pageDetailsId == detailsId, label + " details id changed");
            detailsId = pageDetailsId;
            Require(ReadField(result, "objectId") == objectId, label + " object id changed");
            var pageHash = ReadField(result, "sha256");
            if (expectedHash.Length != 0) Require(pageHash == expectedHash, label + " sha256 changed");
            expectedHash = pageHash;
            fragments.Add(ReadField(result, "detailsJson"));
            SaveField(report, label + ".part-" + (fragments.Count - 1), result, "detailsJson");
            if (ReadField(result, "complete") == "true") break;
            var next = ReadInt64(result, "nextCursor", -1);
            if (next < 0 || next <= cursor) throw new InvalidDataException(label + " pagination did not advance.");
            cursor = checked((int)next);
        }
        var json = string.Concat(fragments);
        using var parsed = JsonDocument.Parse(json);
        Require(parsed.RootElement.TryGetProperty("components", out _)
                || parsed.RootElement.TryGetProperty("object", out _), label + " detail fields");
        using var sha = SHA256.Create();
        var actualHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        Require(actualHash == expectedHash, label + " sha256");
        File.WriteAllText(Path.Combine((string)report["output"]!, label + ".json"), json);
    }

    private static async Task<Dictionary<string, string>> QueryAsync(RuntimeSessionClient runtime,
        string command, string response, IReadOnlyDictionary<string, string> fields,
        string label, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await RuntimeCommandAsync(runtime, command, response, fields, token);
        stopwatch.Stop();
        SaveQueryTiming(label, stopwatch.Elapsed.TotalMilliseconds);
        return result;
    }

    private static async Task<Dictionary<string, string>> RuntimeCommandAsync(
        RuntimeSessionClient runtime, string command, string response,
        IReadOnlyDictionary<string, string> fields, CancellationToken token)
    {
        var requestId = fields.TryGetValue("requestId", out var value) ? value : RequestId();
        var completion = new TaskCompletionSource<Dictionary<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEnvelope(object? _, IpcEnvelope envelope)
        {
            if (envelope.MessageType != response && envelope.MessageType != IpcMessageTypes.CommandRejected) return;
            var payload = IpcPayloadCodec.TryDeserialize(envelope.PayloadUtf8);
            if (!payload.Success || payload.Fields == null
                || !payload.Fields.TryGetValue("requestId", out var received)
                || received != requestId) return;
            if (envelope.MessageType == IpcMessageTypes.CommandRejected)
                completion.TrySetException(new InvalidOperationException(
                    payload.Fields.TryGetValue("detail", out var detail) ? detail : "Runtime command rejected."));
            else completion.TrySetResult(new Dictionary<string, string>(payload.Fields, StringComparer.Ordinal));
        }
        runtime.EnvelopeReceived += OnEnvelope;
        try
        {
            await runtime.SendCommandAsync(command, fields, token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally { runtime.EnvelopeReceived -= OnEnvelope; }
    }

    private static void AppendStatus(Dictionary<string, object?> report, string label,
        StartupBootGate gate, FullRunMovieCoordinator movies)
        => ((List<object>)report["status"]!).Add(new Dictionary<string, object?>
        {
            ["label"] = label, ["nativeFrame"] = gate.NativeCompletedFrames,
            ["movieFrame"] = -1, ["mode"] = movies.Mode,
            ["faultCode"] = gate.FullRunFaultCode, ["waiting"] = gate.IsWaiting
        });

    private static async Task<Dictionary<string, string>> RecordStatusAsync(Dictionary<string, object?> report,
        RuntimeSessionClient runtime, string label, CancellationToken token)
    {
        var fields = await RuntimeCommandAsync(runtime, IpcMessageTypes.FullRunStatus,
            IpcMessageTypes.FullRunState,
            new Dictionary<string, string> { ["requestId"] = RequestId() }, token);
        ((List<object>)report["status"]!).Add(fields.ToDictionary(
            pair => pair.Key, pair => (object?)pair.Value,
            StringComparer.Ordinal));
        ((List<object>)report["status"]!).Add(new Dictionary<string, object?>
        {
            ["label"] = label, ["nativeFrame"] = ReadInt64(fields, "nativeFrame", -1),
            ["movieFrame"] = ReadInt64(fields, "movieFrame", -1),
            ["mode"] = ReadField(fields, "mode"), ["faultCode"] = ReadInt64(fields, "faultCode", 0),
            ["mismatchCount"] = ReadInt64(fields, "mismatchCount", 0)
        });
        return fields;
    }

    private static void SaveField(Dictionary<string, object?> report, string label,
        Dictionary<string, string> fields, string field)
    {
        if (!fields.TryGetValue(field, out var value)) throw new InvalidDataException(label + " omitted " + field);
        var output = (string)report["output"]!;
        File.WriteAllText(Path.Combine(output, label + ".json"), value);
    }

    private static JsonDocument ParseObject(Dictionary<string, string> fields, string name)
        => JsonDocument.Parse(ReadField(fields, name));

    private static JsonElement? FindObject(JsonElement objects, string kind)
    {
        foreach (var item in objects.EnumerateArray())
            if (item.TryGetProperty("kind", out var value)
                && value.GetString()?.Equals(kind, StringComparison.OrdinalIgnoreCase) == true)
                return item;
        return null;
    }

    private static void AssertFrame(Dictionary<string, string> fields, long movieFrame, string label)
    {
        Require(ReadInt64(fields, "movieFrame", -1) == movieFrame, label + " movie frame");
        Require(ReadInt64(fields, "nativeFrame", -1) >= 0, label + " native frame");
    }

    private static long ReadFrame(JsonElement value, string name, long fallback)
        => value.TryGetProperty(name, out var field) && field.TryGetInt64(out var parsed) ? parsed : fallback;

    private static long ReadJsonInt64(JsonElement value, string name, long fallback)
        => value.TryGetProperty(name, out var field) && field.TryGetInt64(out var parsed)
            ? parsed : fallback;

    private static string ReadField(Dictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out var value) ? value : throw new InvalidDataException("Missing field " + name);

    private static long ReadInt64(Dictionary<string, string> fields, string name, long fallback)
        => fields.TryGetValue(name, out var value)
           && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;

    private static string RequestId() => "world-observe-" + Guid.NewGuid().ToString("N");

    private static void Require(bool value, string detail)
    {
        if (!value) throw new InvalidDataException(detail);
    }

    private static void AddCheck(Dictionary<string, object?> report, string detail)
        => ((List<string>)report["checks"]!).Add(detail);

    private static void SaveQueryTiming(string label, double milliseconds)
    {
        if (currentReport == null) return;
        ((List<object>)currentReport["queries"]!).Add(
            new Dictionary<string, object?>
            {
                ["label"] = label,
                ["milliseconds"] = milliseconds
            });
    }

    private static void WriteReport(string output, Dictionary<string, object?> report)
    {
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, JsonOptions));
    }

    private static async Task WaitForExitAsync(VerifiedGameLaunchHandle handle, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!handle.HasExited)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Owned game did not exit after quit.");
            await Task.Delay(100, token);
        }
    }

    private sealed class ResourceMonitor
    {
        private readonly Dictionary<string, object?> report;
        private readonly CancellationToken token;
        private VerifiedGameLaunchHandle? handle;
        private Task? task;
        public ResourceMonitor(Dictionary<string, object?> report, CancellationToken token) { this.report = report; this.token = token; }
        public void Start(VerifiedGameLaunchHandle value) { handle = value; task = Task.Run(RunAsync); }
        public void Stop() { try { task?.Wait(TimeSpan.FromSeconds(1)); } catch { } }
        private async Task RunAsync()
        {
            while (!token.IsCancellationRequested)
            {
                using var process = Process.GetCurrentProcess();
                process.Refresh();
                long gameBytes = 0;
                try { using var game = Process.GetProcessById(handle!.ProcessId); game.Refresh(); gameBytes = game.PrivateMemorySize64; } catch { }
                ((List<object>)report["resources"]!).Add(new Dictionary<string, object?>
                {
                    ["utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["hostPrivateBytes"] = process.PrivateMemorySize64,
                    ["gamePrivateBytes"] = gameBytes
                });
                if (process.PrivateMemorySize64 > 1024L * 1024 * 1024 || gameBytes > 3L * 1024 * 1024 * 1024)
                    throw new InvalidOperationException("Resource limit exceeded.");
                try { await Task.Delay(1000, token); } catch (OperationCanceledException) { }
            }
        }
    }

    private sealed class Options
    {
        public string Output = Path.Combine("artifacts", "world-observation");
        public string Mode = "observe";
        public string Fixture = Path.Combine("fixtures", "full-run", "false-knight-startup-v2.hktas");
        public string Game = DefaultGame;
        public string Bundle = DefaultBundle;
        public static Options Parse(string[] args)
        {
            var result = new Options();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--output=", StringComparison.Ordinal)) result.Output = Path.GetFullPath(arg.Substring(9));
                else if (arg.StartsWith("--mode=", StringComparison.Ordinal)) result.Mode = arg.Substring(7);
                else if (arg.StartsWith("--fixture=", StringComparison.Ordinal)) result.Fixture = Path.GetFullPath(arg.Substring(10));
                else if (arg.StartsWith("--game=", StringComparison.Ordinal)) result.Game = Path.GetFullPath(arg.Substring(7));
                else if (arg.StartsWith("--bundle=", StringComparison.Ordinal)) result.Bundle = Path.GetFullPath(arg.Substring(9));
            }
            if (result.Mode != "baseline" && result.Mode != "observe") throw new ArgumentException("--mode must be baseline or observe.");
            return result;
        }
    }
}
