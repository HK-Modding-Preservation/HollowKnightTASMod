using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.AgentBridge.Tests
{
    [TestClass]
    public sealed class McpProtocolTests
    {
        [TestMethod]
        public void VideoExportSchemasRequireOperationIdentityAndTypedReplayOption()
        {
            foreach (var name in new[] { "hktas_finish_video_export", "hktas_cancel_video_export" })
            {
                Assert.IsTrue(McpCatalog.TryGetTool(name, out var tool));
                using var request = JsonDocument.Parse("{\"operationId\":\"video-test\"}");
                Assert.IsTrue(McpStdioServer.ValidateArguments(tool, request.RootElement, out var error), error);
                using var missing = JsonDocument.Parse("{}");
                Assert.IsFalse(McpStdioServer.ValidateArguments(tool, missing.RootElement, out _));
                Assert.IsTrue(tool.RequiresApprovedControl);
            }
            Assert.IsTrue(McpCatalog.TryGetTool("hktas_start_video_export", out var start));
            using var valid = JsonDocument.Parse("{\"ffmpegPath\":\"ffmpeg.exe\",\"outputPath\":\"movie.mp4\",\"maximumFrames\":1000,\"replayLoadedMovie\":true,\"expectedRuntimeMode\":\"Paused\"}");
            Assert.IsTrue(McpStdioServer.ValidateArguments(start, valid.RootElement, out var validation), validation);
            using var invalid = JsonDocument.Parse(valid.RootElement.GetRawText().Replace("true", "\"true\""));
            Assert.IsFalse(McpStdioServer.ValidateArguments(start, invalid.RootElement, out _));
        }

        [TestMethod]
        public void VideoExportEndFrameIsOptionalBoundedAndForwardedWithoutChangingOldRequests()
        {
            Assert.IsTrue(McpCatalog.TryGetTool("hktas_start_video_export", out var tool));
            var property = tool.InputSchema["properties"]!["endMovieFrame"]!;
            Assert.AreEqual("integer", property["type"]!.GetValue<string>());
            Assert.AreEqual(1L, property["minimum"]!.GetValue<long>());
            Assert.AreEqual(MovieProtocolV2.MaximumExpandedFrames, property["maximum"]!.GetValue<long>());
            Assert.IsFalse(tool.InputSchema["required"]!.AsArray().Any(value => value!.GetValue<string>() == "endMovieFrame"));
            const string required = "\"ffmpegPath\":\"ffmpeg.exe\",\"outputPath\":\"movie.mp4\",\"maximumFrames\":1000,\"expectedRuntimeMode\":\"Paused\"";
            using var oldRequest = JsonDocument.Parse("{" + required + "}");
            using var newRequest = JsonDocument.Parse("{" + required + ",\"endMovieFrame\":3700}");
            using var upperBound = JsonDocument.Parse("{" + required + ",\"endMovieFrame\":" + MovieProtocolV2.MaximumExpandedFrames + "}");
            foreach (var request in new[] { oldRequest, newRequest, upperBound })
                Assert.IsTrue(McpStdioServer.ValidateArguments(tool, request.RootElement, out var error), error);
            foreach (var value in new[] { "0", "-1", "10000001", "1.5", "\"3700\"", "\"invalid\"", "null" })
                AssertRejects(tool, "{" + required + ",\"endMovieFrame\":" + value + "}");

            var select = typeof(McpStdioServer).GetMethod("SelectOptional",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var names = new[] { "ffmpegPath", "outputPath", "maximumFrames", "replayLoadedMovie", "endMovieFrame" };
            var oldFields = (System.Collections.Generic.IReadOnlyDictionary<string, string>)select.Invoke(null,
                new object[] { oldRequest.RootElement, names })!;
            var newFields = (System.Collections.Generic.IReadOnlyDictionary<string, string>)select.Invoke(null,
                new object[] { newRequest.RootElement, names })!;
            Assert.IsFalse(oldFields.ContainsKey("endMovieFrame"));
            Assert.IsFalse(oldFields.ContainsKey("replayLoadedMovie"));
            Assert.AreEqual("3700", newFields["endMovieFrame"]);
            Assert.AreEqual(oldFields["ffmpegPath"], newFields["ffmpegPath"]);
            Assert.AreEqual(oldFields["outputPath"], newFields["outputPath"]);
            Assert.AreEqual(oldFields["maximumFrames"], newFields["maximumFrames"]);
        }

        [TestMethod]
        public void ColdRestoreCancellationRequiresOperationIdentityNotLiveRuntimeMode()
        {
            Assert.IsTrue(McpCatalog.TryGetTool("hktas_cancel_replay_save_restore", out var tool));
            using var request = JsonDocument.Parse("{\"operationId\":\"cold-restore-example\"}");
            Assert.IsTrue(McpStdioServer.ValidateArguments(tool, request.RootElement, out var error), error);
            using var missing = JsonDocument.Parse("{}");
            Assert.IsFalse(McpStdioServer.ValidateArguments(tool, missing.RootElement, out _));
            using var wrongType = JsonDocument.Parse("{\"operationId\":42}");
            Assert.IsFalse(McpStdioServer.ValidateArguments(tool, wrongType.RootElement, out _));
            var select = typeof(McpStdioServer).GetMethod("Select",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var fields = (System.Collections.Generic.IReadOnlyDictionary<string, string>)select.Invoke(null,
                new object[] { request.RootElement, new[] { "operationId" } })!;
            Assert.AreEqual("cold-restore-example", fields["operationId"]);
            Assert.IsTrue(tool.RequiresApprovedControl);
        }

        [TestMethod]
        public void BranchCatalogArgumentsAreTypedAndForwarded()
        {
            var schema = McpCatalog.Tools.Single(tool => tool.Name == "hktas_get_movie").InputSchema;
            Assert.AreEqual("boolean", schema["properties"]!["listBranches"]!["type"]!.GetValue<string>());
            Assert.AreEqual("integer", schema["properties"]!["branchOffset"]!["type"]!.GetValue<string>());
            var flatten = typeof(McpStdioServer).GetMethod("FlatArguments",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            using var request = JsonDocument.Parse("{\"listBranches\":true,\"branchOffset\":50}");
            var fields = (System.Collections.Generic.IReadOnlyDictionary<string, string>)flatten.Invoke(null,
                new object[] { request.RootElement })!;
            Assert.AreEqual("true", fields["listBranches"]);
            Assert.AreEqual("50", fields["branchOffset"]);
        }

        [TestMethod]
        public void MovieChunkArgumentsAreTypedAndForwardedTogether()
        {
            var schema = McpCatalog.Tools.Single(tool => tool.Name == "hktas_get_movie").InputSchema;
            Assert.AreEqual("string", schema["properties"]!["exportId"]!["type"]!.GetValue<string>());
            Assert.AreEqual("integer", schema["properties"]!["exportChunk"]!["type"]!.GetValue<string>());
            var flatten = typeof(McpStdioServer).GetMethod("FlatArguments",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            using var request = JsonDocument.Parse("{\"includeLifecycle\":true,\"exportId\":\"abc\",\"exportChunk\":2}");
            var fields = (System.Collections.Generic.IReadOnlyDictionary<string, string>)flatten.Invoke(null,
                new object[] { request.RootElement })!;
            Assert.AreEqual("true", fields["includeLifecycle"]);
            Assert.AreEqual("abc", fields["exportId"]);
            Assert.AreEqual("2", fields["exportChunk"]);
        }

        [TestMethod]
        public void LifecycleOptionIsOptionalAndForwardedWithoutDroppingRequiredFields()
        {
            foreach (var name in new[] { "hktas_get_movie", "hktas_replace_input_range", "hktas_insert_input_range", "hktas_delete_input_range" })
            {
                var schema = McpCatalog.Tools.Single(tool => tool.Name == name).InputSchema;
                Assert.AreEqual("boolean", schema["properties"]!["includeLifecycle"]!["type"]!.GetValue<string>());
                Assert.IsFalse(schema["required"]?.AsArray().Any(x => x!.GetValue<string>() == "includeLifecycle") == true);
            }
            var select = typeof(McpStdioServer).GetMethod("SelectOptionalLifecycle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            using var plain = JsonDocument.Parse("{\"baseMovieId\":\"abc\"}");
            using var complete = JsonDocument.Parse("{\"baseMovieId\":\"abc\",\"includeLifecycle\":true}");
            var names = new[] { "baseMovieId", "includeLifecycle" };
            var without = (System.Collections.Generic.IReadOnlyDictionary<string, string>)select.Invoke(null,
                new object[] { plain.RootElement, names })!;
            var with = (System.Collections.Generic.IReadOnlyDictionary<string, string>)select.Invoke(null,
                new object[] { complete.RootElement, names })!;
            Assert.AreEqual("abc", without["baseMovieId"]);
            Assert.IsFalse(without.ContainsKey("includeLifecycle"));
            Assert.AreEqual("true", with["includeLifecycle"]);
        }

        [TestMethod]
        public void EveryToolHasClosedObjectSchemaAndUniqueName()
        {
            var names = McpCatalog.Tools
                .Select(tool => tool.Name)
                .ToArray();
            Assert.AreEqual(
                names.Length,
                names.Distinct(StringComparer.Ordinal).Count());
            foreach (var tool in McpCatalog.Tools)
            {
                Assert.AreEqual(
                    "object",
                    tool.InputSchema["type"]!.GetValue<string>());
                Assert.IsFalse(
                    tool.InputSchema["additionalProperties"]!
                        .GetValue<bool>());
                Assert.IsTrue(
                    tool.Name.All(
                        character =>
                            char.IsAsciiLetterOrDigit(character)
                            || character == '_'
                            || character == '-'
                            || character == '.'));
            }
        }

        [TestMethod]
        public void ResourceCatalogContainsOnlyHktasUris()
        {
            Assert.IsTrue(McpCatalog.Resources.Count >= 8);
            foreach (var resource in McpCatalog.Resources)
            {
                Assert.IsTrue(
                    resource["uri"]!.GetValue<string>()
                        .StartsWith(
                            "hktas://session/current/",
                            StringComparison.Ordinal));
            }
        }

        [TestMethod]
        public void ProposalIsLocalSideEffectWithoutControlElevation()
        {
            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_propose_movie_patch",
                    out var proposal));
            Assert.IsFalse(proposal.ReadOnly);
            Assert.IsFalse(proposal.RequiresApprovedControl);
            Assert.IsFalse(
                proposal.ToJson()["annotations"]![
                    "readOnlyHint"]!.GetValue<bool>());
        }

        [TestMethod]
        public void ReplaySaveLifecycleToolsAreClosedAndTyped()
        {
            foreach (var name in new[]
                     {
                         "hktas_approve_replay_save_overwrite",
                         "hktas_cancel_replay_save_restore",
                         "hktas_resume_replay_save_restore"
                     })
            {
                Assert.IsTrue(
                    McpCatalog.TryGetTool(name, out var tool),
                    name);
                Assert.IsTrue(tool.RequiresApprovedControl);
                Assert.IsFalse(tool.ReadOnly);
            }

            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_approve_replay_save_overwrite",
                    out var approve));
            var approvedSchema =
                approve.InputSchema["properties"]!["approved"]!;
            Assert.AreEqual(
                "boolean",
                approvedSchema["type"]!.GetValue<string>());
        }

        [TestMethod]
        public void FrameAuthoringAndHistoryEditingArePublishedToMcp()
        {
            foreach (var name in new[]
                     {
                         "hktas_get_combat_state",
                         "hktas_get_replay_saves",
                         "hktas_step_with_input",
                         "hktas_queue_input_batch",
                         "hktas_begin_input_batch",
                         "hktas_append_input_batch",
                         "hktas_commit_input_batch",
                         "hktas_cancel_input_batch",
                         "hktas_run_until",
                         "hktas_start_recording",
                         "hktas_stop_recording",
                         "hktas_apply_movie_branch",
                         "hktas_seek_movie_tick",
                         "hktas_apply_branch_and_seek",
                         "hktas_replace_input_range",
                         "hktas_insert_input_range",
                         "hktas_delete_input_range"
                     })
            {
                Assert.IsTrue(
                    McpCatalog.TryGetTool(name, out _),
                    name);
            }

            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_step_with_input",
                    out var single));
            Assert.IsTrue(single.RequiresApprovedControl);
            var required = single.InputSchema["required"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray();
            CollectionAssert.Contains(required, "expectedMovieTick");
            CollectionAssert.Contains(required, "expectedSceneEpoch");

            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_replace_input_range",
                    out var replace));
            Assert.IsFalse(replace.RequiresApprovedControl);
            Assert.IsFalse(replace.ReadOnly);

            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_append_input_batch",
                    out var append));
            var appendRequired = append.InputSchema["required"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray();
            CollectionAssert.Contains(appendRequired, "transactionId");
            CollectionAssert.Contains(appendRequired, "chunkIndex");
            CollectionAssert.Contains(
                appendRequired,
                "candidateMovieBase64");
            CollectionAssert.Contains(
                appendRequired,
                "expectedMovieTick");
            CollectionAssert.Contains(
                appendRequired,
                "expectedSceneEpoch");

            foreach (var name in new[]
                     {
                         "hktas_seek_movie_tick",
                         "hktas_apply_branch_and_seek"
                     })
            {
                Assert.IsTrue(McpCatalog.TryGetTool(name, out var seek));
                var seekRequired = seek.InputSchema["required"]!
                    .AsArray()
                    .Select(value => value!.GetValue<string>())
                    .ToArray();
                CollectionAssert.Contains(
                    seekRequired,
                    "targetMovieTick");
                CollectionAssert.Contains(
                    seekRequired,
                    "expectedMovieTick");
                CollectionAssert.Contains(
                    seekRequired,
                    "expectedSceneEpoch");
            }
        }

        [TestMethod]
        public void DeepObservationToolsPublishClosedBoundedSchemas()
        {
            Assert.IsTrue(McpCatalog.TryGetTool(
                "hktas_get_world_snapshot", out var world));
            Assert.IsTrue(world.ReadOnly);
            Assert.IsFalse(world.RequiresApprovedControl);
            using var validWorld = JsonDocument.Parse(
                "{\"snapshotId\":\"snap-1\",\"view\":\"colliders\","
                + "\"includeInactive\":true,\"offset\":0,\"limit\":128}");
            Assert.IsTrue(McpStdioServer.ValidateArguments(
                world, validWorld.RootElement, out var worldError), worldError);
            using var invalidWorld = JsonDocument.Parse("{\"limit\":129}");
            Assert.IsFalse(McpStdioServer.ValidateArguments(
                world, invalidWorld.RootElement, out _));

            Assert.IsTrue(McpCatalog.TryGetTool(
                "hktas_get_object_details", out var details));
            var required = details.InputSchema["required"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray();
            CollectionAssert.AreEqual(new[] { "objectId" }, required);
            using var validDetails = JsonDocument.Parse(
                "{\"objectId\":\"enemy-1\",\"expectedNativeFrame\":4,"
                + "\"cursor\":0,\"maxCharacters\":200000}");
            Assert.IsTrue(McpStdioServer.ValidateArguments(
                details, validDetails.RootElement, out var detailsError), detailsError);
            using var invalidDetails = JsonDocument.Parse(
                "{\"objectId\":\"enemy-1\",\"maxCharacters\":1023}");
            Assert.IsFalse(McpStdioServer.ValidateArguments(
                details, invalidDetails.RootElement, out _));
        }

        [TestMethod]
        public async Task BoundedLineReaderDrainsOversizedInputAndRecovers()
        {
            var input = new string('x', 33)
                        + "\r\n"
                        + "{\"jsonrpc\":\"2.0\"}\r\n";
            var reader = new BoundedTextLineReader(
                new StringReader(input));

            var oversized = await reader.ReadAsync(
                32,
                default);
            Assert.IsTrue(oversized.IsOversized);
            Assert.IsFalse(oversized.IsEndOfStream);
            Assert.IsNull(oversized.Text);

            var recovered = await reader.ReadAsync(
                32,
                default);
            Assert.IsFalse(recovered.IsOversized);
            Assert.IsFalse(recovered.IsEndOfStream);
            Assert.AreEqual(
                "{\"jsonrpc\":\"2.0\"}",
                recovered.Text);

            var end = await reader.ReadAsync(32, default);
            Assert.IsTrue(end.IsEndOfStream);
        }

        [TestMethod]
        public void ToolValidatorEnforcesPublishedBounds()
        {
            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_acquire_control",
                    out var acquire));
            AssertRejects(acquire, "{\"scopes\":[]}");
            AssertRejects(
                acquire,
                "{\"scopes\":[\"control.playback\","
                + "\"control.playback\"]}");
            AssertRejects(
                acquire,
                "{\"scopes\":["
                + string.Join(
                    ",",
                    Enumerable.Range(0, 17)
                        .Select(
                            index =>
                                JsonSerializer.Serialize(
                                    "scope-" + index)))
                + "]}");
            AssertRejects(
                acquire,
                "{\"scopes\":["
                + JsonSerializer.Serialize(
                    new string('s', 129))
                + "]}");
            AssertRejects(
                acquire,
                "{\"scopes\":[\"control.playback\"],"
                + "\"ttlSeconds\":301}");
            AssertRejects(
                acquire,
                "{\"scopes\":[\"control.playback\"],"
                + "\"shell\":\"powershell\"}");

            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_propose_movie_patch",
                    out var propose));
            AssertRejects(
                propose,
                "{\"baseMovieId\":\"none\","
                + "\"candidateMovieBase64\":\"AA==\","
                + "\"reason\":"
                + JsonSerializer.Serialize(new string('r', 513))
                + ",\"expectedMilestone\":\"checkpoint\"}");

            Assert.IsTrue(
                McpCatalog.TryGetTool(
                    "hktas_set_hero_pose",
                    out var pose));
            AssertRejects(
                pose,
                "{\"expectedSnapshotSha256\":\""
                + new string('a', 64)
                + "\",\"expectedMovieTick\":0,"
                + "\"positionX\":10001,\"positionY\":0,"
                + "\"velocityX\":0,\"velocityY\":0}");

            using var valid = JsonDocument.Parse(
                "{\"scopes\":[\"control.playback\"],"
                + "\"ttlSeconds\":30}");
            Assert.IsTrue(
                McpStdioServer.ValidateArguments(
                    acquire,
                    valid.RootElement,
                    out var error),
                error);
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task StdioNeverWritesNonJsonToStdout()
        {
            var assembly = typeof(McpStdioServer)
                .Assembly.Location;
            var start = new ProcessStartInfo(
                "dotnet",
                "\"" + assembly + "\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(start)
                                ?? throw new InvalidOperationException(
                                    "AgentBridge process did not start.");
            await process.StandardInput.WriteLineAsync("not-json");
            process.StandardInput.Close();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsFalse(string.IsNullOrWhiteSpace(stderr));
            var lines = stdout.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, lines.Length);
            using var parsed = JsonDocument.Parse(lines[0]);
            Assert.AreEqual(
                "2.0",
                parsed.RootElement
                    .GetProperty("jsonrpc")
                    .GetString());
            Assert.AreEqual(
                -32700,
                parsed.RootElement
                    .GetProperty("error")
                    .GetProperty("code")
                    .GetInt32());
        }

        private static void AssertRejects(
            McpToolDefinition tool,
            string json)
        {
            using var document = JsonDocument.Parse(json);
            Assert.IsFalse(
                McpStdioServer.ValidateArguments(
                    tool,
                    document.RootElement,
                    out var error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }
    }
}
