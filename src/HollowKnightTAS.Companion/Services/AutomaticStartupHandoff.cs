using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>One attempt per authenticated source session; never force-kills a source game.</summary>
    public sealed class AutomaticStartupHandoff : IDisposable
    {
        private readonly SessionRegistry sessions;
        private readonly Dispatcher dispatcher;
        private readonly Func<string, Task> validate;
        private readonly Func<string, Task> launch;
        private readonly Func<bool> busy;
        private readonly Action<string> report;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly HashSet<string> attempted = new HashSet<string>(StringComparer.Ordinal);
        private bool disposed;
        public bool IsActive { get; private set; }

        public AutomaticStartupHandoff(SessionRegistry sessions, Dispatcher dispatcher,
            Func<string, Task> validate, Func<string, Task> launch, Func<bool> busy, Action<string> report)
        {
            this.sessions = sessions; this.dispatcher = dispatcher;
            this.validate = validate; this.launch = launch; this.busy = busy; this.report = report;
            sessions.SessionsChanged += OnSessionsChanged;
        }

        public void Check() => OnSessionsChanged(this, EventArgs.Empty);

        private void OnSessionsChanged(object? sender, EventArgs args)
        {
            if (disposed) return;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                if (disposed || IsActive || busy()) return;
                foreach (var session in sessions.Sessions)
                {
                    if (!session.IsConnected || !attempted.Add(session.SessionId)) continue;
                    IsActive = true;
                    try { await TryHandoffAsync(session, shutdown.Token); }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
                    catch (Exception error) { report("自动启动接管未完成：" + error.Message); }
                    finally { IsActive = false; }
                    if (disposed || busy()) return;
                }
            }));
        }

        private async Task TryHandoffAsync(RuntimeSessionClient session, CancellationToken cancellation)
        {
            report("正在检查普通启动接管条件，游戏 PID " + session.GameProcessId);
            using var source = Process.GetProcessById(session.GameProcessId);
            if (source.StartTime.ToUniversalTime().Ticks != session.GameProcessStartTimeUtcTicks)
                throw new InvalidOperationException("游戏进程身份已改变，未执行接管。");
            var gamePath = source.MainModule?.FileName ?? throw new InvalidOperationException("无法读取游戏路径。");
            if (!string.Equals(Path.GetFileName(gamePath), "hollow_knight.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("注册进程不是 Hollow Knight。");
            var operation = "startup-" + Guid.NewGuid().ToString("N");
            string result;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            do
            {
                result = await RequestAsync(session, operation, "prepare", cancellation);
                if (result != "wait-for-title") break;
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("游戏未进入安全的启动标题阶段；保留原进程。");
                await Task.Delay(250, cancellation);
            } while (true);
            report("普通启动接管准备结果：" + result);
            if (result == "already-controlled" || result == "ineligible") return;
            if (result != "prepared") throw new InvalidOperationException("启动接管准备回执不匹配。");

            // Complete all package checks while the original game is still alive.
            await validate(gamePath);
            report("正在接管普通启动：校验已完成，等待标题进程正常退出…");
            // A committed source may exit before its final reply drains. Only a
            // previously prepared, matching Process handle is accepted as proof.
            try { await RequestAsync(session, operation, "commit", cancellation); }
            catch (Exception error) when ((error is TimeoutException || error is IOException)
                && source.HasExited) { }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await source.WaitForExitAsync(timeout.Token);
            }
            report("普通启动已退出，正在以首帧暂停模式重新启动…");
            await launch(gamePath);
        }

        private static async Task<string> RequestAsync(RuntimeSessionClient session, string operation,
            string phase, CancellationToken cancellation)
        {
            var requestId = "startup-request-" + Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnEnvelope(object? sender, IpcEnvelope envelope)
            {
                if (envelope.MessageType != IpcMessageTypes.CommandAccepted
                    && envelope.MessageType != IpcMessageTypes.CommandRejected) return;
                var payload = IpcPayloadCodec.TryDeserialize(envelope.PayloadUtf8);
                if (!payload.Success || payload.Fields == null
                    || !payload.Fields.TryGetValue("requestId", out var id) || id != requestId) return;
                var detail = payload.Fields.TryGetValue("detail", out var text) ? text : "Missing startup reply.";
                if (envelope.MessageType == IpcMessageTypes.CommandAccepted) completion.TrySetResult(detail);
                else completion.TrySetException(new InvalidOperationException(detail));
            }
            session.EnvelopeReceived += OnEnvelope;
            try
            {
                await session.SendCommandAsync(IpcMessageTypes.StartupHandoff, new Dictionary<string, string>
                {
                    ["requestId"] = requestId, ["operationId"] = operation, ["phase"] = phase,
                    ["processId"] = session.GameProcessId.ToString(CultureInfo.InvariantCulture),
                    ["processStartTimeUtcTicks"] = session.GameProcessStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture)
                }, cancellation);
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellation);
            }
            finally { session.EnvelopeReceived -= OnEnvelope; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            sessions.SessionsChanged -= OnSessionsChanged;
            shutdown.Cancel();
            // In-flight handlers retain the token until their awaited calls end.
        }
    }
}
