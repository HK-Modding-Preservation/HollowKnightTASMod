using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Automation.Client
{
    public sealed class AutomationClient : IAutomationClient
    {
        private readonly SemaphoreSlim requestLock =
            new SemaphoreSlim(1, 1);
        private NamedPipeClientStream? pipe;
        private string clientId = string.Empty;
        private string sessionId = string.Empty;
        private string manifestSha256 = string.Empty;
        private long sequence;

        public bool IsConnected =>
            pipe?.IsConnected == true;
        public string ClientId => clientId;
        public string SessionId => sessionId;
        public string ManifestSha256 => manifestSha256;

        public async Task<AutomationHandshake> ConnectAsync(
            AutomationConnectOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                throw new InvalidOperationException(
                    "Automation client is already connected.");
            }

            options ??= new AutomationConnectOptions();
            if (!IpcIdentifier.IsValid(options.ClientId, 128))
            {
                throw new ArgumentException(
                    "Client ID is invalid.",
                    nameof(options));
            }

            var bootstrapBytes = await File.ReadAllBytesAsync(
                Path.GetFullPath(options.BootstrapPath),
                cancellationToken);
            if (!AutomationBootstrapDescriptor.TryParse(
                    bootstrapBytes,
                    out var bootstrap,
                    out var bootstrapError)
                || bootstrap == null)
            {
                throw new InvalidDataException(
                    "Automation bootstrap is invalid: "
                    + bootstrapError);
            }

            using (var timeout =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                timeout.CancelAfter(options.Timeout);
                var connected = new NamedPipeClientStream(
                    ".",
                    bootstrap.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous
                    | PipeOptions.CurrentUserOnly);
                try
                {
                    await connected.ConnectAsync(timeout.Token);
                    var nonce = new byte[32];
                    using (var random =
                           RandomNumberGenerator.Create())
                    {
                        random.GetBytes(nonce);
                    }

                    await IpcCodec.WriteFrameAsync(
                        connected,
                        new IpcEnvelope(
                            AutomationProtocol.Version,
                            bootstrap.SessionId,
                            0,
                            AutomationProtocol.Hello,
                            AutomationHandshakeCodec.CreateHello(
                                options.ClientId,
                                bootstrap.SessionId,
                                bootstrap.ManifestSha256,
                                nonce,
                                bootstrap.Token)),
                        timeout.Token);
                    var ack = await IpcCodec.ReadFrameAsync(
                        connected,
                        timeout.Token);
                    ValidateAck(ack, bootstrap, options.ClientId);
                    pipe = connected;
                    connected = null!;
                    clientId = options.ClientId;
                    sessionId = bootstrap.SessionId;
                    manifestSha256 = bootstrap.ManifestSha256;
                    sequence = 0;
                    return new AutomationHandshake(
                        clientId,
                        sessionId,
                        manifestSha256,
                        bootstrap.Mode.ToString());
                }
                finally
                {
                    connected?.Dispose();
                }
            }
        }

        public AutomationCommandEnvelope CreateCommand(
            string commandId,
            string requiredScope,
            IReadOnlyDictionary<string, string>? arguments = null,
            string? leaseId = null,
            string? expectedRuntimeMode = null,
            long? expectedMovieTick = null,
            string? requestId = null,
            string? idempotencyKey = null)
        {
            RequireConnected();
            return new AutomationCommandEnvelope(
                requestId
                ?? "request-" + Guid.NewGuid().ToString("N"),
                idempotencyKey
                ?? "idempotency-" + Guid.NewGuid().ToString("N"),
                clientId,
                sessionId,
                manifestSha256,
                commandId,
                requiredScope,
                leaseId ?? string.Empty,
                expectedRuntimeMode ?? string.Empty,
                expectedMovieTick,
                IpcPayloadCodec.Serialize(
                    arguments
                    ?? new Dictionary<string, string>(
                        StringComparer.Ordinal)));
        }

        public async Task<AutomationResultEnvelope> ExecuteAsync(
            AutomationCommandEnvelope command,
            CancellationToken cancellationToken = default)
        {
            RequireConnected();
            if (command.ClientId != clientId
                || command.SessionId != sessionId
                || command.ManifestSha256 != manifestSha256)
            {
                throw new InvalidOperationException(
                    "Command is bound to a different automation session.");
            }

            await requestLock.WaitAsync(cancellationToken);
            try
            {
                var active = pipe
                             ?? throw new InvalidOperationException(
                                 "Automation pipe is unavailable.");
                var current = checked(++sequence);
                await IpcCodec.WriteFrameAsync(
                    active,
                    new IpcEnvelope(
                        AutomationProtocol.Version,
                        sessionId,
                        current,
                        AutomationProtocol.Command,
                        command.ToPayload()),
                    cancellationToken);
                var response = await IpcCodec.ReadFrameAsync(
                    active,
                    cancellationToken);
                var parsed =
                    AutomationResultEnvelope.TryParse(
                        response.PayloadUtf8,
                        out var result,
                        out var error);
                if (response.ProtocolVersion
                    != AutomationProtocol.Version
                    || response.SessionId != sessionId
                    || response.Sequence != current
                    || response.MessageType
                    != AutomationProtocol.Result
                    || !parsed
                    || result == null)
                {
                    throw new InvalidDataException(
                        "Automation response is invalid: " + error);
                }

                return result;
            }
            finally
            {
                requestLock.Release();
            }
        }

        public Task<AutomationResultEnvelope> GetOperationalStatusAsync(
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(CreateCommand(AutomationCommandIds.GetState,
                AutomationScope.ObserveStateSummary,
                new Dictionary<string, string> { ["statusOnly"] = "true" }), cancellationToken);
        }

        public async Task<AutomationStateEnvelope> GetStateAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.GetState,
                    AutomationScope.ObserveStateSummary),
                cancellationToken);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.ResultCode + ": " + result.Detail);
            }

            if (!result.Data.TryGetValue(
                    "stateJson",
                    out var stateJson))
            {
                throw new InvalidDataException(
                    "Automation response omitted stateJson.");
            }

            return AutomationStateParser.Parse(stateJson);
        }

        public async Task<AutomationSemanticState> GetSemanticStateAsync(
            CancellationToken cancellationToken = default)
        {
            var state = await GetStateAsync(cancellationToken);
            if (!state.Fields.TryGetValue(
                    "semanticSnapshotJson",
                    out var snapshotJson))
            {
                throw new InvalidDataException(
                    "Automation state omitted semanticSnapshotJson.");
            }

            return new AutomationSemanticState(
                state,
                AutomationSemanticSnapshot.Parse(snapshotJson));
        }

        public Task<AutomationResultEnvelope> GetCombatStateAsync(
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.GetCombatState,
                    AutomationScope.ObserveStateDeep),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> GetWorldSnapshotAsync(
            string? snapshotId = null,
            string view = "world",
            bool includeInactive = false,
            int offset = 0,
            int limit = 64,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(snapshotId)
                && !IpcIdentifier.IsValid(snapshotId, 96))
                throw new ArgumentException("Snapshot ID is invalid.", nameof(snapshotId));
            if (view != "world" && view != "all" && view != "colliders")
                throw new ArgumentOutOfRangeException(nameof(view));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (limit < 1 || limit > 128)
                throw new ArgumentOutOfRangeException(nameof(limit));

            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.GetWorldSnapshot,
                    AutomationScope.ObserveStateDeep,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["snapshotId"] = snapshotId ?? string.Empty,
                        ["view"] = view,
                        ["includeInactive"] = includeInactive ? "true" : "false",
                        ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
                        ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
                    }),
                cancellationToken);
        }

        public async Task<string> GetWorldSnapshotJsonAsync(
            string? snapshotId = null,
            string view = "world",
            bool includeInactive = false,
            int limit = 64,
            CancellationToken cancellationToken = default)
        {
            if (limit < 1 || limit > 128)
                throw new ArgumentOutOfRangeException(nameof(limit));

            var offset = 0;
            string? activeSnapshotId = snapshotId;
            JsonObject? merged = null;
            var mergedObjects = new JsonArray();
            long expectedTotal = -1;
            while (true)
            {
                var result = await GetWorldSnapshotAsync(
                    activeSnapshotId,
                    view,
                    includeInactive,
                    offset,
                    limit,
                    cancellationToken);
                EnsureSuccessful(result);
                var page = ParseObject(result, "snapshotJson");
                if (merged == null)
                {
                    merged = (JsonObject)page.DeepClone();
                    merged.Remove("objects");
                }
                if (page["objects"] is JsonArray objects)
                {
                    foreach (var item in objects)
                        mergedObjects.Add(item?.DeepClone());
                }
                expectedTotal = ReadInt64(page, "total", expectedTotal);
                activeSnapshotId = ReadField(result, "snapshotId");
                var nextOffset = ReadInt64(page, "nextOffset", -1);
                if (nextOffset < 0)
                    break;
                if (activeSnapshotId.Length == 0 || nextOffset <= offset)
                    throw new InvalidDataException("World snapshot pagination is invalid.");
                offset = checked((int)nextOffset);
            }

            if (merged == null)
                throw new InvalidDataException("World snapshot response was empty.");
            merged["objects"] = mergedObjects;
            if (expectedTotal >= 0)
                merged["total"] = expectedTotal;
            merged["nextOffset"] = -1;
            return merged.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        public Task<AutomationResultEnvelope> GetObjectDetailsAsync(
            string objectId,
            long? expectedNativeFrame = null,
            string? detailsId = null,
            int cursor = 0,
            int maxCharacters = 100000,
            CancellationToken cancellationToken = default)
        {
            if (!IpcIdentifier.IsValid(objectId, 128))
                throw new ArgumentException("Object ID is invalid.", nameof(objectId));
            if (!string.IsNullOrEmpty(detailsId)
                && !IpcIdentifier.IsValid(detailsId, 96))
                throw new ArgumentException("Details ID is invalid.", nameof(detailsId));
            if (expectedNativeFrame.HasValue && expectedNativeFrame.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedNativeFrame));
            if (cursor < 0)
                throw new ArgumentOutOfRangeException(nameof(cursor));
            if (maxCharacters < 1024 || maxCharacters > 200000)
                throw new ArgumentOutOfRangeException(nameof(maxCharacters));

            var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["objectId"] = objectId,
                ["cursor"] = cursor.ToString(CultureInfo.InvariantCulture),
                ["maxCharacters"] = maxCharacters.ToString(CultureInfo.InvariantCulture)
            };
            if (expectedNativeFrame.HasValue)
                arguments["expectedNativeFrame"] = expectedNativeFrame.Value.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(detailsId))
                arguments["detailsId"] = detailsId;
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.GetObjectDetails,
                    AutomationScope.ObserveStateDeep,
                    arguments),
                cancellationToken);
        }

        public async Task<string> GetObjectDetailsJsonAsync(
            string objectId,
            long? expectedNativeFrame = null,
            int maxCharacters = 100000,
            CancellationToken cancellationToken = default)
        {
            var builder = new System.Text.StringBuilder();
            string? detailsId = null;
            var cursor = 0;
            string? expectedSha256 = null;
            var totalCharacters = -1L;
            while (true)
            {
                var result = await GetObjectDetailsAsync(
                    objectId,
                    expectedNativeFrame,
                    detailsId,
                    cursor,
                    maxCharacters,
                    cancellationToken);
                EnsureSuccessful(result);
                if (ReadField(result, "objectId") != objectId)
                    throw new InvalidDataException("Object details response changed objectId.");
                var fragment = ReadField(result, "detailsJson");
                builder.Append(fragment);
                detailsId = ReadField(result, "detailsId");
                expectedSha256 = ReadField(result, "sha256");
                totalCharacters = ReadInt64(result, "totalCharacters", totalCharacters);
                var complete = ReadField(result, "complete") == "true";
                var nextCursor = ReadInt64(result, "nextCursor", -1);
                if (complete || nextCursor < 0)
                    break;
                if (detailsId.Length == 0 || nextCursor <= cursor)
                    throw new InvalidDataException("Object details pagination is invalid.");
                cursor = checked((int)nextCursor);
                expectedNativeFrame = null;
            }

            var json = builder.ToString();
            if (totalCharacters >= 0 && json.Length != totalCharacters)
                throw new InvalidDataException("Object details character count does not match.");
            if (string.IsNullOrEmpty(expectedSha256)
                || Sha256Utility.ComputeUtf8Hex(json) != expectedSha256)
                throw new InvalidDataException("Object details SHA-256 does not match.");
            return json;
        }

        public Task<AutomationResultEnvelope> GetReplaySavesAsync(
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.GetReplaySaves,
                    AutomationScope.ObserveReplaySaves),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> GetMovieAsync(
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.GetMovie,
                    AutomationScope.MovieRead),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> GetLifecycleMovieAsync(CancellationToken cancellationToken = default) =>
            ExecuteAsync(CreateCommand(AutomationCommandIds.GetMovie, AutomationScope.MovieRead,
                new Dictionary<string, string> { ["includeLifecycle"] = "true" }), cancellationToken);

        public Task<AutomationResultEnvelope> ListMovieBranchesAsync(int offset = 0, CancellationToken cancellationToken = default)
        {
            if (offset < 0 || offset > int.MaxValue - 50) throw new ArgumentOutOfRangeException(nameof(offset));
            return ExecuteAsync(CreateCommand(AutomationCommandIds.GetMovie, AutomationScope.MovieRead,
                new Dictionary<string, string> { ["listBranches"] = "true", ["branchOffset"] = offset.ToString(CultureInfo.InvariantCulture) }), cancellationToken);
        }

        public Task<AutomationResultEnvelope> GetLifecycleMovieChunkAsync(string exportId, int chunkIndex,
            CancellationToken cancellationToken = default)
        {
            if (!HollowKnightTAS.Core.Movie.MovieProtocolV1.IsLowerSha256(exportId))
                throw new ArgumentException("Expected an export SHA-256.", nameof(exportId));
            if (chunkIndex < 0) throw new ArgumentOutOfRangeException(nameof(chunkIndex));
            return ExecuteAsync(CreateCommand(AutomationCommandIds.GetMovie, AutomationScope.MovieRead,
                new Dictionary<string, string> { ["includeLifecycle"] = "true", ["exportId"] = exportId,
                    ["exportChunk"] = chunkIndex.ToString(CultureInfo.InvariantCulture) }), cancellationToken);
        }

        public Task<AutomationResultEnvelope> EditLifecycleInputRangeAsync(
            HollowKnightTAS.Core.Movie.TimelineEditKind kind, string baseMovieId, long startTick,
            long deleteCount, byte[]? canonicalReplacementMovieUtf8, CancellationToken cancellationToken = default)
        {
            var arguments = new Dictionary<string, string> {
                ["includeLifecycle"] = "true", ["baseMovieId"] = baseMovieId,
                ["startTick"] = startTick.ToString(CultureInfo.InvariantCulture)
            };
            string command;
            switch (kind)
            {
                case HollowKnightTAS.Core.Movie.TimelineEditKind.Replace:
                    command = AutomationCommandIds.ReplaceInputRange;
                    arguments["deleteCount"] = deleteCount.ToString(CultureInfo.InvariantCulture);
                    break;
                case HollowKnightTAS.Core.Movie.TimelineEditKind.Insert:
                    if (deleteCount != 0) throw new ArgumentException("Insert cannot delete ticks.", nameof(deleteCount));
                    command = AutomationCommandIds.InsertInputRange;
                    break;
                case HollowKnightTAS.Core.Movie.TimelineEditKind.Delete:
                    if (canonicalReplacementMovieUtf8 != null) throw new ArgumentException("Delete cannot insert inputs.");
                    command = AutomationCommandIds.DeleteInputRange;
                    arguments["count"] = deleteCount.ToString(CultureInfo.InvariantCulture);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (kind != HollowKnightTAS.Core.Movie.TimelineEditKind.Delete)
                arguments["replacementMovieBase64"] = Convert.ToBase64String(canonicalReplacementMovieUtf8
                    ?? throw new ArgumentNullException(nameof(canonicalReplacementMovieUtf8)));
            return ExecuteAsync(CreateCommand(command, AutomationScope.MovieEdit, arguments), cancellationToken);
        }

        public Task<AutomationResultEnvelope> StepWithInputAsync(
            byte[] canonicalOneTickMovieUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInputMovieAsync(
                AutomationCommandIds.StepWithInput,
                canonicalOneTickMovieUtf8,
                expectedSceneEpoch,
                leaseId,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> QueueInputBatchAsync(
            byte[] canonicalInputMovieUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInputMovieAsync(
                AutomationCommandIds.QueueInputBatch,
                canonicalInputMovieUtf8,
                expectedSceneEpoch,
                leaseId,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> BeginInputBatchAsync(
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInputBatchTransactionAsync(
                AutomationCommandIds.BeginInputBatch,
                null,
                null,
                null,
                expectedSceneEpoch,
                leaseId,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> AppendInputBatchAsync(
            string transactionId,
            int chunkIndex,
            byte[] canonicalInputMovieChunkUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            if (canonicalInputMovieChunkUtf8 == null)
            {
                throw new ArgumentNullException(
                    nameof(canonicalInputMovieChunkUtf8));
            }

            return ExecuteInputBatchTransactionAsync(
                AutomationCommandIds.AppendInputBatch,
                transactionId,
                chunkIndex,
                canonicalInputMovieChunkUtf8,
                expectedSceneEpoch,
                leaseId,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> CommitInputBatchAsync(
            string transactionId,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInputBatchTransactionAsync(
                AutomationCommandIds.CommitInputBatch,
                transactionId,
                null,
                null,
                expectedSceneEpoch,
                leaseId,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> CancelInputBatchAsync(
            string transactionId,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInputBatchTransactionAsync(
                AutomationCommandIds.CancelInputBatch,
                transactionId,
                null,
                null,
                expectedSceneEpoch,
                leaseId,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> StepAsync(
            int count,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.Step,
                    AutomationScope.ControlStep,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["count"] = count.ToString(
                            CultureInfo.InvariantCulture)
                    },
                    leaseId,
                    "Paused",
                    expectedMovieTick),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> PauseAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteSimpleControlAsync(
                AutomationCommandIds.Pause,
                AutomationScope.ControlPlayback,
                leaseId,
                expectedRuntimeMode,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> QuitGameAsync(
            string leaseId, string expectedRuntimeMode, long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteSimpleControlAsync(AutomationCommandIds.QuitGame,
                AutomationScope.ControlPlayback, leaseId, expectedRuntimeMode,
                expectedMovieTick, cancellationToken);
        }

        public Task<AutomationResultEnvelope> LoadGameSlotAsync(
            int slot, string leaseId, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(CreateCommand(AutomationCommandIds.LoadGameSlot,
                AutomationScope.ControlPlayback,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["slot"] = slot.ToString(CultureInfo.InvariantCulture)
                }, leaseId, "Running", null), cancellationToken);
        }

        public Task<AutomationResultEnvelope> ReloadGameSlotAsync(int slot, string leaseId, long expectedMovieTick,
            CancellationToken cancellationToken = default)
            => ExecuteAsync(CreateCommand(AutomationCommandIds.ReloadGameSlot, AutomationScope.ControlPlayback,
                new Dictionary<string, string> { ["slot"] = slot.ToString(CultureInfo.InvariantCulture) },
                leaseId, "Paused", expectedMovieTick), cancellationToken);

        public Task<AutomationResultEnvelope> RestartRecordingSessionAsync(int slot, string leaseId,
            string expectedRuntimeMode, long? expectedMovieTick = null, CancellationToken cancellationToken = default)
            => ExecuteAsync(CreateCommand(AutomationCommandIds.RestartRecordingSession, AutomationScope.ControlPlayback,
                new Dictionary<string, string> { ["slot"] = slot.ToString(CultureInfo.InvariantCulture) },
                leaseId, expectedRuntimeMode, expectedMovieTick), cancellationToken);

        public Task<AutomationResultEnvelope> CancelRecordingRestartAsync(string operationId, string leaseId,
            CancellationToken cancellationToken = default)
            => ExecuteAsync(CreateCommand(AutomationCommandIds.CancelRecordingRestart, AutomationScope.ControlPlayback,
                new Dictionary<string, string> { ["operationId"] = operationId }, leaseId), cancellationToken);

        public Task<AutomationResultEnvelope> ResumeAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteSimpleControlAsync(
                AutomationCommandIds.Resume,
                AutomationScope.ControlPlayback,
                leaseId,
                expectedRuntimeMode,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> RunUntilAsync(
            long targetMovieTick,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.RunUntil,
                    AutomationScope.ControlRunUntil,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["targetMovieTick"] =
                            targetMovieTick.ToString(
                                CultureInfo.InvariantCulture)
                    },
                    leaseId,
                    expectedRuntimeMode,
                    expectedMovieTick),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> StartRecordingAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteSimpleControlAsync(
                AutomationCommandIds.StartRecording,
                AutomationScope.ControlRecording,
                leaseId,
                expectedRuntimeMode,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> StopRecordingAsync(
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteSimpleControlAsync(
                AutomationCommandIds.StopRecording,
                AutomationScope.ControlRecording,
                leaseId,
                expectedRuntimeMode,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> StartVideoExportAsync(
            string ffmpegPath,
            string outputPath,
            int maximumFrames,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            bool replayLoadedMovie = false,
            CancellationToken cancellationToken = default)
        {
            if (maximumFrames <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumFrames),
                    "Maximum frames must be positive.");
            }

            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.StartVideoExport,
                    AutomationScope.ControlPlayback,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ffmpegPath"] = ffmpegPath,
                        ["outputPath"] = outputPath,
                        ["maximumFrames"] = maximumFrames.ToString(
                            CultureInfo.InvariantCulture),
                        ["replayLoadedMovie"] = replayLoadedMovie
                            ? "true"
                            : "false"
                    },
                    leaseId,
                    expectedRuntimeMode,
                    expectedMovieTick),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> FinishVideoExportAsync(
            string operationId,
            string leaseId,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.FinishVideoExport,
                    AutomationScope.ControlPlayback,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["operationId"] = operationId
                    },
                    leaseId),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> CancelVideoExportAsync(
            string operationId,
            string leaseId,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.CancelVideoExport,
                    AutomationScope.ControlPlayback,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["operationId"] = operationId
                    },
                    leaseId),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> CreateReplaySaveAsync(
            string label,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteReplaySaveAsync(
                AutomationCommandIds.CreateReplaySave,
                "label",
                label,
                leaseId,
                expectedRuntimeMode,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> SetAutoSavePolicyAsync(
            bool enabled, long intervalMovieTicks, int retentionCount,
            string leaseId, string expectedRuntimeMode, long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(CreateCommand(AutomationCommandIds.SetAutoSavePolicy,
                AutomationScope.ControlReplaySave,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["enabled"] = enabled ? "true" : "false",
                    ["intervalMovieTicks"] = intervalMovieTicks.ToString(CultureInfo.InvariantCulture),
                    ["retentionCount"] = retentionCount.ToString(CultureInfo.InvariantCulture)
                }, leaseId, expectedRuntimeMode, expectedMovieTick), cancellationToken);
        }

        public Task<AutomationResultEnvelope> RestoreReplaySaveAsync(
            string replaySaveId,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteReplaySaveAsync(
                AutomationCommandIds.RestoreReplaySave,
                "replaySaveId",
                replaySaveId,
                leaseId,
                expectedRuntimeMode,
                expectedMovieTick,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> SeekMovieTickAsync(
            long targetMovieTick,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.SeekMovieTick,
                    AutomationScope.ControlReplaySave,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["targetMovieTick"] =
                            targetMovieTick.ToString(
                                CultureInfo.InvariantCulture),
                        ["expectedSceneEpoch"] =
                            expectedSceneEpoch.ToString(
                                CultureInfo.InvariantCulture)
                    },
                    leaseId,
                    "Paused",
                    expectedMovieTick),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> ApplyBranchAndSeekAsync(
            string branchMovieId,
            long targetMovieTick,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.ApplyBranchAndSeek,
                    AutomationScope.MovieApplyBranch,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["branchMovieId"] = branchMovieId,
                        ["targetMovieTick"] =
                            targetMovieTick.ToString(
                                CultureInfo.InvariantCulture),
                        ["expectedSceneEpoch"] =
                            expectedSceneEpoch.ToString(
                                CultureInfo.InvariantCulture)
                    },
                    leaseId,
                    "Paused",
                    expectedMovieTick),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> ApplyMovieBranchAsync(
            string branchMovieId,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.ApplyMovieBranch,
                    AutomationScope.MovieApplyBranch,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["branchMovieId"] = branchMovieId
                    },
                    leaseId,
                    expectedRuntimeMode,
                    expectedMovieTick),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> ValidateMoviePatchAsync(
            byte[] canonicalCandidateMovieUtf8,
            CancellationToken cancellationToken = default)
        {
            if (canonicalCandidateMovieUtf8 == null)
            {
                throw new ArgumentNullException(
                    nameof(canonicalCandidateMovieUtf8));
            }

            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.ValidateMoviePatch,
                    AutomationScope.MovieValidate,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["candidateMovieBase64"] =
                            Convert.ToBase64String(
                                canonicalCandidateMovieUtf8)
                    }),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> ProposeMoviePatchAsync(
            string baseMovieId,
            byte[] canonicalCandidateMovieUtf8,
            string expectedMilestone,
            string reason,
            CancellationToken cancellationToken = default)
        {
            if (canonicalCandidateMovieUtf8 == null)
            {
                throw new ArgumentNullException(
                    nameof(canonicalCandidateMovieUtf8));
            }

            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.ProposeMoviePatch,
                    AutomationScope.MoviePropose,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["baseMovieId"] = baseMovieId,
                        ["candidateMovieBase64"] =
                            Convert.ToBase64String(
                                canonicalCandidateMovieUtf8),
                        ["expectedMilestone"] = expectedMilestone,
                        ["reason"] = reason
                    }),
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> ReplaceInputRangeAsync(
            string baseMovieId,
            long startTick,
            long deleteCount,
            byte[] canonicalReplacementMovieUtf8,
            CancellationToken cancellationToken = default)
        {
            return ExecuteTimelineEditAsync(
                AutomationCommandIds.ReplaceInputRange,
                baseMovieId,
                startTick,
                deleteCount,
                canonicalReplacementMovieUtf8,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> InsertInputRangeAsync(
            string baseMovieId,
            long startTick,
            byte[] canonicalReplacementMovieUtf8,
            CancellationToken cancellationToken = default)
        {
            return ExecuteTimelineEditAsync(
                AutomationCommandIds.InsertInputRange,
                baseMovieId,
                startTick,
                null,
                canonicalReplacementMovieUtf8,
                cancellationToken);
        }

        public Task<AutomationResultEnvelope> DeleteInputRangeAsync(
            string baseMovieId,
            long startTick,
            long count,
            CancellationToken cancellationToken = default)
        {
            var arguments = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["baseMovieId"] = baseMovieId,
                ["startTick"] = startTick.ToString(
                    CultureInfo.InvariantCulture),
                ["count"] = count.ToString(
                    CultureInfo.InvariantCulture)
            };
            return ExecuteAsync(
                CreateCommand(
                    AutomationCommandIds.DeleteInputRange,
                    AutomationScope.MovieEdit,
                    arguments),
                cancellationToken);
        }

        public async IAsyncEnumerable<AutomationResultEnvelope>
            SubscribeTimelineAsync(
                long fromMovieTick = 0,
                int count = 100,
                TimeSpan? pollingInterval = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            var afterSequence = 0L;
            var interval = pollingInterval
                           ?? TimeSpan.FromMilliseconds(250);
            while (!cancellationToken.IsCancellationRequested)
            {
                var command = CreateCommand(
                    AutomationCommandIds.GetTimeline,
                    AutomationScope.ObserveTimeline,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["count"] = count.ToString(
                            CultureInfo.InvariantCulture),
                        ["fromMovieTick"] = fromMovieTick.ToString(
                            CultureInfo.InvariantCulture),
                        ["afterSequence"] =
                            afterSequence.ToString(
                                CultureInfo.InvariantCulture)
                    });
                var result = await ExecuteAsync(
                    command,
                    cancellationToken);
                yield return result;
                if (result.Success
                    && result.Data.TryGetValue(
                        "nextAfterSequence",
                        out var nextText)
                    && long.TryParse(
                        nextText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var nextSequence)
                    && nextSequence >= afterSequence)
                {
                    afterSequence = nextSequence;
                }
                else if (result.Success
                    && TryFindLastTimelineSequence(
                        result,
                        out var last))
                {
                    afterSequence = last;
                }

                await Task.Delay(interval, cancellationToken);
            }
        }

        private static void EnsureSuccessful(AutomationResultEnvelope result)
        {
            if (!result.Success)
                throw new InvalidOperationException(
                    result.ResultCode + ": " + result.Detail);
        }

        private static string ReadField(
            AutomationResultEnvelope result,
            string name)
        {
            if (!result.Data.TryGetValue(name, out var value))
                throw new InvalidDataException(
                    "Automation response omitted " + name + ".");
            return value;
        }

        private static JsonObject ParseObject(
            AutomationResultEnvelope result,
            string name)
        {
            var json = ReadField(result, name);
            try
            {
                return JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidDataException(
                        name + " is not a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    name + " is invalid JSON.", exception);
            }
        }

        private static long ReadInt64(
            JsonObject value,
            string name,
            long fallback)
        {
            if (!value.TryGetPropertyValue(name, out var node)
                || node == null)
                return fallback;
            try
            {
                return node.GetValue<long>();
            }
            catch (Exception exception)
                when (exception is FormatException
                      || exception is InvalidOperationException
                      || exception is OverflowException)
            {
                throw new InvalidDataException(
                    name + " is not an Int64.", exception);
            }
        }

        private static long ReadInt64(
            AutomationResultEnvelope result,
            string name,
            long fallback)
        {
            return result.Data.TryGetValue(name, out var text)
                   && long.TryParse(
                       text,
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out var value)
                ? value
                : fallback;
        }

        public ValueTask DisposeAsync()
        {
            pipe?.Dispose();
            pipe = null;
            requestLock.Dispose();
            return ValueTask.CompletedTask;
        }

        private Task<AutomationResultEnvelope> ExecuteSimpleControlAsync(
            string commandId,
            string scope,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                CreateCommand(
                    commandId,
                    scope,
                    leaseId: leaseId,
                    expectedRuntimeMode: expectedRuntimeMode,
                    expectedMovieTick: expectedMovieTick),
                cancellationToken);
        }

        private Task<AutomationResultEnvelope> ExecuteInputMovieAsync(
            string commandId,
            byte[] canonicalMovieUtf8,
            int expectedSceneEpoch,
            string leaseId,
            long expectedMovieTick,
            CancellationToken cancellationToken)
        {
            if (canonicalMovieUtf8 == null)
            {
                throw new ArgumentNullException(
                    nameof(canonicalMovieUtf8));
            }

            var arguments = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["candidateMovieBase64"] =
                    Convert.ToBase64String(canonicalMovieUtf8),
                ["expectedSceneEpoch"] =
                    expectedSceneEpoch.ToString(
                        CultureInfo.InvariantCulture)
            };
            return ExecuteAsync(
                CreateCommand(
                    commandId,
                    AutomationScope.ControlInput,
                    arguments,
                    leaseId,
                    "Paused",
                    expectedMovieTick),
                cancellationToken);
        }

        private Task<AutomationResultEnvelope>
            ExecuteInputBatchTransactionAsync(
                string commandId,
                string? transactionId,
                int? chunkIndex,
                byte[]? canonicalInputMovieChunkUtf8,
                int expectedSceneEpoch,
                string leaseId,
                long expectedMovieTick,
                CancellationToken cancellationToken)
        {
            var arguments = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["expectedSceneEpoch"] = expectedSceneEpoch.ToString(
                    CultureInfo.InvariantCulture)
            };
            if (transactionId != null)
            {
                arguments["transactionId"] = transactionId;
            }

            if (chunkIndex.HasValue)
            {
                arguments["chunkIndex"] = chunkIndex.Value.ToString(
                    CultureInfo.InvariantCulture);
            }

            if (canonicalInputMovieChunkUtf8 != null)
            {
                arguments["candidateMovieBase64"] =
                    Convert.ToBase64String(
                        canonicalInputMovieChunkUtf8);
            }

            return ExecuteAsync(
                CreateCommand(
                    commandId,
                    AutomationScope.ControlInput,
                    arguments,
                    leaseId,
                    "Paused",
                    expectedMovieTick),
                cancellationToken);
        }

        private Task<AutomationResultEnvelope> ExecuteTimelineEditAsync(
            string commandId,
            string baseMovieId,
            long startTick,
            long? deleteCount,
            byte[] canonicalReplacementMovieUtf8,
            CancellationToken cancellationToken)
        {
            if (canonicalReplacementMovieUtf8 == null)
            {
                throw new ArgumentNullException(
                    nameof(canonicalReplacementMovieUtf8));
            }

            var arguments = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["baseMovieId"] = baseMovieId,
                ["startTick"] = startTick.ToString(
                    CultureInfo.InvariantCulture),
                ["replacementMovieBase64"] =
                    Convert.ToBase64String(
                        canonicalReplacementMovieUtf8)
            };
            if (deleteCount.HasValue)
            {
                arguments["deleteCount"] =
                    deleteCount.Value.ToString(
                        CultureInfo.InvariantCulture);
            }

            return ExecuteAsync(
                CreateCommand(
                    commandId,
                    AutomationScope.MovieEdit,
                    arguments),
                cancellationToken);
        }

        private Task<AutomationResultEnvelope> ExecuteReplaySaveAsync(
            string commandId,
            string argumentName,
            string argumentValue,
            string leaseId,
            string expectedRuntimeMode,
            long expectedMovieTick,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                CreateCommand(
                    commandId,
                    AutomationScope.ControlReplaySave,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        [argumentName] = argumentValue
                    },
                    leaseId,
                    expectedRuntimeMode,
                    expectedMovieTick),
                cancellationToken);
        }

        private static void ValidateAck(
            IpcEnvelope ack,
            AutomationBootstrapDescriptor bootstrap,
            string expectedClientId)
        {
            var decoded = IpcPayloadCodec.TryDeserialize(
                ack.PayloadUtf8);
            var expected = new[]
            {
                "clientId",
                "manifestSha256",
                "mode",
                "product",
                "schemaVersion",
                "serverNonce",
                "sessionId"
            };
            if (ack.ProtocolVersion != AutomationProtocol.Version
                || ack.Sequence != 0
                || ack.SessionId != bootstrap.SessionId
                || ack.MessageType != AutomationProtocol.HelloAck
                || !decoded.Success
                || decoded.Fields == null
                || decoded.Fields.Count != expected.Length
                || expected.Any(
                    key => !decoded.Fields.ContainsKey(key))
                || decoded.Fields["clientId"] != expectedClientId
                || decoded.Fields["manifestSha256"]
                   != bootstrap.ManifestSha256
                || decoded.Fields["mode"] != bootstrap.Mode.ToString()
                || decoded.Fields["product"]
                   != AutomationProtocol.Product
                || decoded.Fields["schemaVersion"] != "1"
                || decoded.Fields["sessionId"]
                   != bootstrap.SessionId)
            {
                throw new InvalidDataException(
                    "Automation hello acknowledgement is invalid.");
            }

            try
            {
                if (Convert.FromBase64String(
                        decoded.Fields["serverNonce"]).Length != 32)
                {
                    throw new InvalidDataException(
                        "Automation server nonce is invalid.");
                }
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    "Automation server nonce is invalid.",
                    exception);
            }
        }

        private static bool TryFindLastTimelineSequence(
            AutomationResultEnvelope result,
            out long sequence)
        {
            sequence = -1;
            if (!result.Data.TryGetValue(
                    "entries",
                    out var entries))
            {
                return false;
            }

            foreach (var line in entries.Split('\n'))
            {
                var parts = line.Split('|');
                if (parts.Length == 4
                    && long.TryParse(
                        parts[0],
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                    && parsed > sequence)
                {
                    sequence = parsed;
                }
            }

            return sequence >= 0;
        }

        private void RequireConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException(
                    "Automation client is not connected.");
            }
        }
    }
}
