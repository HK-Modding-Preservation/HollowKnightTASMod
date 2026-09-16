using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Automation
{
    public static class AutomationScope
    {
        public const string ObserveStatus = "observe.status";
        public const string ObserveStateSummary =
            "observe.state.summary";
        public const string ObserveStateDeep = "observe.state.deep";
        public const string ObserveTimeline = "observe.timeline";
        public const string ObserveDesync = "observe.desync";
        public const string ObserveReplaySaves =
            "observe.replay-saves";
        public const string MovieRead = "movie.read";
        public const string MoviePropose = "movie.propose";
        public const string MovieValidate = "movie.validate";
        public const string MovieApplyBranch =
            "movie.apply-branch";
        public const string MovieEdit = "movie.edit";
        public const string ControlPlayback = "control.playback";
        public const string ControlStep = "control.step";
        public const string ControlInput = "control.input";
        public const string ControlRunUntil = "control.run-until";
        public const string ControlReplaySave =
            "control.replay-save";
        public const string ControlRecording =
            "control.recording";
        public const string DebugStatePose = "debug.state.pose";
        public const string DebugStateResources =
            "debug.state.resources";

        private static readonly HashSet<string> Known =
            new HashSet<string>(
                new[]
                {
                    ObserveStatus,
                    ObserveStateSummary,
                    ObserveStateDeep,
                    ObserveTimeline,
                    ObserveDesync,
                    ObserveReplaySaves,
                    MovieRead,
                    MoviePropose,
                    MovieValidate,
                    MovieApplyBranch,
                    MovieEdit,
                    ControlPlayback,
                    ControlStep,
                    ControlInput,
                    ControlRunUntil,
                    ControlReplaySave,
                    ControlRecording,
                    DebugStatePose,
                    DebugStateResources
                },
                StringComparer.Ordinal);

        public static IReadOnlyCollection<string> All => Known;

        public static bool IsKnown(string? value)
        {
            return value != null && Known.Contains(value);
        }

        public static bool IsReadOnly(string value)
        {
            return string.Equals(
                       value,
                       ObserveStatus,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       ObserveStateSummary,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       ObserveStateDeep,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       ObserveTimeline,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       ObserveDesync,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       ObserveReplaySaves,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       MovieRead,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       MoviePropose,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       MovieValidate,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       MovieEdit,
                       StringComparison.Ordinal);
        }
    }
}
