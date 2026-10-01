using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Automation;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private static string FrameSaveRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-frame-saves");
        public bool HasFrameSave(long frame) { InitializeWorldlines(); return FindTimelineFrame(frame) != null; }
        private sealed class FrameSave
        {
            public long Frame { get; set; }
            public string Movie { get; set; } = "";
            public Dictionary<string, string> OriginalHashes { get; set; } = new();
        }

        public bool CanNavigateFrame(long frame, bool forward)
            => !gridApplying && fullRunMovies?.IsPending == true
                && currentFullRunMovieFrame >= 0 && frame >= 0 && frame <= gridTotalFrames
                && (forward ? frame > currentFullRunMovieFrame && fullRunMovies.Mode != "Completed"
                    : frame < currentFullRunMovieFrame);

        public async Task FrameMenuAsync(string action, long frame)
        {
            if (gridApplying) return;
            SetGridApplying(true);
            try
            {
                if (fullRunMovies?.IsPending != true)
                    throw new InvalidOperationException("帧存档与定位需要从 Studio 启动全流程会话。");
                if (action == "load" || action == "rebuild")
                    await fullRunMovies.PauseForDocumentChangeAsync(CancellationToken.None);
                else if (startupBoot?.IsWaiting != true)
                    await fullRunMovies.PauseAsync(CancellationToken.None);
                if (action == "load")
                {
                    InitializeWorldlines();
                    var node = FindTimelineFrame(frame) ?? throw new InvalidOperationException("所选世界线此帧尚未存档，请到时间线选择其他分支。");
                    await RestoreTimelineCoreAsync(selectedTimelineTree!, selectedWorldline!, node);
                }
                else if (action == "rebuild")
                {
                    if (frame < 0 || frame > gridTotalFrames)
                        throw new InvalidOperationException("目标帧超出序列。");
                    await RestartDraftAtAsync(frame);
                }
                else if (action == "seek" || action == "restore")
                {
                    if (frame < 0 || frame > gridTotalFrames)
                        throw new InvalidOperationException("目标帧超出序列。");
                    var snapshot = await ReadReadyFrameSnapshotAsync();
                    if (action == "seek" ? frame <= snapshot.Frame : frame >= snapshot.Frame)
                        throw new InvalidOperationException(action == "seek"
                            ? "播放到帧只能选择当前帧之后的未来帧。" : "恢复到帧只能选择当前帧之前的过去帧。");
                    if (action == "seek" && !draftRequiresRestart) await PlayForwardToFrameAsync(frame);
                    else await RestartDraftAtAsync(frame);
                }
                else if (action == "save")
                {
                    // Saving records a replay target. It never navigates the game to that target.
                    var observed = await ReadReadyFrameSnapshotAsync();
                    if (frame == -1) frame = observed.Frame;
                    var draft = movieEditor.ValidateAny(MovieText).V2Document
                        ?? throw new InvalidOperationException("序列无效，请打开有效的 .hktas 文件。");
                    var actual = TimelineTree.Parse(observed.Movie);
                    if (!draftRequiresRestart && startupBoot?.NativeCompletedFrames > 0
                        && fullRunMovies.Mode == "Recording" && !gridHasUserEdits)
                    {
                        var total = InputGridEditor.Count(draft);
                        draft = new MovieV2Document(draft.SourceName, actual.Header,
                            actual.Runs.Concat(total > observed.Frame
                                ? SliceV2(draft, observed.Frame, total - observed.Frame).Runs
                                : Array.Empty<NativeFrameRun>()));
                    }
                    else if (!draftRequiresRestart && draft.Header.EnvironmentSha256 == "none")
                        draft = new MovieV2Document(draft.SourceName, actual.Header, draft.Runs);
                    if (frame < 0 || frame > InputGridEditor.Count(draft))
                        throw new InvalidOperationException("目标帧超出序列。");
                    var snapshot = new FrameSave { Frame = frame,
                        Movie = new MovieV2Codec().WriteCanonical(draft),
                        OriginalHashes = new Dictionary<string, string>(fullRunMovies.OriginalHashes) };
                    SaveTimelineSnapshot(snapshot, observed);

                }
            }
            catch (Exception ex) { GridStatus = Status = ex.Message; }
            finally { SetGridApplying(false); }
        }

        private async Task<FrameSave> ReadFrameSnapshotAsync()
        {
            if (startupBoot?.NativeCompletedFrames == 0) return new FrameSave { Frame = 0, Movie = MovieText };
            var result = await automationBroker!.ExecuteHumanAsync(AutomationCommandIds.FullRunSnapshot,
                AutomationScope.MovieRead, null, "Paused", null, CancellationToken.None);
            RequireAutomationSuccess(result);
            var path = Path.GetFullPath(result.Data["path"]);
            if (!path.StartsWith(Path.GetFullPath(fullRunMovies!.ShadowRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || new FileInfo(path).Length > MovieProtocolV2.MaximumSourceUtf8Bytes)
                throw new InvalidDataException("存档 Movie 路径或大小无效。");
            var movie = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true));
            var candidate = movieEditor.ValidateAny(movie);
            if (!candidate.Success || candidate.V2Document == null) throw new InvalidDataException("存档 Movie 无效。");
            return new FrameSave { Frame = long.Parse(result.Data["movieFrame"], CultureInfo.InvariantCulture), Movie = movie };
        }

        private async Task<FrameSave> ReadReadyFrameSnapshotAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (true)
            {
                try { return await ReadFrameSnapshotAsync(); }
                catch (InvalidOperationException) when (DateTime.UtcNow < deadline) { await Task.Delay(200); }
            }
        }

        private async Task PlayForwardToFrameAsync(long frame)
        {
            if (startupBoot!.NativeCompletedFrames == 0)
            {
                if (gridHasUserEdits && earliestGridEdit == 0)
                    throw new InvalidOperationException("启动第 0 帧的输入已修改，请先应用草稿；播放到未来帧不会自动重启。");
                var first = await fullRunMovies!.StepAsync(0, CancellationToken.None);
                if (first.Mode == "Fault") throw new InvalidOperationException(first.Error);
                var initialized = await ReadReadyFrameSnapshotAsync();
                if (initialized.Frame == frame)
                {
                    currentFullRunMovieFrame = frame;
                    TrackGridFrame(frame, true);
                    GridStatus = $"已从当前进度播放到第 {frame} 帧并暂停。";
                    return;
                }
            }
            if (fullRunMovies!.Mode == "Completed")
                throw new InvalidOperationException("固定回放已结束；未来帧播放不会自动重启游戏。");
            await ApplyPendingInputsAsync(withinGridOperation: true);
            var result = await automationBroker.ExecuteHumanAsync(AutomationCommandIds.FullRunSeek,
                AutomationScope.ControlPlayback, new Dictionary<string, string>
                {
                    ["targetFrame"] = frame.ToString(CultureInfo.InvariantCulture),
                    ["expectedNativeFrame"] = startupBoot!.NativeCompletedFrames.ToString(CultureInfo.InvariantCulture)
                }, "Paused", null, CancellationToken.None);
            RequireAutomationSuccess(result);
            var started = await fullRunMovies.RunAsync(startupBoot.NativeCompletedFrames, CancellationToken.None);
            if (started.Mode == "Fault") throw new InvalidOperationException(started.Error);
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (true)
            {
                startupBoot.Refresh();
                if (startupBoot.FullRunFaultCode != 0) throw new InvalidOperationException("播放遇到原生错误：" + startupBoot.FullRunFaultCode);
                if (startupBoot.IsWaiting) break;
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("播放到指定帧超时。");
                await Task.Delay(100);
            }
            var snapshot = await ReadReadyFrameSnapshotAsync();
            if (snapshot.Frame != frame) throw new InvalidOperationException($"播放停在 {snapshot.Frame}，未到达目标 {frame}。");
            currentFullRunMovieFrame = frame;
            TrackGridFrame(frame, true);
            GridStatus = $"已从当前进度播放到第 {frame} 帧并暂停。";
        }

        private async Task RestartDraftAtAsync(long frame, string? sourceMovie = null, CancellationToken cancellationToken = default, bool pauseWhenInputReadyZero = false,
            bool switchSequence = false, InitialSaveSnapshot? sourceSaves = null, bool saveCurrentBranch = true)
        {
            using var timing = new ReplayPerformanceTrace("restore");
            if (saveCurrentBranch) await SaveCurrentBranchAsync(closing: true);
            timing.Mark("branch-saved");
            if (switchSequence)
            {
                SetSequenceInitialSaves(sourceSaves);
            }
            SetRestorePresentationFrozen(true);
            restoreTargetFrame = frame;
            var completed = false;
            try
            {
                if (sourceMovie != null) MovieText = sourceMovie;
                await RestartDraftCoreAsync(frame, cancellationToken, pauseWhenInputReadyZero, timing);
                SetRestoreProgress(1);
                completed = true;
            }
            finally
            {
                try { if (finishRestorePresentation != null) await finishRestorePresentation(completed); }
                finally { SetRestorePresentationFrozen(false); }
            }
            timing.Complete();
        }

        private async Task RestartDraftCoreAsync(long frame, CancellationToken cancellationToken = default, bool pauseWhenInputReadyZero = false,
            ReplayPerformanceTrace? timing = null)
        {
            var candidate = GridAny().V2Document ?? throw new InvalidOperationException("需要有效的 v2 Movie。");
            if (candidate.Header.EnvironmentSha256 == "none" && sequenceInitialSaves != null
                && sequenceInitialSaves.Id != fullRunMovies?.SessionInitialSaves?.Id)
            {
                if (restartProtectedGame == null) throw new InvalidOperationException("受控重启入口不可用。");
                await restartProtectedGame();
            }
            if (candidate.Header.EnvironmentSha256 == "none" && startupBoot?.NativeCompletedFrames == 0)
            {
                if (fullRunMovies!.Mode == "Unarmed") fullRunMovies.ArmRecording(candidate.Header.MouseEnabled, ParseFrameRate(DefaultFrameRate));
                if (fullRunMovies.Mode != "Recording") throw new InvalidOperationException("草稿环境尚未初始化，请新建录制后重试。");
                await fullRunMovies.StepAsync(0, cancellationToken);
            }
            if (candidate.Header.EnvironmentSha256 == "none" && startupBoot?.NativeCompletedFrames > 0)
            {
                var snapshot = await ReadReadyFrameSnapshotAsync();
                var recorded = movieEditor.ValidateAny(snapshot.Movie).V2Document!;
                candidate = new MovieV2Document(candidate.SourceName, recorded.Header.WithCustomKeys(candidate.Header.CustomKeys), candidate.Runs);
                MovieText = gridSource = new MovieV2Codec().WriteCanonical(candidate);
            }
            if (frame > InputGridEditor.Count(candidate)) throw new InvalidOperationException("存档目标超出 Movie。");
            if (frame == InputGridEditor.Count(candidate))
                candidate = new MovieV2Document(candidate.SourceName, candidate.Header, candidate.Runs.Concat(new[] {
                    new NativeFrameRun(500, Array.Empty<GameInputSample>(), new MovieSourceSpan("<resume>", 1, 1, 1), ParseFrameRate(DefaultFrameRate), true) }));
            MovieText = gridSource = new MovieV2Codec().WriteCanonical(candidate);
            if (restartProtectedGame == null) throw new InvalidOperationException("受控重启入口不可用。");
            GridStatus = "正在恢复，请稍候…";
            cancellationToken.ThrowIfCancellationRequested();
            timing?.Mark("movie-prepared");
            await restartProtectedGame();
            timing?.Mark("restart-ready");
            cancellationToken.ThrowIfCancellationRequested();
            fullRunMovies!.ArmReplay(candidate, frame == 0 && !pauseWhenInputReadyZero ? -1 : frame);
            timing?.Mark("replay-armed");
            restoreReplayStarted = true;
            currentFullRunMovieFrame = 0;
            RefreshInputGrid();
            if (frame > 0)
            {
                var started = await fullRunMovies.RunAsync(0, cancellationToken);
                if (started.Mode == "Fault") throw new InvalidOperationException(started.Error);
                var deadline = DateTime.UtcNow.AddMinutes(10);
                var progressObserved = false;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    startupBoot!.Refresh();
                    if (!progressObserved && currentFullRunMovieFrame > 0)
                    {
                        timing?.Mark("first-movie-progress-observed");
                        progressObserved = true;
                    }
                    if (startupBoot.FullRunFaultCode != 0) throw new InvalidOperationException("重放遇到原生错误：" + startupBoot.FullRunFaultCode);
                    if (startupBoot.IsWaiting) break;
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("重放等待超时，可手动暂停检查状态。");
                    await Task.Delay(100, cancellationToken);
                }
            }
            if (frame > 0)
            {
                var observed = await ReadReadyFrameSnapshotAsync();
                if (observed.Frame != frame) throw new InvalidOperationException($"重放停在 {observed.Frame}，未到达目标 {frame}。");
            }
            timing?.Mark("target-verified");
            currentFullRunMovieFrame = frame;
            gridHasUserEdits = false;
            draftRequiresRestart = false;
            earliestGridEdit = long.MaxValue;
            TrackGridFrame(frame, true);
            GridStatus = $"已停在第 {frame} 帧边界。";
        }
    }
}
