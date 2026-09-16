using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class InputGridRow : INotifyPropertyChanged
    {
        public InputGridRow(long tick, FrameRunCommand input, long currentTick)
        { Tick = tick; Input = input; Current = tick == currentTick ? "▶" : ""; }
        public long Tick { get; }
        public FrameRunCommand Input { get; }
        public string Current { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void UpdateCurrent(long tick)
        {
            var value = Tick == tick ? "▶" : "";
            if (value == Current) return;
            Current = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }
        public bool Left => Has(TasAction.Left);
        public bool Right => Has(TasAction.Right);
        public bool Up => Has(TasAction.Up);
        public bool Down => Has(TasAction.Down);
        public bool Jump => Has(TasAction.Jump);
        public bool Attack => Has(TasAction.Attack);
        public bool Dash => Has(TasAction.Dash);
        public bool Cast => Has(TasAction.Cast);
        public bool QuickCast => Has(TasAction.QuickCast);
        public bool SuperDash => Has(TasAction.SuperDash);
        public bool DreamNail => Has(TasAction.DreamNail);
        public string Axes => Input.HasAnalogAxes ? $"{Input.AxisX}, {Input.AxisY}" : "—";
        private bool Has(TasAction action) => (Input.HeldActions & action) != 0;
    }

    // Only the visible page is expanded. Editing remains run-based and preserves analog data.
    public static class InputGridEditor
    {
        public const int PageSize = 500;
        public static long Count(MovieDocument movie) => movie.Commands.OfType<FrameRunCommand>().Sum(r => r.FrameCount);

        public static IReadOnlyList<InputGridRow> Page(MovieDocument movie, long start, long currentTick)
        {
            if (start < 0 || start > Count(movie)) throw new ArgumentOutOfRangeException(nameof(start));
            var count = Math.Min(PageSize, Count(movie) - start);
            var slice = MovieInputSlice.Extract(movie, start, count);
            var rows = new List<InputGridRow>((int)count);
            foreach (var run in slice.Commands.OfType<FrameRunCommand>())
                for (long i = 0; i < run.FrameCount; i++) rows.Add(new InputGridRow(start + rows.Count, run, currentTick));
            return rows;
        }

        public static MovieDocument Toggle(MovieDocument movie, long start, long count, TasAction action)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var bits = (int)action;
            if (bits == 0 || (bits & (bits - 1)) != 0 || (action & TasAction.AllGameplay) != action)
                throw new ArgumentOutOfRangeException(nameof(action));
            var runs = MovieInputSlice.Extract(movie, start, count).Commands.OfType<FrameRunCommand>().ToArray();
            // Like painting a binary input column: all set -> clear; mixed/clear -> set.
            var clear = runs.All(r => (r.HeldActions & action) != 0);
            var replacement = runs.Select(r => new FrameRunCommand(r.FrameCount,
                clear ? r.HeldActions & ~action : r.HeldActions | action, r.AxisX, r.AxisY, r.HasAnalogAxes, r.Span));
            return MovieTimelineEditor.Replace(movie, start, count, replacement).Movie;
        }

        public static MovieDocument Paste(MovieDocument movie, long start, long deleteCount, MovieDocument clipboard)
        {
            if (clipboard.Commands.Any(c => c is not FrameRunCommand))
                throw new ArgumentException("粘贴内容只能包含输入帧；不会复制检查点或生命周期事件。", nameof(clipboard));
            var runs = clipboard.Commands.OfType<FrameRunCommand>().ToArray();
            if (runs.Length == 0) throw new ArgumentException("剪贴板没有输入帧。", nameof(clipboard));
            return MovieTimelineEditor.Replace(movie, start, deleteCount, runs).Movie;
        }

        public static MovieDocument SetAxes(MovieDocument movie, long start, long count, bool enabled, int x, int y)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (x < -10000 || x > 10000 || y < -10000 || y > 10000)
                throw new ArgumentOutOfRangeException(nameof(x), "模拟轴范围为 -10000…10000。");
            var runs = MovieInputSlice.Extract(movie, start, count).Commands.OfType<FrameRunCommand>().ToArray();
            var directions = TasAction.Left | TasAction.Right | TasAction.Up | TasAction.Down;
            if (enabled && runs.Any(r => (r.HeldActions & directions) != 0))
                throw new ArgumentException("模拟轴与方向键不能同时使用；请先清除选区方向键。");
            return MovieTimelineEditor.Replace(movie, start, count, runs.Select(r => new FrameRunCommand(
                r.FrameCount, r.HeldActions, enabled ? x : 0, enabled ? y : 0, enabled, r.Span))).Movie;
        }
    }
}
