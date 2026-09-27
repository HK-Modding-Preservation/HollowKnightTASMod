using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace HollowKnightTAS.Companion.Services
{
    // Window geometry is independent of Runtime observations. A game's modal move loop
    // can stop its PlayerLoop/IPC replies, while WinEvents and this dispatcher continue.
    internal sealed class InfoOverlayWindowTracker : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly Action<int, int, int, int, uint> place;
        private readonly Action hide;
        private readonly WinEventCallback callback;
        private readonly DispatcherTimer timer;
        private IntPtr owner, locationHook, moveHook;
        private bool visible, disposed;
        public bool IsMoving { get; private set; }

        public InfoOverlayWindowTracker(Dispatcher dispatcher, Action<int, int, int, int, uint> place, Action hide)
        {
            this.dispatcher = dispatcher; this.place = place; this.hide = hide; callback = OnEvent;
            timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += OnTick;
        }
        public void SetOwner(IntPtr value)
        {
            if (disposed || owner == value) return;
            Unhook(); owner = value; IsMoving = false;
            GetWindowThreadProcessId(owner, out var process);
            if (owner != IntPtr.Zero && process != 0)
            {
                locationHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, process, 0, 0);
                moveHook = SetWinEventHook(0xA, 0xB, IntPtr.Zero, callback, process, 0, 0);
            }
            Refresh();
        }
        public void SetVisible(bool value)
        { visible = value; if (value && !disposed) { timer.Start(); Refresh(); } else timer.Stop(); }
        private void OnTick(object? sender, EventArgs args) => Refresh();
        private void OnEvent(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
        {
            if (disposed || hwnd != owner || (type == 0x800B && objectId != 0)) return;
            void Update()
            {
                if (disposed || hwnd != owner) return;
                if (type == 0xA) IsMoving = true;
                if (type == 0xB) IsMoving = false;
                Refresh();
            }
            if (dispatcher.CheckAccess()) Update();
            else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)Update);
        }
        private void Refresh()
        {
            if (disposed || !visible || owner == IntPtr.Zero) return;
            if (!ColliderOverlayController.TryGetClientBounds(owner, out var bounds, out var dpi)) { hide(); return; }
            place(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, dpi);
        }
        private void Unhook()
        {
            if (locationHook != IntPtr.Zero) UnhookWinEvent(locationHook);
            if (moveHook != IntPtr.Zero) UnhookWinEvent(moveHook);
            locationHook = moveHook = IntPtr.Zero;
        }
        public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); timer.Tick -= OnTick; Unhook(); }
        private delegate void WinEventCallback(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
}
