using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private bool colliderOverlayEnabled;
        private string colliderOverlayStatus = "碰撞箱显示未启用。";
        private ColliderOverlayController? colliderOverlayController;
        private Window? colliderOverlayOwnerWindow;
        private EventHandler? colliderOverlayOwnerClosed;

        // Kept internal so offline Companion tests can use an isolated file.
        internal static string? ColliderOverlaySettingPathOverride { get; set; }

        private static string ColliderOverlaySettingPath => Path.Combine(
            ColliderOverlaySettingPathOverride != null
                ? Path.GetDirectoryName(ColliderOverlaySettingPathOverride) ?? string.Empty
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ColliderOverlaySettingPathOverride != null
                ? Path.GetFileName(ColliderOverlaySettingPathOverride)
                : Path.Combine("HollowKnightTAS", "studio-collider-overlay.txt"));

        public bool ColliderOverlayEnabled
        {
            get => colliderOverlayEnabled;
            set
            {
                if (colliderOverlayEnabled == value) return;
                try
                {
                    var path = ColliderOverlaySettingPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temporary = path + ".new";
                    File.WriteAllText(temporary, value ? "enabled\n" : "disabled\n");
                    File.Move(temporary, path, true);
                    colliderOverlayEnabled = value;
                    colliderOverlayController?.SetEnabled(value);
                    ColliderOverlayStatus = value ? "正在等待游戏画面…" : "碰撞箱显示已关闭。";
                    OnPropertyChanged();
                }
                catch (Exception exception)
                {
                    ColliderOverlayStatus = "碰撞箱设置保存失败：" + exception.Message;
                    OnPropertyChanged();
                }
            }
        }

        public string ColliderOverlayStatus
        {
            get => colliderOverlayStatus;
            private set => Set(ref colliderOverlayStatus, value);
        }

        private void InitializeColliderOverlay()
        {
            try
            {
                var path = ColliderOverlaySettingPath;
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 32)
                        throw new InvalidDataException("碰撞箱设置过大。");
                    var text = File.ReadAllText(path).Trim();
                    if (text != "enabled" && text != "disabled")
                        throw new InvalidDataException("碰撞箱设置无效。");
                    colliderOverlayEnabled = text == "enabled";
                }
            }
            catch (Exception exception)
            {
                colliderOverlayEnabled = false;
                ColliderOverlayStatus = "碰撞箱设置读取失败；本次默认关闭：" + exception.Message;
            }

            colliderOverlayController = new ColliderOverlayController(
                () => SelectedSession?.Client,
                RequestRuntimeAsync,
                message => ColliderOverlayStatus = message);
            AttachColliderOverlayLifecycle();
            if (colliderOverlayEnabled) colliderOverlayController.SetEnabled(true);
        }

        private void AttachColliderOverlayLifecycle()
        {
            var application = Application.Current;
            if (application == null) return;
            application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                if (colliderOverlayController == null
                    || application.MainWindow == null
                    || colliderOverlayOwnerClosed != null) return;
                colliderOverlayOwnerWindow = application.MainWindow;
                colliderOverlayOwnerClosed = (_, _) => DisposeColliderOverlay();
                colliderOverlayOwnerWindow.Closed += colliderOverlayOwnerClosed;
            }));
        }

        private async Task<IReadOnlyDictionary<string, string>> RequestRuntimeAsync(
            RuntimeSessionClient session, IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken)
        {
            if (!fields.TryGetValue("requestId", out var requestId)
                || string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Collider requestId is required.", nameof(fields));
            var completion = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void Received(object? sender, IpcEnvelope envelope)
            {
                if (envelope.MessageType != IpcMessageTypes.WorldSnapshot
                    && envelope.MessageType != IpcMessageTypes.CommandRejected
                    && envelope.MessageType != IpcMessageTypes.Fault) return;
                var payload = IpcPayloadCodec.TryDeserialize(envelope.PayloadUtf8);
                if (!payload.Success || payload.Fields == null
                    || !payload.Fields.TryGetValue("requestId", out var responseId)
                    || !string.Equals(responseId, requestId, StringComparison.Ordinal)) return;
                if (envelope.MessageType == IpcMessageTypes.WorldSnapshot)
                    completion.TrySetResult(payload.Fields);
                else
                    completion.TrySetException(new InvalidOperationException(
                        payload.Fields.TryGetValue("detail", out var detail) ? detail : envelope.MessageType));
            }

            session.EnvelopeReceived += Received;
            try
            {
                await session.SendCommandAsync(IpcMessageTypes.GetWorldSnapshot,
                    fields, cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                return await completion.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("worldSnapshot request timed out.");
            }
            finally
            {
                session.EnvelopeReceived -= Received;
            }
        }

        internal void DisposeColliderOverlay()
        {
            if (colliderOverlayOwnerWindow != null && colliderOverlayOwnerClosed != null)
                colliderOverlayOwnerWindow.Closed -= colliderOverlayOwnerClosed;
            colliderOverlayOwnerWindow = null;
            colliderOverlayOwnerClosed = null;
            colliderOverlayController?.Dispose();
            colliderOverlayController = null;
        }
    }
}
