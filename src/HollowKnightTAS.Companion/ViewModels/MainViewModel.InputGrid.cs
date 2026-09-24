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
        private long gridTotalFrames;
        private long lastFollowedGridFrame = -1;
        private long currentFullRunMovieFrame = -1;
        private bool autoFollowGrid = true;
        private string? gridProgressRequestId;
        private DateTime gridProgressRequestedUtc;
        private TasAction gridAction = TasAction.Attack;
        private bool gridApplying;
        public event EventHandler? InputGridRefreshed;
        public event Action<long>? InputGridPositionChanged;
        public ObservableCollection<InputGridRow> InputRows { get; } = new ObservableCollection<InputGridRow>();
        public IReadOnlyList<TasAction> GridActions { get; } = Enum.GetValues<TasAction>()
            .Where(a => a != TasAction.None && a != TasAction.AllGameplay).ToArray();
        public string GridStart { get => gridStart; set => Set(ref gridStart, value); }
        public string GridCount { get => gridCount; set => Set(ref gridCount, value); }
        public string GridStatus { get => gridStatus; private set => Set(ref gridStatus, value); }
        public TasAction GridAction { get => gridAction; set => Set(ref gridAction, value); }
        public bool AutoFollowGrid
        {
            get => autoFollowGrid;
            set
            {
                if (autoFollowGrid == value) return;
                Set(ref autoFollowGrid, value);
                if (value) TrackGridFrame(CurrentGridFrame, true);
            }
        }
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
            {
                try
                {
                    if (gridApplying) throw new InvalidOperationException("正在提交表格分支，请等待完成。");
                    action();
                }
                catch (Exception ex) { GridStatus = ex.Message; }
            });
            RefreshGridCommand = Local(() => RefreshInputGrid());
            PreviousGridPageCommand = Local(() =>
            {
                gridPageStart = Math.Max(0, gridPageStart - InputGridEditor.PageSize);
                RefreshInputGrid();
            });
            NextGridPageCommand = Local(() =>
            {
                var source = GridAny();
                var total = source.V2Document != null ? InputGridEditor.Count(source.V2Document)
                    : InputGridEditor.Count(source.V1Document!);
                if (gridPageStart + InputGridEditor.PageSize < total)
                    gridPageStart += InputGridEditor.PageSize;
                RefreshInputGrid();
            });
            GoToGridFrameCommand = Local(() => { gridPageStart = GridIndex(); RefreshInputGrid(); });
            FollowGridFrameCommand = Local(() =>
            {
                gridPageStart = Math.Max(0, CurrentGridFrame);
                RefreshInputGrid();
                ScrollGridToFrame(CurrentGridFrame);
            });
            ToggleGridCommand = Local(() =>
            {
                if (GridAny().V2Document != null)
                    throw new InvalidOperationException(
                        "v2 有多通道动作与按下/抬起边缘；请在 Movie Text 编辑 samples。");
                EditGrid(InputGridEditor.Toggle(GridMovie(), GridIndex(), GridLength(), GridAction));
            });
            InsertGridCommand = Local(() =>
            {
                var source = GridAny();
                if (source.V2Document != null)
                    EditGrid(new MovieV2TimelineEditor().InsertFrames(source.V2Document,
                        GridIndex(), new[] { new NativeFrameRun(GridLength(),
                            Array.Empty<GameInputSample>(),
                            new MovieSourceSpan("<input-grid>", 1, 1, 1)) }));
                else EditGrid(MovieTimelineEditor.Insert(source.V1Document!, GridIndex(),
                    new[] { new FrameRunCommand(GridLength(), TasAction.None, 0, 0, false,
                        new MovieSourceSpan("<input-grid>", 1, 1, 1)) }).Movie);
            });
            DeleteGridCommand = Local(() =>
            {
                var source = GridAny();
                if (source.V2Document != null)
                    EditGrid(new MovieV2TimelineEditor().DeleteFrames(source.V2Document,
                        GridIndex(), GridLength()));
                else EditGrid(MovieTimelineEditor.Delete(source.V1Document!,
                    GridIndex(), GridLength()).Movie);
            });
            CopyGridCommand = Local(() =>
            {
                var source = GridAny();
                Clipboard.SetText(source.V2Document != null
                    ? new MovieV2Codec().WriteCanonical(SliceV2(source.V2Document,
                        GridIndex(), GridLength()))
                    : new MovieCanonicalWriter().WriteToString(MovieInputSlice.Extract(
                        source.V1Document!, GridIndex(), GridLength())));
                GridStatus = "已复制所选输入帧。";
            });
            PasteGridCommand = Local(() =>
            {
                var target = GridAny();
                var pasted = movieEditor.ValidateAny(Clipboard.GetText());
                if (!pasted.Success || pasted.Version != target.Version)
                    throw new InvalidOperationException("剪贴板不是同版本的有效 HK-TAS 输入片段。");
                if (target.V2Document != null)
                {
                    var editor = new MovieV2TimelineEditor();
                    var shortened = editor.DeleteFrames(target.V2Document,
                        GridIndex(), GridLength());
                    if (!shortened.Success) throw new InvalidOperationException(
                        shortened.Diagnostics[0].Message);
                    EditGrid(editor.InsertFrames(shortened.Movie, GridIndex(),
                        pasted.V2Document!.Runs));
                }
                else EditGrid(InputGridEditor.Paste(target.V1Document!,
                    GridIndex(), GridLength(), pasted.V1Document!));
            });
            UndoGridCommand = Local(() => RestoreGridHistory(gridUndo, gridRedo));
            RedoGridCommand = Local(() => RestoreGridHistory(gridRedo, gridUndo));
            ApplyGridCommand = Command(() => ApplyGridAsync(false));
            ApplyGridAndSeekCommand = Command(() => ApplyGridAsync(true));
        }

        private MovieAnyEditorResult GridAny()
        {
            if (TimelineIncludeLifecycle)
                throw new InvalidOperationException(
                    "完整生命周期分支请使用 Advanced Authoring；表格编辑当前文本序列。");
            var result = movieEditor.ValidateAny(MovieText);
            if (!result.Success)
                throw new InvalidOperationException(
                    "当前文本无效，请先在 Movie Text 修正并校验。");
            if (gridSource != MovieText)
            {
                gridUndo.Clear();
                gridRedo.Clear();
                gridSource = MovieText;
            }
            return result;
        }

        private MovieDocument GridMovie() => GridAny().V1Document
            ?? throw new InvalidOperationException("当前表格是 v2 原生帧。");
        private long GridIndex() => ParseCount(GridStart, 0,
            MovieProtocolV2.MaximumExpandedFrames, "frame");
        private long GridLength() => ParseCount(GridCount, 1,
            MovieProtocolV2.MaximumExpandedFrames, "selected frame count");

        private long CurrentGridFrame => startupBoot?.IsPending == true
            ? currentFullRunMovieFrame >= 0 ? currentFullRunMovieFrame : 0
            : currentMovieTick;

        public void ShowCurrentGridFrame() => TrackGridFrame(CurrentGridFrame, true);

        public async Task PollInputGridProgressAsync()
        {
            var session = SelectedSession?.Client;
            if (session?.IsConnected != true
                || (startupBoot?.IsPending != true && InputRows.Count == 0))
                return;
            var now = DateTime.UtcNow;
            var interval = startupBoot?.IsWaiting == true || currentControlMode == "Paused"
                ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(200);
            if (now - gridProgressRequestedUtc < interval) return;
            if (gridProgressRequestId != null
                && now - gridProgressRequestedUtc < TimeSpan.FromSeconds(2))
                return;
            var requestId = "studio-grid-follow-" + Guid.NewGuid().ToString("N");
            gridProgressRequestId = requestId;
            gridProgressRequestedUtc = now;
            try
            {
                var fullRun = startupBoot?.IsPending == true;
                await session.SendCommandAsync(fullRun
                        ? HollowKnightTAS.Core.Ipc.IpcMessageTypes.FullRunStatus
                        : HollowKnightTAS.Core.Ipc.IpcMessageTypes.RequestSnapshot,
                    fullRun ? Fields("requestId", requestId)
                        : Fields("requestId", requestId, "statusOnly", "true"),
                    System.Threading.CancellationToken.None);
            }
            catch
            {
                // A disconnected session may disappear between the timer tick and the write.
                if (gridProgressRequestId == requestId) gridProgressRequestId = null;
            }
        }

        private void TrackGridFrame(long frame, bool forceScroll = false)
        {
            if (frame < 0 || InputRows.Count == 0 || gridTotalFrames == 0) return;
            var visibleFrame = Math.Min(frame, gridTotalFrames - 1);
            foreach (var row in InputRows) row.UpdateCurrent(visibleFrame);
            if (!AutoFollowGrid) return;
            var changedPage = visibleFrame < gridPageStart
                || visibleFrame - gridPageStart >= InputRows.Count;
            if (changedPage)
            {
                gridPageStart = Math.Max(0, visibleFrame - 80);
                RefreshInputGrid();
            }
            if (forceScroll || changedPage || visibleFrame != lastFollowedGridFrame)
                ScrollGridToFrame(visibleFrame);
        }

        private void ScrollGridToFrame(long frame)
        {
            if (gridTotalFrames == 0) return;
            var visibleFrame = Math.Min(Math.Max(frame, 0), gridTotalFrames - 1);
            lastFollowedGridFrame = visibleFrame;
            InputGridPositionChanged?.Invoke(visibleFrame);
        }

        public bool TryEditGridAxes(string start, string count, bool enabled, int x, int y)
        {
            try
            {
                if (gridApplying) throw new InvalidOperationException("正在提交表格分支，请等待完成。");
                if (GridAny().V2Document != null)
                    throw new InvalidOperationException("v2 动作轴请在 Movie Text 编辑对应通道的 values。");
                EditGrid(InputGridEditor.SetAxes(GridMovie(),
                    ParseCount(start, 0, MovieProtocolV1.DefaultMaxExpandedTicks, "frame"),
                    ParseCount(count, 1, MovieProtocolV1.DefaultMaxExpandedTicks,
                        "selected frame count"), enabled, x, y));
                return true;
            }
            catch (Exception e) { GridStatus = e.Message; return false; }
        }

        private void RefreshInputGrid(string? message = null)
        {
            var source = GridAny();
            var total = source.V2Document != null ? InputGridEditor.Count(source.V2Document)
                : InputGridEditor.Count(source.V1Document!);
            gridTotalFrames = total;
            gridPageStart = Math.Min(gridPageStart, Math.Max(0, total - 1));
            InputRows.Clear();
            if (source.V2Document != null)
                foreach (var row in InputGridEditor.Page(source.V2Document,
                    gridPageStart, CurrentGridFrame))
                    InputRows.Add(new InputGridRow(row));
            else
                foreach (var row in InputGridEditor.Page(source.V1Document!,
                    gridPageStart, CurrentGridFrame)) InputRows.Add(row);
            GridStatus = $"帧 {gridPageStart}–{Math.Max(gridPageStart, gridPageStart + InputRows.Count - 1)} / 共 {total} 帧。" +
                (message ?? (source.V2Document != null
                    ? "v2 原生帧含菜单和游戏输入；表格支持插入、删除、复制与粘贴。"
                    : "点击按键切换；Shift 选择范围。编辑仅改变草稿，应用后再重放。"));
            lastFollowedGridFrame = -1;
            InputGridRefreshed?.Invoke(this, EventArgs.Empty);
        }

        private void EditGrid(MovieDocument candidate)
        {
            var text = new MovieCanonicalWriter().WriteToString(candidate);
            var validated = movieEditor.Validate(text);
            if (!validated.Success)
                throw new InvalidOperationException("编辑结果超出序列限制，草稿未改变。");
            PushGridHistory(gridUndo, MovieText);
            gridRedo.Clear();
            MovieText = gridSource = validated.CanonicalText;
            RefreshInputGrid("草稿已修改，游戏状态未改变。应用分支后可重放到选中帧。");
        }

        private void EditGrid(MovieV2EditResult result)
        {
            if (!result.Success)
                throw new InvalidOperationException(result.Diagnostics.Count > 0
                    ? result.Diagnostics[0].Message : "v2 编辑失败，草稿未改变。");
            PushGridHistory(gridUndo, MovieText);
            gridRedo.Clear();
            MovieText = gridSource = result.CanonicalText;
            RefreshInputGrid("v2 草稿已修改；从下一次 Steam 启动的第 0 帧加载并重放。");
        }

        private static MovieV2Document SliceV2(MovieV2Document movie, long start, long count)
        {
            var total = InputGridEditor.Count(movie);
            if (count < 1 || start < 0 || start > total || count > total - start)
                throw new ArgumentOutOfRangeException(nameof(count),
                    "所选原生帧超出 Movie。");
            var end = start + count;
            var position = 0L;
            var runs = new List<NativeFrameRun>();
            foreach (var run in movie.Runs)
            {
                var runEnd = position + run.RepeatCount;
                var overlapStart = Math.Max(start, position);
                var overlapEnd = Math.Min(end, runEnd);
                if (overlapEnd > overlapStart)
                    runs.Add(new NativeFrameRun(overlapEnd - overlapStart,
                        run.Samples, run.Span));
                position = runEnd;
                if (position >= end) break;
            }
            return new MovieV2Document("<grid-copy>", movie.Header, runs);
        }

        private static void PushGridHistory(Stack<string> history, string text)
        {
            if (history.Count >= 100)
            {
                var newest = history.Take(99).Reverse().ToArray();
                history.Clear();
                foreach (var entry in newest) history.Push(entry);
            }
            history.Push(text);
        }

        private void RestoreGridHistory(Stack<string> source, Stack<string> target)
        {
            GridAny();
            if (source.Count == 0)
            {
                GridStatus = "没有可撤销/重做的表格编辑。";
                return;
            }
            PushGridHistory(target, MovieText);
            MovieText = gridSource = source.Pop();
            RefreshInputGrid("草稿历史已恢复，游戏状态未改变。");
        }

        private async Task ApplyGridAsync(bool seek)
        {
            if (gridApplying) throw new InvalidOperationException("表格分支提交正在进行。");
            if (GridAny().V2Document != null)
                throw new InvalidOperationException(
                    "v2 原生帧草稿须在下一次 Steam 启动的第 0 帧打开；当前运行不能从过去重写。");
            GridMovie();
            var target = GridIndex();
            if (target > InputGridEditor.Count(GridMovie()))
                throw new InvalidOperationException("目标帧超出序列。");
            var submittedText = MovieText;
            gridApplying = true;
            try
            {
                await UploadMovieAsync();
                if (MovieText != submittedText)
                    throw new InvalidOperationException(
                        "提交期间文本已改变；刚才提交的是旧草稿，新草稿尚未应用，未发起重放。");
                if (seek)
                {
                    SeekTargetTick = target.ToString(CultureInfo.InvariantCulture);
                    await ApplyBranchAndSeekAsync();
                }
                GridStatus = seek
                    ? "分支已提交并请求重放；以恢复状态完成为准。"
                    : "分支已应用；游戏位置未回退。修改过去输入后请重放到目标帧。";
            }
            catch (Exception ex) { GridStatus = ex.Message; throw; }
            finally { gridApplying = false; }
        }
    }
}
