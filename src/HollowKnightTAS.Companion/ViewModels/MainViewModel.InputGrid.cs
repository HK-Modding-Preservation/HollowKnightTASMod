using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private readonly Stack<string> gridUndo = new Stack<string>();
        private readonly Stack<string> gridRedo = new Stack<string>();
        private string gridSource = string.Empty;
        private string gridStart = "0", gridCount = "1", gridStatus = "打开脚本后刷新表格。";
        private long gridPageStart;
        private TasAction gridAction = TasAction.Attack;
        private bool gridApplying;
        public ObservableCollection<InputGridRow> InputRows { get; } = new ObservableCollection<InputGridRow>();
        public IReadOnlyList<TasAction> GridActions { get; } = Enum.GetValues<TasAction>()
            .Where(a => a != TasAction.None && a != TasAction.AllGameplay).ToArray();
        public string GridStart { get => gridStart; set => Set(ref gridStart, value); }
        public string GridCount { get => gridCount; set => Set(ref gridCount, value); }
        public string GridStatus { get => gridStatus; private set => Set(ref gridStatus, value); }
        public TasAction GridAction { get => gridAction; set => Set(ref gridAction, value); }
        public ICommand RefreshGridCommand { get; private set; } = null!;
        public ICommand PreviousGridPageCommand { get; private set; } = null!;
        public ICommand NextGridPageCommand { get; private set; } = null!;
        public ICommand GoToGridFrameCommand { get; private set; } = null!;
        public ICommand FollowGridFrameCommand { get; private set; } = null!;
        public ICommand ToggleGridCommand { get; private set; } = null!;
        public ICommand InsertGridCommand { get; private set; } = null!;
        public ICommand DeleteGridCommand { get; private set; } = null!;
        public ICommand CopyGridCommand { get; private set; } = null!;
        public ICommand PasteGridCommand { get; private set; } = null!;
        public ICommand UndoGridCommand { get; private set; } = null!;
        public ICommand RedoGridCommand { get; private set; } = null!;
        public ICommand ApplyGridCommand { get; private set; } = null!;
        public ICommand ApplyGridAndSeekCommand { get; private set; } = null!;

        private void InitializeInputGrid()
        {
            ICommand Local(Action action) => new RelayCommand(() =>
            { try { if (gridApplying) throw new InvalidOperationException("正在提交表格分支，请等待完成。"); action(); } catch (Exception ex) { GridStatus = ex.Message; } });
            RefreshGridCommand = Local(() => RefreshInputGrid());
            PreviousGridPageCommand = Local(() => { gridPageStart = Math.Max(0, gridPageStart - InputGridEditor.PageSize); RefreshInputGrid(); });
            NextGridPageCommand = Local(() => { var total = InputGridEditor.Count(GridMovie()); if (gridPageStart + InputGridEditor.PageSize < total) gridPageStart += InputGridEditor.PageSize; RefreshInputGrid(); });
            GoToGridFrameCommand = Local(() => { gridPageStart = GridIndex(); RefreshInputGrid(); });
            FollowGridFrameCommand = Local(() => { gridPageStart = Math.Max(0, currentMovieTick); RefreshInputGrid(); });
            ToggleGridCommand = Local(() => EditGrid(InputGridEditor.Toggle(GridMovie(), GridIndex(), GridLength(), GridAction)));
            InsertGridCommand = Local(() => EditGrid(MovieTimelineEditor.Insert(GridMovie(), GridIndex(),
                new[] { new FrameRunCommand(GridLength(), TasAction.None, 0, 0, false, new MovieSourceSpan("<input-grid>", 1, 1, 1)) }).Movie));
            DeleteGridCommand = Local(() => EditGrid(MovieTimelineEditor.Delete(GridMovie(), GridIndex(), GridLength()).Movie));
            CopyGridCommand = Local(() => { Clipboard.SetText(new MovieCanonicalWriter().WriteToString(
                MovieInputSlice.Extract(GridMovie(), GridIndex(), GridLength()))); GridStatus = "已复制输入帧，不含检查点。"; });
            PasteGridCommand = Local(() =>
            {
                var pasted = movieEditor.Validate(Clipboard.GetText());
                if (!pasted.Success || pasted.Document == null) throw new InvalidOperationException("剪贴板不是有效的 HK-TAS 输入片段。请先在表格中复制帧。");
                EditGrid(InputGridEditor.Paste(GridMovie(), GridIndex(), GridLength(), pasted.Document));
            });
            UndoGridCommand = Local(() => RestoreGridHistory(gridUndo, gridRedo));
            RedoGridCommand = Local(() => RestoreGridHistory(gridRedo, gridUndo));
            ApplyGridCommand = Command(() => ApplyGridAsync(false));
            ApplyGridAndSeekCommand = Command(() => ApplyGridAsync(true));
        }

        private MovieDocument GridMovie()
        {
            if (TimelineIncludeLifecycle) throw new InvalidOperationException("完整生命周期分支请使用 Advanced Authoring；表格编辑当前文本序列。");
            var result = movieEditor.Validate(MovieText);
            if (!result.Success || result.Document == null) throw new InvalidOperationException("当前文本无效，请先在 Movie Text 修正并校验。");
            if (gridSource != MovieText) { gridUndo.Clear(); gridRedo.Clear(); gridSource = MovieText; }
            return result.Document;
        }

        private long GridIndex() => ParseCount(GridStart, 0, MovieProtocolV1.DefaultMaxExpandedTicks, "frame");
        private long GridLength() => ParseCount(GridCount, 1, MovieProtocolV1.DefaultMaxExpandedTicks, "selected frame count");

        public bool TryEditGridAxes(string start, string count, bool enabled, int x, int y)
        {
            try
            {
                if (gridApplying) throw new InvalidOperationException("正在提交表格分支，请等待完成。");
                EditGrid(InputGridEditor.SetAxes(GridMovie(),
                    ParseCount(start, 0, MovieProtocolV1.DefaultMaxExpandedTicks, "frame"),
                    ParseCount(count, 1, MovieProtocolV1.DefaultMaxExpandedTicks, "selected frame count"), enabled, x, y));
                return true;
            }
            catch (Exception e) { GridStatus = e.Message; return false; }
        }

        private void RefreshInputGrid(string? message = null)
        {
            var movie = GridMovie();
            var total = InputGridEditor.Count(movie);
            gridPageStart = Math.Min(gridPageStart, Math.Max(0, total - 1));
            InputRows.Clear();
            foreach (var row in InputGridEditor.Page(movie, gridPageStart, currentMovieTick)) InputRows.Add(row);
            GridStatus = $"帧 {gridPageStart}–{Math.Max(gridPageStart, gridPageStart + InputRows.Count - 1)} / 共 {total} 帧。" +
                (message ?? "点击按键切换；Shift 选择范围。编辑仅改变草稿，应用后再重放。");
        }

        private void EditGrid(MovieDocument candidate)
        {
            var text = new MovieCanonicalWriter().WriteToString(candidate);
            var validated = movieEditor.Validate(text);
            if (!validated.Success) throw new InvalidOperationException("编辑结果超出序列限制，草稿未改变。");
            PushGridHistory(gridUndo, MovieText);
            gridRedo.Clear();
            MovieText = gridSource = validated.CanonicalText;
            RefreshInputGrid("草稿已修改，游戏状态未改变。应用分支后可重放到选中帧。");
        }

        private static void PushGridHistory(Stack<string> history, string text)
        {
            if (history.Count >= 100)
            {
                var newest = history.Take(99).Reverse().ToArray(); history.Clear();
                foreach (var entry in newest) history.Push(entry);
            }
            history.Push(text);
        }

        private void RestoreGridHistory(Stack<string> source, Stack<string> target)
        {
            GridMovie();
            if (source.Count == 0) { GridStatus = "没有可撤销/重做的表格编辑。"; return; }
            PushGridHistory(target, MovieText);
            MovieText = gridSource = source.Pop();
            RefreshInputGrid("草稿历史已恢复，游戏状态未改变。");
        }

        private async Task ApplyGridAsync(bool seek)
        {
            if (gridApplying) throw new InvalidOperationException("表格分支提交正在进行。");
            GridMovie();
            var target = GridIndex();
            if (target > InputGridEditor.Count(GridMovie())) throw new InvalidOperationException("目标帧超出序列。");
            var submittedText = MovieText;
            gridApplying = true;
            try
            {
                await UploadMovieAsync(); // Propose + Apply through the same broker as Movie Text / AI.
                if (MovieText != submittedText) throw new InvalidOperationException("提交期间文本已改变；刚才提交的是旧草稿，新草稿尚未应用，未发起重放。");
                if (seek)
                {
                    SeekTargetTick = target.ToString(CultureInfo.InvariantCulture);
                    await ApplyBranchAndSeekAsync();
                }
                GridStatus = seek ? "分支已提交并请求重放；以恢复状态完成为准。" : "分支已应用；游戏位置未回退。修改过去输入后请重放到目标帧。";
            }
            catch (Exception ex) { GridStatus = ex.Message; throw; }
            finally { gridApplying = false; }
        }
    }
}
