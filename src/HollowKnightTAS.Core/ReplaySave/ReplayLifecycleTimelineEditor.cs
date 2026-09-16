using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Core.ReplaySave
{
    // An editing plan is deliberately not a ReplayLifecycleLog: planned native
    // operations have no Completed outcome or measured engine-frame duration.
    public sealed class ReplayLifecyclePlannedOperation
    {
        internal ReplayLifecyclePlannedOperation(ReplayLifecycleRecord source,
            long afterMovieTick, string prefix, bool requiresReplay)
        {
            Source = source;
            AfterMovieTick = afterMovieTick;
            InputPrefixSha256 = prefix;
            RequiresReplay = requiresReplay;
        }

        public ReplayLifecycleRecord Source { get; }
        public long AfterMovieTick { get; }
        public string InputPrefixSha256 { get; }
        public bool RequiresReplay { get; }
    }

    public sealed class ReplayLifecycleTimelineEditResult
    {
        internal ReplayLifecycleTimelineEditResult(ReplayLifecycleLog source,
            MovieTimelineEditResult inputEdit, List<ReplayLifecyclePlannedOperation> operations)
        {
            Source = source;
            InputEdit = inputEdit;
            Operations = new ReadOnlyCollection<ReplayLifecyclePlannedOperation>(operations);
        }

        public ReplayLifecycleLog Source { get; }
        public MovieTimelineEditResult InputEdit { get; }
        public IReadOnlyList<ReplayLifecyclePlannedOperation> Operations { get; }
    }

    public static class ReplayLifecycleTimelineEditor
    {
        public static ReplayLifecycleTimelineEditResult CaptureUnedited(MovieDocument movie, ReplayLifecycleLog lifecycle)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (!lifecycle.IsCompleted) throw new InvalidOperationException("Cannot capture an unfinished lifecycle recording.");
            lifecycle.VerifyInputPrefixes(movie);
            var ticks = movie.Commands.OfType<FrameRunCommand>().Aggregate(0L, (n, frame) => checked(n + frame.FrameCount));
            return new ReplayLifecycleTimelineEditResult(lifecycle,
                new MovieTimelineEditResult(movie, TimelineEditKind.Replace, 0, 0, 0, ticks, Array.Empty<string>()),
                lifecycle.Records.Select(record => new ReplayLifecyclePlannedOperation(record,
                    record.AfterMovieTick, record.InputPrefixSha256, false)).ToList());
        }

        public static ReplayLifecycleTimelineEditResult Edit(MovieDocument sourceMovie,
            ReplayLifecycleLog lifecycle, TimelineEditKind kind, long startTick,
            long deleteCount, IEnumerable<FrameRunCommand> replacement)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (!lifecycle.IsCompleted)
                throw new InvalidOperationException("Cannot edit an unfinished lifecycle recording.");
            lifecycle.VerifyInputPrefixes(sourceMovie);
            var operations = new List<ReplayLifecyclePlannedOperation>();
            foreach (var record in lifecycle.Records)
                operations.Add(new ReplayLifecyclePlannedOperation(record, record.AfterMovieTick,
                    record.InputPrefixSha256, false));
            return EditCore(sourceMovie, lifecycle, operations, kind, startTick, deleteCount, replacement);
        }

        public static ReplayLifecycleTimelineEditResult Edit(ReplayLifecycleTimelineEditResult previous,
            TimelineEditKind kind, long startTick, long deleteCount, IEnumerable<FrameRunCommand> replacement)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            return EditCore(previous.InputEdit.Movie, previous.Source, previous.Operations,
                kind, startTick, deleteCount, replacement);
        }

        private static ReplayLifecycleTimelineEditResult EditCore(MovieDocument sourceMovie,
            ReplayLifecycleLog lifecycle, IEnumerable<ReplayLifecyclePlannedOperation> previous,
            TimelineEditKind kind, long startTick, long deleteCount, IEnumerable<FrameRunCommand> replacement)
        {
            var edit = MovieTimelineEditor.Edit(sourceMovie, kind, startTick, deleteCount, replacement);
            var end = checked(startTick + deleteCount);
            var operations = new List<ReplayLifecyclePlannedOperation>();
            var prefixes = new Dictionary<long, string>();
            bool earlierOperationChanged = false;
            foreach (var record in previous)
            {
                // Use the same before-input anchor convention as movie events.
                // Deleting inputs must not implicitly delete a load/menu action:
                // covered actions collapse to the end of the replacement, in
                // their original order, before the retained suffix.
                var anchor = checked(record.AfterMovieTick + 1);
                var mapped = anchor < startTick ? anchor
                    : anchor < end ? checked(startTick + edit.InsertedTicks)
                    : checked(anchor + edit.TickDelta);
                if (!prefixes.TryGetValue(mapped, out var prefix))
                {
                    prefix = MoviePrefixIdentity.ComputeSha256(edit.Movie, mapped);
                    prefixes.Add(mapped, prefix);
                }
                earlierOperationChanged |= record.RequiresReplay || mapped != anchor || prefix != record.InputPrefixSha256;
                operations.Add(new ReplayLifecyclePlannedOperation(record.Source, mapped - 1,
                    prefix, earlierOperationChanged));
            }
            return new ReplayLifecycleTimelineEditResult(lifecycle, edit, operations);
        }
    }
}
