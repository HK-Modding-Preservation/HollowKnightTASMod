using System;
using System.Linq;
using HollowKnightTAS.AgentBridge;
using HollowKnightTAS.Automation.Client;
using HollowKnightTAS.Cli.Commands.Automation;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class AuthoringSurfaceParityTests
    {
        [TestMethod]
        public void RequiredLibTasOperationsHaveUiSdkCliAndMcpAdapters()
        {
            var rows = new[]
            {
                Row(AutomationCommandIds.QuitGame, "QuitGameCommand", "QuitGameAsync", "hktas_quit_game"),
                Row(AutomationCommandIds.LoadGameSlot, "LoadGameSlotCommand", "LoadGameSlotAsync", "hktas_load_game_slot"),
                Row(AutomationCommandIds.RestartRecordingSession, "RestartRecordingSessionCommand", "RestartRecordingSessionAsync", "hktas_restart_recording_session"),
                Row(AutomationCommandIds.CancelRecordingRestart, "CancelRecordingRestartCommand", "CancelRecordingRestartAsync", "hktas_cancel_recording_restart"),
                Row(
                    AutomationCommandIds.SetAutoSavePolicy,
                    "SetAutoSavePolicyCommand",
                    "SetAutoSavePolicyAsync",
                    "hktas_set_auto_save_policy"),
                Row(
                    AutomationCommandIds.GetState,
                    "SnapshotCommand",
                    "GetStateAsync",
                    "hktas_get_state"),
                Row(
                    AutomationCommandIds.GetCombatState,
                    "RefreshCombatStateCommand",
                    "GetCombatStateAsync",
                    "hktas_get_combat_state"),
                Row(
                    AutomationCommandIds.Pause,
                    "PauseCommand",
                    "PauseAsync",
                    "hktas_pause"),
                Row(
                    AutomationCommandIds.Resume,
                    "ResumeCommand",
                    "ResumeAsync",
                    "hktas_resume"),
                Row(
                    AutomationCommandIds.StepWithInput,
                    "StepWithInputCommand",
                    "StepWithInputAsync",
                    "hktas_step_with_input"),
                Row(
                    AutomationCommandIds.Step,
                    "StepCommand",
                    "StepAsync",
                    "hktas_step"),
                Row(
                    AutomationCommandIds.QueueInputBatch,
                    "RunInputBatchCommand",
                    "QueueInputBatchAsync",
                    "hktas_queue_input_batch"),
                Row(
                    AutomationCommandIds.RunUntil,
                    "RunUntilCommand",
                    "RunUntilAsync",
                    "hktas_run_until"),
                Row(
                    AutomationCommandIds.StartRecording,
                    "StartRecordingCommand",
                    "StartRecordingAsync",
                    "hktas_start_recording"),
                Row(
                    AutomationCommandIds.StopRecording,
                    "StopRecordingCommand",
                    "StopRecordingAsync",
                    "hktas_stop_recording"),
                Row(
                    AutomationCommandIds.GetReplaySaves,
                    "RefreshReplaySavesCommand",
                    "GetReplaySavesAsync",
                    "hktas_get_replay_saves"),
                Row(
                    AutomationCommandIds.CreateReplaySave,
                    "CreateReplaySaveCommand",
                    "CreateReplaySaveAsync",
                    "hktas_create_replay_save"),
                Row(
                    AutomationCommandIds.RestoreReplaySave,
                    "RestoreReplaySaveCommand",
                    "RestoreReplaySaveAsync",
                    "hktas_restore_replay_save"),
                Row(
                    AutomationCommandIds.GetMovie,
                    "RefreshRuntimeMovieCommand",
                    "GetMovieAsync",
                    "hktas_get_movie"),
                Row(
                    AutomationCommandIds.ApplyMovieBranch,
                    "ApplyMovieBranchCommand",
                    "ApplyMovieBranchAsync",
                    "hktas_apply_movie_branch"),
                Row(
                    AutomationCommandIds.SeekMovieTick,
                    "SeekMovieTickCommand",
                    "SeekMovieTickAsync",
                    "hktas_seek_movie_tick"),
                Row(
                    AutomationCommandIds.ApplyBranchAndSeek,
                    "ApplyBranchAndSeekCommand",
                    "ApplyBranchAndSeekAsync",
                    "hktas_apply_branch_and_seek"),
                Row(
                    AutomationCommandIds.ReplaceInputRange,
                    "ReplaceInputRangeCommand",
                    "ReplaceInputRangeAsync",
                    "hktas_replace_input_range"),
                Row(
                    AutomationCommandIds.InsertInputRange,
                    "InsertInputRangeCommand",
                    "InsertInputRangeAsync",
                    "hktas_insert_input_range"),
                Row(
                    AutomationCommandIds.DeleteInputRange,
                    "DeleteInputRangeCommand",
                    "DeleteInputRangeAsync",
                    "hktas_delete_input_range"),
                Row(AutomationCommandIds.ReloadGameSlot, "ReloadGameSlotCommand", "ReloadGameSlotAsync", "hktas_reload_game_slot")
            };

            var uiProperties = typeof(MainViewModel)
                .GetProperties()
                .Select(value => value.Name)
                .ToHashSet(StringComparer.Ordinal);
            var sdkMethods = typeof(IAutomationClient)
                .GetMethods()
                .Select(value => value.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                Assert.IsTrue(
                    AutomationCommandIds.IsKnown(row.CommandId),
                    "Core: " + row.CommandId);
                Assert.IsTrue(
                    uiProperties.Contains(row.UiProperty),
                    "UI: " + row.UiProperty);
                Assert.IsTrue(
                    sdkMethods.Contains(row.SdkMethod),
                    "SDK: " + row.SdkMethod);
                Assert.IsTrue(
                    McpCatalog.TryGetTool(row.McpTool, out _),
                    "MCP: " + row.McpTool);
            }

            StringAssert.Contains(
                AutomationCommands.CallUsage,
                "automation call <command-id> <scope>");
        }

        private static SurfaceRow Row(
            string commandId,
            string uiProperty,
            string sdkMethod,
            string mcpTool)
        {
            return new SurfaceRow(
                commandId,
                uiProperty,
                sdkMethod,
                mcpTool);
        }

        private sealed class SurfaceRow
        {
            public SurfaceRow(
                string commandId,
                string uiProperty,
                string sdkMethod,
                string mcpTool)
            {
                CommandId = commandId;
                UiProperty = uiProperty;
                SdkMethod = sdkMethod;
                McpTool = mcpTool;
            }

            public string CommandId { get; }
            public string UiProperty { get; }
            public string SdkMethod { get; }
            public string McpTool { get; }
        }
    }
}
