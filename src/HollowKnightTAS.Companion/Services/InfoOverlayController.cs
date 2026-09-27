using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class InfoOverlayController : IDisposable
    {
        private readonly Func<RuntimeSessionClient?> sessionProvider;
        private readonly Func<RuntimeSessionClient, IReadOnlyDictionary<string, string>, CancellationToken,
            Task<IReadOnlyDictionary<string, string>>> request;
        private readonly Func<bool> suspended;
        private readonly Action<string> report;
        private readonly Action<double, double> moved;
        private readonly DispatcherTimer timer;
        private readonly CancellationTokenSource cancellation = new();
        private InfoOverlaySettings settings = InfoOverlaySettings.Defaults();
        private InfoOverlayWindow? window;
        private RuntimeSessionClient? displayedSession;
        private bool busy, disposed, adjusting;
        private long generation;

        public InfoOverlayController(Func<RuntimeSessionClient?> sessionProvider,
            Func<RuntimeSessionClient, IReadOnlyDictionary<string, string>, CancellationToken, Task<IReadOnlyDictionary<string, string>>> request,
            Func<bool> suspended, Action<string> report, Action<double, double> moved)
        {
            this.sessionProvider = sessionProvider; this.request = request; this.suspended = suspended;
            this.report = report; this.moved = moved;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += OnTick;
        }
        public void Configure(InfoOverlaySettings value)
        {
            value.Validate();
            settings = System.Text.Json.JsonSerializer.Deserialize<InfoOverlaySettings>(System.Text.Json.JsonSerializer.Serialize(value))!;
            generation++;
            if (settings.Enabled) { timer.Start(); _ = PollAsync(); }
            else { timer.Stop(); window?.Hide(); report(UiText.T("信息显示已关闭。")); }
        }
        public void SetAdjusting(bool value) { adjusting = value; window?.SetAdjusting(value); }
        private void OnTick(object? sender, EventArgs e) => _ = PollAsync();
        internal async Task PollAsync()
        {
            if (disposed || busy || !settings.Enabled) return;
            busy = true;
            var session = sessionProvider(); var epoch = generation;
            try
            {
                if (session == null || !session.IsConnected || suspended())
                { window?.Hide(); displayedSession = null; return; }
                if (!ReferenceEquals(displayedSession, session)) { window?.Hide(); displayedSession = session; }
                var owner = ColliderOverlayController.FindMainWindow(session.GameProcessId);
                if (!ColliderOverlayController.TryGetClientBounds(owner, out var bounds, out var dpi))
                { window?.Hide(); return; }
                var response = await request(session, new Dictionary<string, string>
                {
                    ["requestId"] = "studio-info-" + Guid.NewGuid().ToString("N"), ["view"] = "info"
                }, cancellation.Token);
                if (disposed || epoch != generation || !ReferenceEquals(session, sessionProvider()) || !session.IsConnected || suspended())
                { window?.Hide(); return; }
                if (!response.TryGetValue("snapshotJson", out var json)) throw new FormatException("Info snapshot is missing.");
                var values = InfoOverlayModel.Decode(json);
                if (window == null)
                {
                    window = new InfoOverlayWindow();
                    window.PositionChanged += moved;
                }
                // Recheck after asynchronous capture: the window may have moved/minimized meanwhile.
                if (!ColliderOverlayController.TryGetClientBounds(owner, out bounds, out dpi)) { window.Hide(); return; }
                window.SetOwner(owner);
                window.SetBounds(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, dpi);
                window.SetAdjusting(adjusting);
                window.Update(settings, values);
                if (settings.Items.Any(item => item.Enabled) || adjusting) window.ShowOverlay(); else window.Hide();
                report(UiText.T("信息显示已启用。"));
            }
            catch (OperationCanceledException) when (disposed) { }
            catch (Exception error)
            {
                if (!disposed && epoch == generation)
                { window?.Hide(); report(UiText.T("信息读取失败：") + error.Message); }
            }
            finally { busy = false; if (disposed) cancellation.Dispose(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; generation++; timer.Stop(); timer.Tick -= OnTick;
            cancellation.Cancel(); window?.Close(); window = null;
            if (!busy) cancellation.Dispose();
        }
    }
}
