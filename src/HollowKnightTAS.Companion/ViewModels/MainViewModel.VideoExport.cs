using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Movie;
using Microsoft.Win32;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private string? videoRangeTreeId;
        private int? videoStartNodeId, videoEndNodeId;
        private bool videoExportBusy, videoCaptureStarted;
        private CancellationTokenSource? videoExportCancellation;
        // The scenario harness replaces only the modal file picker; it executes the real commands.
        private Func<(string Ffmpeg, string Output)?> videoExportFilePicker = PickVideoExportFiles;
        public bool IsVideoExportBusy => videoExportBusy;
        public int? VideoStartNodeId => selectedTimelineTree?.Id == videoRangeTreeId ? videoStartNodeId : null;
        public int? VideoEndNodeId => selectedTimelineTree?.Id == videoRangeTreeId ? videoEndNodeId : null;
        public string VideoRangeStatus
        {
            get
            {
                if (VideoStartNodeId == null || VideoEndNodeId == null)
                    return "选择节点并设为起点、终点；两点必须有祖先关系。";
                try
                {
                    var plan = SelectedVideoRange();
                    return $"导出范围：第 {plan.StartMovieFrame + 1}–{plan.EndMovieFrame} 帧（起点之后至终点）。";
                }
                catch (Exception exception) { return exception.Message; }
            }
        }
        public ICommand MarkVideoStartCommand => new RelayCommand(() => MarkVideoPoint(true), CanMarkVideoPoint);
        public ICommand MarkVideoEndCommand => new RelayCommand(() => MarkVideoPoint(false), CanMarkVideoPoint);
        public ICommand ClearVideoRangeCommand => new RelayCommand(() =>
        {
            videoRangeTreeId = null; videoStartNodeId = videoEndNodeId = null; NotifyVideoRange();
        }, () => !gridApplying && !videoExportBusy);
        public ICommand ExportTimelineVideoCommand => new AsyncRelayCommand(
            () => ExportStudioVideoAsync(true), () => CanStartStudioVideo() && VideoStartNodeId != null && VideoEndNodeId != null);

        private bool CanMarkVideoPoint() => !gridApplying && !videoExportBusy && selectedTimelineNode != null;
        private bool CanStartStudioVideo() => !gridApplying && !videoExportBusy
            && string.IsNullOrEmpty(videoExportOperationId)
            && fullRunMovies?.IsPending == true && restartProtectedGame != null
            && (startupBoot?.FullRunFaultCode ?? 0) == 0;
        private void MarkVideoPoint(bool start)
        {
            if (!CanMarkVideoPoint()) return;
            if (videoRangeTreeId != selectedTimelineTree!.Id)
            { videoStartNodeId = videoEndNodeId = null; videoRangeTreeId = selectedTimelineTree.Id; }
            if (start) videoStartNodeId = selectedTimelineNode!.Id; else videoEndNodeId = selectedTimelineNode!.Id;
            if (videoStartNodeId != null && videoEndNodeId != null)
            {
                try
                {
                    _ = SelectedVideoRange();
                    if (selectedTimelineTree.PathTo(videoStartNodeId.Value).Any(n => n.Id == videoEndNodeId))
                        (videoStartNodeId, videoEndNodeId) = (videoEndNodeId, videoStartNodeId);
                }
                catch (Exception) { /* Retain both choices so the range message explains the invalid pair. */ }
            }
            NotifyVideoRange();
        }
        private StudioVideoExportPlan SelectedVideoRange()
        {
            if (selectedTimelineTree == null || VideoStartNodeId == null || VideoEndNodeId == null)
                throw new InvalidOperationException("请先在同一时间线选择两个导出端点。");
            return StudioVideoExportPlan.ForTimeline(selectedTimelineTree, VideoStartNodeId.Value, VideoEndNodeId.Value);
        }
        private void NotifyVideoRange()
        {
            OnPropertyChanged(nameof(VideoStartNodeId)); OnPropertyChanged(nameof(VideoEndNodeId));
            OnPropertyChanged(nameof(VideoRangeStatus)); OnPropertyChanged(nameof(ExportTimelineVideoCommand));
            OnPropertyChanged(nameof(MarkVideoStartCommand)); OnPropertyChanged(nameof(MarkVideoEndCommand));
            OnPropertyChanged(nameof(ClearVideoRangeCommand));
        }
        private void SetVideoExportBusy(bool value)
        {
            videoExportBusy = value;
            OnPropertyChanged(nameof(IsVideoExportBusy));
            SetGridApplying(value);
            NotifyVideoRange();
        }

        private static (string Ffmpeg, string Output)? PickVideoExportFiles()
        {
            var ffmpeg = BundledFfmpeg.Resolve();
            var output = new SaveFileDialog { Title = UiText.T("选择 MP4 导出文件（不会覆盖已有文件）"),
                Filter = UiText.T("MP4 视频|*.mp4"), DefaultExt = ".mp4", AddExtension = true,
                OverwritePrompt = false, FileName = "hktas-export.mp4" };
            return output.ShowDialog() == true ? (ffmpeg, output.FileName) : null;
        }

        private async Task StartVideoExportAsync()
        {
            if (fullRunMovies?.IsPending == true) await ExportStudioVideoAsync(false);
            else await StartLegacyVideoExportAsync();
        }

        private async Task ExportStudioVideoAsync(bool timelineRange)
        {
            if (!CanStartStudioVideo()) return;
            SetVideoExportBusy(true);
            videoExportCancellation = new CancellationTokenSource();
            var token = videoExportCancellation.Token;
            string[]? savedUndo = null, savedRedo = null;
            string? savedMovie = null;
            InitialSaveSnapshot? savedInitialSaves = null;
            bool savedEdits = false;
            long savedEarliestEdit = long.MaxValue;
            string? operationId = null;
            bool startRequested = false;
            try
            {
                // Synchronize recorded input before freezing the export, preserving unsaved edits.
                await SaveCurrentBranchAsync(closing: true);
                token.ThrowIfCancellationRequested();
                var plan = timelineRange ? SelectedVideoRange() : StudioVideoExportPlan.ForMovie(MovieText);
                var files = videoExportFilePicker();
                if (files == null) return;
                if (!File.Exists(files.Value.Ffmpeg)) throw new FileNotFoundException("FFmpeg 不存在。", files.Value.Ffmpeg);
                if (File.Exists(files.Value.Output)) throw new IOException("导出文件已存在；请选择新的 .mp4 文件。");
                if (!string.Equals(Path.GetExtension(files.Value.Output), ".mp4", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("导出文件必须使用 .mp4 扩展名。");
                fullRunMovies!.VerifyOriginalSavesUnchanged();
                var exportInitialSaves = plan.TreeId == null ? sequenceInitialSaves
                    : GetTimelineInitialSaves(worldlines!.Library.Trees.Single(t => t.Id == plan.TreeId));
                savedMovie = MovieText; savedEdits = gridHasUserEdits; savedEarliestEdit = earliestGridEdit;
                savedInitialSaves = sequenceInitialSaves;
                savedUndo = gridUndo.ToArray(); savedRedo = gridRedo.ToArray();
                SetSequenceInitialSaves(exportInitialSaves);
                VideoExportStatus = $"准备导出：正在恢复到第 {plan.StartMovieFrame} 帧，准备过程不会录入视频。";
                await RestartDraftAtAsync(plan.StartMovieFrame, plan.Movie, token, pauseWhenInputReadyZero: true, saveCurrentBranch: false);
                token.ThrowIfCancellationRequested();
                // Native frame zero precedes Unity initialization. Run only the bootstrap;
                // pauseAtMovieFrame=0 stops before the very first input is consumed.
                if (plan.StartMovieFrame == 0)
                {
                    var started = await fullRunMovies.RunAsync(0, token);
                    if (started.Mode == "Fault") throw new InvalidOperationException(started.Error);
                    await WaitVideoPauseAsync(token);
                }
                var snapshot = await ReadReadyFrameSnapshotAsync();
                if (snapshot.Frame != plan.StartMovieFrame)
                    throw new InvalidOperationException($"导出起点不一致：需要 {plan.StartMovieFrame}，实际 {snapshot.Frame}。");
                token.ThrowIfCancellationRequested();
                var maximumFrames = int.MaxValue;
                startRequested = true;
                var result = await automationBroker.ExecuteHumanAsync(AutomationCommandIds.StartVideoExport,
                    AutomationScope.ControlPlayback, new Dictionary<string, string>
                    {
                        ["ffmpegPath"] = Path.GetFullPath(files.Value.Ffmpeg),
                        ["outputPath"] = Path.GetFullPath(files.Value.Output),
                        ["maximumFrames"] = maximumFrames.ToString(CultureInfo.InvariantCulture),
                        ["replayLoadedMovie"] = "true",
                        ["endMovieFrame"] = plan.EndMovieFrame.ToString(CultureInfo.InvariantCulture)
                    }, "Paused", null, CancellationToken.None);
                RequireAutomationSuccess(result);
                operationId = RequireResultField(result, "detail");
                videoExportOperationId = operationId;
                videoCaptureStarted = true;
                SetGridApplying(true); // Refresh Pause/Continue and Cancel while edits remain locked.
                VideoExportStatus = $"正在导出第 {plan.StartMovieFrame + 1}–{plan.EndMovieFrame} 帧 · {files.Value.Output}";
                var deadline = DateTime.UtcNow.AddHours(2);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var status = await automationBroker.ExecuteHumanAsync(AutomationCommandIds.FullRunStatus,
                        AutomationScope.ObserveStatus, null, string.Empty, null, CancellationToken.None);
                    RequireAutomationSuccess(status);
                    if (!status.Data.TryGetValue("videoExport.operationId", out var observedId) || observedId != operationId)
                        throw new InvalidOperationException("导出会话已断开或发生改变。");
                    var phase = status.Data["videoExport.state"];
                    if (videoCaptureStarted != (phase == "Capturing"))
                    { videoCaptureStarted = phase == "Capturing"; SetGridApplying(true); }
                    var frames = status.Data.TryGetValue("videoExport.frames", out var count) ? count : "0";
                    VideoExportStatus = $"阶段={phase} · 帧数={frames} · 输出={files.Value.Output}";
                    if (phase == "Completed")
                    {
                        if (!File.Exists(files.Value.Output)) throw new IOException("导出已结束，但未找到 MP4 文件。");
                        Status = "MP4 导出完成。";
                        break;
                    }
                    if (phase == "Cancelled") throw new OperationCanceledException();
                    if (phase == "Failed") throw new InvalidOperationException(status.Data["videoExport.detail"]);
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("视频导出超时。");
                    await Task.Delay(200, token);
                }
                fullRunMovies.VerifyOriginalSavesUnchanged();
            }
            catch (Exception exception)
            {
                if (videoExportBusy)
                {
                    try
                    {
                        // A start acknowledgement can be lost after Runtime accepted it.
                        // This newly restarted session had no previous export to confuse it with.
                        if (startRequested && operationId == null)
                        {
                            var observed = await automationBroker.ExecuteHumanAsync(AutomationCommandIds.FullRunStatus,
                                AutomationScope.ObserveStatus, null, string.Empty, null, CancellationToken.None);
                            if (observed.Success && observed.Data.TryGetValue("videoExport.state", out var phase)
                                && (phase == "Capturing" || phase == "Finalizing")
                                && observed.Data.TryGetValue("videoExport.operationId", out var accepted)) operationId = accepted;
                        }
                        if (!string.IsNullOrEmpty(operationId))
                            await CancelFullRunVideoAsync(operationId);
                        else if (fullRunMovies?.IsArmed == true && startupBoot?.IsWaiting != true)
                            await fullRunMovies.PauseAsync(CancellationToken.None);
                    }
                    catch (Exception cleanup) { exception = new InvalidOperationException(exception.Message + "；停止导出失败：" + cleanup.Message); }
                }
                VideoExportStatus = Status = exception is OperationCanceledException ? "MP4 导出已取消。" : "MP4 导出失败：" + exception.Message;
            }
            finally
            {
                if (savedMovie != null)
                {
                    SetSequenceInitialSaves(savedInitialSaves);
                    MovieText = gridSource = savedMovie;
                    gridHasUserEdits = savedEdits; earliestGridEdit = savedEarliestEdit;
                    gridUndo.Clear(); gridRedo.Clear();
                    foreach (var item in savedUndo!.Reverse()) gridUndo.Push(item);
                    foreach (var item in savedRedo!.Reverse()) gridRedo.Push(item);
                    // Export may replay another saved branch. Never resume it as the editor's draft.
                    draftRequiresRestart = true;
                    RefreshInputGrid();
                }
                videoCaptureStarted = false;
                videoExportCancellation?.Dispose(); videoExportCancellation = null;
                videoExportOperationId = string.Empty;
                if (videoExportBusy) SetVideoExportBusy(false);
            }
        }

        private async Task WaitVideoPauseAsync(CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                startupBoot!.Refresh();
                if (startupBoot.FullRunFaultCode != 0) throw new InvalidOperationException("重放遇到错误：" + startupBoot.FullRunFaultCode);
                if (startupBoot.IsWaiting && startupBoot.NativeCompletedFrames > 0) return;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("导出起点恢复超时。");
                await Task.Delay(100, token);
            }
        }
        private async Task CancelFullRunVideoAsync(string operationId)
        {
            var result = await automationBroker.ExecuteHumanAsync(AutomationCommandIds.CancelVideoExport,
                AutomationScope.ControlPlayback, new Dictionary<string, string> { ["operationId"] = operationId },
                string.Empty, null, CancellationToken.None);
            RequireAutomationSuccess(result);
        }
        private async Task ToggleVideoPauseAsync()
        {
            if (!videoCaptureStarted || fullRunMovies?.IsArmed != true) return;
            if (startupBoot!.IsWaiting)
            {
                var status = await automationBroker.ExecuteHumanAsync(AutomationCommandIds.FullRunStatus,
                    AutomationScope.ObserveStatus, null, string.Empty, null, CancellationToken.None);
                RequireAutomationSuccess(status);
                if (!status.Data.TryGetValue("videoExport.state", out var phase) || phase != "Capturing") return;
                var boundary = await fullRunMovies.RunAsync(startupBoot.NativeCompletedFrames, CancellationToken.None);
                if (boundary.Mode == "Fault") throw new InvalidOperationException(boundary.Error);
            }
            else await fullRunMovies.PauseAsync(CancellationToken.None);
        }
        public async Task CancelVideoExportAndWaitAsync()
        {
            if (!videoExportBusy) return;
            videoExportCancellation?.Cancel();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (videoExportBusy)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("正在停止视频导出，请稍后重试关闭。");
                await Task.Delay(50);
            }
        }
        private async Task CancelVideoExportAsync()
        {
            if (videoExportCancellation != null)
            {
                videoExportCancellation.Cancel();
                VideoExportStatus = "正在取消 MP4 导出…";
                return;
            }
            if (fullRunMovies?.IsPending == true && !string.IsNullOrEmpty(videoExportOperationId))
                await CancelFullRunVideoAsync(videoExportOperationId);
            else await CancelLegacyVideoExportAsync();
        }
    }
}
