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
        private readonly Dictionary<long, string> frameSaves = new();
        private static string FrameSaveRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-frame-saves");
        private void LoadFrameSaveIndex()
        {
            var path = Path.Combine(FrameSaveRoot, "index.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return;
            var index = JsonSerializer.Deserialize<Dictionary<long, string>>(File.ReadAllText(path));
            if (index == null) return;
            foreach (var item in index)
                if (item.Key >= 0 && item.Key <= MovieProtocolV2.MaximumExpandedFrames
                    && Path.GetFullPath(item.Value).StartsWith(FrameSaveRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(item.Value)) frameSaves[item.Key] = item.Value;
        }
        public bool HasFrameSave(long frame) { InitializeWorldlines(); return FindTimelineFrame(frame) != null; }
        private sealed class FrameSave
        {
            public long Frame { get; set; }
            public string Movie { get; set; } = "";
            public Dictionary<string, string> OriginalHashes { get; set; } = new();
        }

        public async Task FrameMenuAsync(string action, long frame)
        {
            if (gridApplying) return;
            SetGridApplying(true);
            try
            {
                if (fullRunMovies?.IsPending != true)
                    throw new InvalidOperationException("帧存档与定位需要从 Studio 启动全流程会话。");
                if (startupBoot?.IsWaiting != true)
                    await fullRunMovies.PauseAsync(CancellationToken.None);
                if (action == "load")
                {
                    InitializeWorldlines();
                    var node = FindTimelineFrame(frame) ?? throw new InvalidOperationException("所选世界线此帧尚未存档，请到时间线选择其他分支。");
                    await RestoreTimelineCoreAsync(selectedTimelineTree!, selectedWorldline!, node);
                }
                else if (action == "seek") await SeekFrameAsync(frame);
                else if (action == "save")
                {
                    // Query the actual paused frame, never label a stale UI selection as a save.
                    var snapshot = await ReadFrameSnapshotAsync();
                    if (frame == -1) frame = snapshot.Frame;
                    if (snapshot.Frame != frame)
                    {
                        await SeekFrameAsync(frame);
                        snapshot = await ReadFrameSnapshotAsync();
                    }
                    if (snapshot.Frame != frame) throw new InvalidOperationException("游戏没有停在所选帧，未创建存档。");
                    snapshot.OriginalHashes = new Dictionary<string, string>(fullRunMovies.OriginalHashes);
                    SaveTimelineSnapshot(snapshot);
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

        private async Task SeekFrameAsync(long frame)
        {
            if (frame < 0 || frame > gridTotalFrames) throw new InvalidOperationException("目标帧超出序列。");
            // Rebuild from the draft so edits in the past are applied consistently.
            await RestartDraftAtAsync(frame);
        }

        private async Task RestartDraftAtAsync(long frame)
        {
            var candidate = GridAny().V2Document ?? throw new InvalidOperationException("需要有效的 v2 Movie。");
            if (candidate.Header.EnvironmentSha256 == "none" && startupBoot?.NativeCompletedFrames == 0)
            {
                if (fullRunMovies!.Mode == "Unarmed") fullRunMovies.ArmRecording(candidate.Header.MouseEnabled, ParseFrameRate(DefaultFrameRate));
                if (fullRunMovies.Mode != "Recording") throw new InvalidOperationException("草稿环境尚未初始化，请新建录制后重试。");
                await fullRunMovies.StepAsync(0, CancellationToken.None);
            }
            if (candidate.Header.EnvironmentSha256 == "none" && startupBoot?.NativeCompletedFrames > 0)
            {
                var snapshot = await ReadReadyFrameSnapshotAsync();
                var recorded = movieEditor.ValidateAny(snapshot.Movie).V2Document!;
                candidate = new MovieV2Document(candidate.SourceName, recorded.Header, candidate.Runs);
                MovieText = gridSource = new MovieV2Codec().WriteCanonical(candidate);
            }
            if (frame > InputGridEditor.Count(candidate)) throw new InvalidOperationException("存档目标超出 Movie。");
            if (frame == InputGridEditor.Count(candidate))
                candidate = new MovieV2Document(candidate.SourceName, candidate.Header, candidate.Runs.Concat(new[] {
                    new NativeFrameRun(500, Array.Empty<GameInputSample>(), new MovieSourceSpan("<resume>", 1, 1, 1), ParseFrameRate(DefaultFrameRate), true) }));
            MovieText = gridSource = new MovieV2Codec().WriteCanonical(candidate);
            if (restartProtectedGame == null) throw new InvalidOperationException("受控重启入口不可用。");
            GridStatus = $"正在重启并重放到第 {frame} 帧…";
            await restartProtectedGame();
            fullRunMovies!.ArmReplay(candidate, frame);
            currentFullRunMovieFrame = 0;
            RefreshInputGrid();
            if (frame > 0)
            {
                var started = await fullRunMovies.RunAsync(0, CancellationToken.None);
                if (started.Mode == "Fault") throw new InvalidOperationException(started.Error);
                var deadline = DateTime.UtcNow.AddMinutes(10);
                while (true)
                {
                    startupBoot!.Refresh();
                    if (startupBoot.FullRunFaultCode != 0) throw new InvalidOperationException("重放遇到原生错误：" + startupBoot.FullRunFaultCode);
                    if (startupBoot.IsWaiting) break;
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("重放等待超时，可手动暂停检查状态。");
                    await Task.Delay(100);
                }
            }
            if (frame > 0)
            {
                var observed = await ReadReadyFrameSnapshotAsync();
                if (observed.Frame != frame) throw new InvalidOperationException($"重放停在 {observed.Frame}，未到达目标 {frame}。");
            }
            currentFullRunMovieFrame = frame;
            gridHasUserEdits = false;
            earliestGridEdit = long.MaxValue;
            TrackGridFrame(frame, true);
            GridStatus = $"已停在第 {frame} 帧边界。";
        }
    }
}
