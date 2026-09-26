using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.AgentBridge
{
    internal sealed class McpToolDefinition
    {
        public McpToolDefinition(
            string name,
            string description,
            JsonObject inputSchema,
            bool readOnly,
            bool requiresApprovedControl)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            ReadOnly = readOnly;
            RequiresApprovedControl = requiresApprovedControl;
        }

        public string Name { get; }
        public string Description { get; }
        public JsonObject InputSchema { get; }
        public bool ReadOnly { get; }
        public bool RequiresApprovedControl { get; }

        public JsonObject ToJson()
        {
            return new JsonObject
            {
                ["name"] = Name,
                ["description"] = Description,
                ["inputSchema"] = InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["readOnlyHint"] = ReadOnly,
                    ["destructiveHint"] = false,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                }
            };
        }
    }

    internal static class McpCatalog
    {
        public static readonly IReadOnlyList<McpToolDefinition> Tools =
            new[]
            {
                Tool(
                    "hktas_get_state",
                    "Read a fresh non-visual semantic state snapshot; statusOnly=true reads menu-safe operational status without a gameplay snapshot.",
                    Schema(Props(("statusOnly", Boolean()))),
                    true),
                Tool(
                    "hktas_get_combat_state",
                    "Read the latest normalized non-visual Hero/Boss combat state.",
                    Schema(),
                    true),
                Tool(
                    "hktas_get_world_snapshot",
                    "Read a bounded immutable full-run v2 world snapshot page; large objects are represented by detail stubs.",
                    Schema(
                        Props(
                            ("snapshotId", String(96)),
                            ("view", String(16)),
                            ("includeInactive", Boolean()),
                            ("offset", Integer(0)),
                            ("limit", Integer(1, 128)))),
                    true),
                Tool(
                    "hktas_get_object_details",
                    "Read immutable full-run v2 details for one object, with bounded text pagination.",
                    Schema(
                        Props(
                            ("objectId", String(128)),
                            ("expectedNativeFrame", Integer(0)),
                            ("detailsId", String(96)),
                            ("cursor", Integer(0)),
                            ("maxCharacters", Integer(1024, 200000))),
                        new[] { "objectId" }),
                    true),
                Tool(
                    "hktas_get_timeline",
                    "Read a bounded non-visual Runtime event timeline.",
                    Schema(
                        Props(
                            ("fromMovieTick", Integer(-1)),
                            ("afterSequence", Integer(0)),
                            ("count", Integer(1, 200))),
                        Array.Empty<string>()),
                    true),
                Tool(
                    "hktas_get_desync",
                    "Read the latest structured desync evidence.",
                    Schema(),
                    true),
                Tool(
                    "hktas_get_replay_saves",
                    "List structured replay-save checkpoints and restore metadata.",
                    Schema(),
                    true),
                Tool(
                    "hktas_get_movie",
                    "Read a movie or freeze a complete lifecycle export; read its immutable chunks using exportId and exportChunk.",
                    Schema(Props(("includeLifecycle", Boolean()), ("exportId", String(64)), ("exportChunk", Integer(0)),
                        ("listBranches", Boolean()), ("branchOffset", Integer(0)))),
                    true),
                Tool(
                    "hktas_propose_movie_patch",
                    "Validate and store a candidate movie as an isolated branch.",
                    Schema(
                        Props(
                            ("baseMovieId", String(64)),
                            ("candidateMovieBase64", String(900000)),
                            ("reason", String(512)),
                            ("expectedMilestone", String(256))),
                        new[]
                        {
                            "baseMovieId",
                            "candidateMovieBase64",
                            "reason",
                            "expectedMilestone"
                        }),
                    false,
                    false),
                Tool(
                    "hktas_validate_movie_patch",
                    "Validate a candidate movie without storing or running it.",
                    Schema(
                        Props(
                            ("candidateMovieBase64", String(900000))),
                        new[] { "candidateMovieBase64" }),
                    true),
                Tool(
                    "hktas_acquire_control",
                    "Acquire one short exclusive control lease for explicit scopes.",
                    Schema(
                        Props(
                            ("scopes", StringArray()),
                            ("ttlSeconds", Integer(1, 300))),
                        new[] { "scopes" }),
                    false),
                Tool(
                    "hktas_release_control",
                    "Release the bridge-owned control lease.",
                    Schema(),
                    false),
                ControlTool(
                    "hktas_pause",
                    "Pause at the registered T08 safe boundary."),
                ControlTool(
                    "hktas_resume",
                    "Resume from a T08 pause."),
                ControlTool("hktas_quit_game",
                    "Exit at an idle paused boundary after pending saves finish. Save the desired TAS point first."),
                Tool("hktas_load_game_slot",
                    "Load an existing game slot through the native menu path in a fresh running title-menu session. Wait for recording origin readiness before input; accepted is not completed.",
                    Schema(Props(("slot", Integer(1, 4)),
                        ("expectedRuntimeMode", String(32))),
                        new[] { "slot", "expectedRuntimeMode" }), false),
                Tool("hktas_restart_recording_session",
                    "Exit the current game normally and load an existing slot in one fresh recording process. Save desired TAS points first. Poll status recordingRestart fields using the returned operationId.",
                    Schema(Props(("slot", Integer(1, 4)), ("expectedRuntimeMode", String(32)),
                        ("expectedMovieTick", Integer(0))), new[] { "slot", "expectedRuntimeMode" }), false),
                Tool("hktas_reload_game_slot",
                    "Reload an existing slot in the same process from a paused completed frame. Poll nativeReloadPhase/nativeReloadFailure; acceptance is not completion. Gameplay input is unavailable during native loading.",
                    Schema(Props(("slot", Integer(1, 4)), ("expectedRuntimeMode", String(32)),
                        ("expectedMovieTick", Integer(0))), new[] { "slot", "expectedRuntimeMode", "expectedMovieTick" }), false),
                Tool("hktas_cancel_recording_restart",
                    "Cancel a pending recording restart by operationId; a ready target cannot be cancelled.",
                    Schema(Props(("operationId", String(96))), new[] { "operationId" }), false),
                Tool(
                    "hktas_step",
                    "Advance a paused simulation by movie ticks.",
                    Schema(
                        Props(
                            ("count", Integer(1, 10000)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[] { "count", "expectedRuntimeMode" }),
                    false),
                Tool(
                    "hktas_step_with_input",
                    "Set exactly one input tick and advance atomically.",
                    Schema(
                        Props(
                            ("candidateMovieBase64", String(900000)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "candidateMovieBase64",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_queue_input_batch",
                    "Execute one validated input movie of up to 10000000 ticks.",
                    Schema(
                        Props(
                            ("candidateMovieBase64", String(900000)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "candidateMovieBase64",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_begin_input_batch",
                    "Begin a chunked input transaction without changing Runtime state.",
                    Schema(
                        Props(
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_append_input_batch",
                    "Append one canonical frames-only movie chunk; commit is still side-effect free until explicitly requested.",
                    Schema(
                        Props(
                            ("transactionId", String(128)),
                            ("chunkIndex", Integer(0)),
                            ("candidateMovieBase64", String(6000000)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "transactionId",
                            "chunkIndex",
                            "candidateMovieBase64",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_commit_input_batch",
                    "Canonicalize and atomically schedule a completed chunked input transaction.",
                    Schema(
                        Props(
                            ("transactionId", String(128)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "transactionId",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_cancel_input_batch",
                    "Cancel a staged chunked input transaction without changing Runtime state.",
                    Schema(
                        Props(
                            ("transactionId", String(128)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "transactionId",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_run_until",
                    "Run until a target movie tick, then pause.",
                    Schema(
                        Props(
                            ("targetMovieTick", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "targetMovieTick",
                            "expectedRuntimeMode"
                        }),
                    false),
                Tool(
                    "hktas_start_recording",
                    "Start typed input recording.",
                    ControlSchema(),
                    false),
                Tool(
                    "hktas_stop_recording",
                    "Stop recording and return the canonical movie.",
                    ControlSchema(),
                    false),
                Tool(
                    "hktas_start_video_export",
                    "Start exporting gameplay frames and audio to a video file. Full-run v2 can stop capture at the optional inclusive endMovieFrame boundary.",
                    Schema(
                        Props(
                            ("ffmpegPath", String(1024)),
                            ("outputPath", String(1024)),
                            ("maximumFrames", Integer(1)),
                            ("replayLoadedMovie", Boolean()),
                            ("endMovieFrame", Integer(1, MovieProtocolV2.MaximumExpandedFrames)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "ffmpegPath",
                            "outputPath",
                            "maximumFrames",
                            "expectedRuntimeMode"
                        }),
                    false),
                Tool(
                    "hktas_finish_video_export",
                    "Finish an active video export identified by operationId.",
                    Schema(
                        Props(("operationId", String(96))),
                        new[] { "operationId" }),
                    false),
                Tool(
                    "hktas_cancel_video_export",
                    "Cancel an active video export identified by operationId.",
                    Schema(
                        Props(("operationId", String(96))),
                        new[] { "operationId" }),
                    false),
                Tool(
                    "hktas_start_replay",
                    "Start replay of the loaded canonical movie.",
                    ControlSchema(),
                    false),
                Tool(
                    "hktas_stop_replay",
                    "Stop active replay.",
                    ControlSchema(),
                    false),
                Tool(
                    "hktas_create_replay_save",
                    "Create a T09 replay save without touching ordinary slots.",
                    Schema(
                        Props(
                            ("label", String(128)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[] { "label", "expectedRuntimeMode" }),
                    false),
                Tool(
                    "hktas_set_auto_save_policy",
                    "Configure auto saves by advanced movie ticks; preferences persist on normal game exit. Retention affects automatic saves only, never manual saves.",
                    Schema(Props(("enabled", Boolean()),
                            ("intervalMovieTicks", Integer(1, 1000000000)),
                            ("retentionCount", Integer(1, 1000)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[] { "enabled", "intervalMovieTicks", "retentionCount", "expectedRuntimeMode" }),
                    false),
                Tool(
                    "hktas_restore_replay_save",
                    "Restore a named T09 replay save through the safe coordinator.",
                    Schema(
                        Props(
                            ("replaySaveId", String(128)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "replaySaveId",
                            "expectedRuntimeMode"
                        }),
                    false),
                Tool(
                    "hktas_seek_movie_tick",
                    "Restore the nearest exact-prefix replay checkpoint and deterministically replay its short tail to a target tick.",
                    Schema(
                        Props(
                            ("targetMovieTick", Integer(0)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "targetMovieTick",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_approve_replay_save_overwrite",
                    "Explicitly approve or deny dedicated TAS-slot overwrite for an active restore.",
                    Schema(
                        Props(
                            ("approved", Boolean()),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "approved",
                            "expectedRuntimeMode"
                        }),
                    false),
                Tool(
                    "hktas_cancel_replay_save_restore",
                    "Cancel the cold restore identified by operationId before its verified target. The source process may already have exited.",
                    Schema(Props(("operationId", String(96))), new[] { "operationId" }), false),
                ControlTool(
                    "hktas_resume_replay_save_restore",
                    "Resume only from a replay-save restore's verified paused target."),
                Tool(
                    "hktas_apply_movie_branch",
                    "Explicitly load a previously proposed movie branch.",
                    Schema(
                        Props(
                            ("branchMovieId", String(64)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "branchMovieId",
                            "expectedRuntimeMode"
                        }),
                    false),
                Tool(
                    "hktas_apply_branch_and_seek",
                    "Atomically load a content-addressed movie branch, restore its nearest compatible checkpoint, and replay to the target tick.",
                    Schema(
                        Props(
                            ("branchMovieId", String(64)),
                            ("targetMovieTick", Integer(0)),
                            ("expectedSceneEpoch", Integer(0)),
                            ("expectedRuntimeMode", String(32)),
                            ("expectedMovieTick", Integer(0))),
                        new[]
                        {
                            "branchMovieId",
                            "targetMovieTick",
                            "expectedSceneEpoch",
                            "expectedRuntimeMode",
                            "expectedMovieTick"
                        }),
                    false),
                Tool(
                    "hktas_replace_input_range",
                    "Replace an input-tick range and store the result as an isolated branch.",
                    Schema(
                        Props(
                            ("includeLifecycle", Boolean()),
                            ("baseMovieId", String(64)),
                            ("startTick", Integer(0)),
                            ("deleteCount", Integer(0)),
                            ("replacementMovieBase64", String(900000))),
                        new[]
                        {
                            "baseMovieId",
                            "startTick",
                            "deleteCount",
                            "replacementMovieBase64"
                        }),
                    false,
                    false),
                Tool(
                    "hktas_insert_input_range",
                    "Insert input ticks and store the result as an isolated branch.",
                    Schema(
                        Props(
                            ("includeLifecycle", Boolean()),
                            ("baseMovieId", String(64)),
                            ("startTick", Integer(0)),
                            ("replacementMovieBase64", String(900000))),
                        new[]
                        {
                            "baseMovieId",
                            "startTick",
                            "replacementMovieBase64"
                        }),
                    false,
                    false),
                Tool(
                    "hktas_delete_input_range",
                    "Delete an input-tick range and store the result as an isolated branch.",
                    Schema(
                        Props(
                            ("includeLifecycle", Boolean()),
                            ("baseMovieId", String(64)),
                            ("startTick", Integer(0)),
                            ("count", Integer(1))),
                        new[] { "baseMovieId", "startTick", "count" }),
                    false,
                    false)
            };

        public static readonly IReadOnlyList<JsonObject> Resources =
            new[]
            {
                Resource(
                    "hktas://session/current/status",
                    "Runtime status"),
                Resource(
                    "hktas://session/current/manifest",
                    "Environment manifest binding"),
                Resource(
                    "hktas://session/current/capabilities",
                    "Automation capability catalog"),
                Resource(
                    "hktas://session/current/state/summary",
                    "Non-visual semantic state summary"),
                Resource(
                    "hktas://session/current/state/combat",
                    "Normalized non-visual combat state"),
                Resource(
                    "hktas://session/current/timeline",
                    "Bounded event timeline"),
                Resource(
                    "hktas://session/current/desync/latest",
                    "Latest desync evidence"),
                Resource(
                    "hktas://session/current/replay-saves",
                    "Replay-save catalog"),
                Resource(
                    "hktas://session/current/movie",
                    "Current canonical TAS movie"),
                Resource(
                    "hktas://session/current/restore-strategy",
                    "Restore strategy and acceleration status")
            };

        public static bool TryGetTool(
            string name,
            out McpToolDefinition tool)
        {
            tool = Tools.FirstOrDefault(
                value => value.Name == name)!;
            return tool != null;
        }

        private static McpToolDefinition ControlTool(
            string name,
            string description)
        {
            return Tool(
                name,
                description,
                ControlSchema(),
                false);
        }

        private static JsonObject ControlSchema()
        {
            return Schema(
                Props(
                    ("expectedRuntimeMode", String(32)),
                    ("expectedMovieTick", Integer(0))),
                new[] { "expectedRuntimeMode" });
        }

        private static McpToolDefinition Tool(
            string name,
            string description,
            JsonObject schema,
            bool readOnly,
            bool? requiresApprovedControl = null)
        {
            return new McpToolDefinition(
                name,
                description,
                schema,
                readOnly,
                requiresApprovedControl ?? !readOnly);
        }

        private static JsonObject Resource(
            string uri,
            string name)
        {
            return new JsonObject
            {
                ["uri"] = uri,
                ["name"] = name,
                ["description"] =
                    "HollowKnightTAS non-visual automation resource.",
                ["mimeType"] = "application/json"
            };
        }

        private static JsonObject Schema(
            JsonObject? properties = null,
            IEnumerable<string>? required = null)
        {
            var result = new JsonObject
            {
                ["type"] = "object",
                ["properties"] =
                    properties ?? new JsonObject(),
                ["additionalProperties"] = false
            };
            var requiredArray = new JsonArray();
            foreach (var value in required ?? Array.Empty<string>())
            {
                requiredArray.Add(value);
            }

            if (requiredArray.Count > 0)
            {
                result["required"] = requiredArray;
            }

            return result;
        }

        private static JsonObject Props(
            params (string Name, JsonObject Schema)[] items)
        {
            var result = new JsonObject();
            foreach (var item in items)
            {
                result[item.Name] = item.Schema;
            }

            return result;
        }

        private static JsonObject String(int maximumLength)
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = maximumLength
            };
        }

        private static JsonObject StringArray()
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["maxLength"] = 128
                },
                ["minItems"] = 1,
                ["maxItems"] = 16,
                ["uniqueItems"] = true
            };
        }

        private static JsonObject Integer(
            long minimum,
            long? maximum = null)
        {
            var result = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = minimum
            };
            if (maximum.HasValue)
            {
                result["maximum"] = maximum.Value;
            }

            return result;
        }

        private static JsonObject Number(
            double minimum,
            double maximum)
        {
            return new JsonObject
            {
                ["type"] = "number",
                ["minimum"] = minimum,
                ["maximum"] = maximum
            };
        }

        private static JsonObject Boolean()
        {
            return new JsonObject
            {
                ["type"] = "boolean"
            };
        }
    }
}
