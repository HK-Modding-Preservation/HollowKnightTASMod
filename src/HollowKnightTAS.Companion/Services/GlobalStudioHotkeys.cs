using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace HollowKnightTAS.Companion.Services
{
    // Registers transport/save keys only. Editor shortcuts must never steal text input.
    public sealed class GlobalStudioHotkeys : IDisposable
    {
        private readonly IGlobalHotkeyPlatform platform;
        private readonly IntPtr handle;
        private readonly HwndSource source;
        private readonly Action<Key, ModifierKeys> execute;
        private readonly Action<string> report;
        private readonly Func<bool> canUse;
        private readonly Dictionary<int, (Key Key, ModifierKeys Modifiers)> registered = new();
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly HeldStepRepeat repeat = new();
        private Key pause, step;
        private bool enabled, attempted, disposed;

        public GlobalStudioHotkeys(IntPtr handle, Func<bool> canUse,
            Action<Key, ModifierKeys> execute, Action<string> report, IGlobalHotkeyPlatform? platform = null)
        {
            this.platform = platform ?? new WindowsPlatform();
            this.handle = handle; this.canUse = canUse; this.execute = execute; this.report = report;
            source = HwndSource.FromHwnd(handle)!;
            source.AddHook(Hook);
            timer.Tick += (_, _) => Tick();
        }

        public void Configure(bool enabled, Key pause, Key step)
        {
            var wasEnabled = this.enabled;
            Release();
            this.enabled = enabled; this.pause = pause; this.step = step;
            if (enabled || wasEnabled) report("");
            if (enabled) { timer.Start(); Tick(); } else timer.Stop();
        }

        public void Refresh() { if (!disposed) Tick(); }

        private void Tick()
        {
            // Native file dialogs belong to Studio too. Do not capture typing in them.
            var pid = platform.ForegroundProcessId;
            var active = enabled && pid != Environment.ProcessId && canUse();
            if (!active) { Release(); return; }
            if (!attempted) Register();
            if (repeat.Poll(Environment.TickCount64,
                Down(KeyInterop.VirtualKeyFromKey(step)) && Modifiers() == ModifierKeys.None))
                execute(step, ModifierKeys.None);
        }

        private void Register()
        {
            attempted = true;
            var keys = new List<(Key, ModifierKeys)> { (pause, ModifierKeys.None), (step, ModifierKeys.None) };
            for (var i = 0; i < 10; i++)
            {
                keys.Add((Key.F1 + i, ModifierKeys.None));
                keys.Add((Key.F1 + i, ModifierKeys.Shift));
            }
            var conflicts = new List<string>();
            for (var i = 0; i < keys.Count; i++)
            {
                var (key, modifiers) = keys[i];
                var id = 0x5A00 + i;
                uint flags = 0x4000 | (modifiers == ModifierKeys.Shift ? 4u : 0u); // MOD_NOREPEAT
                if (platform.Register(handle, id, flags, (uint)KeyInterop.VirtualKeyFromKey(key)))
                    registered[id] = (key, modifiers);
                else conflicts.Add(modifiers == ModifierKeys.Shift ? "Shift+" + key : key.ToString());
            }
            report(conflicts.Count == 0 ? "" : "全局热键注册失败或被占用：" + string.Join(", ", conflicts));
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x0312 || !registered.TryGetValue(wParam.ToInt32(), out var binding)) return IntPtr.Zero;
            handled = true;
            var pid = platform.ForegroundProcessId;
            if (!enabled || pid == Environment.ProcessId || !canUse()) return IntPtr.Zero;
            execute(binding.Key, binding.Modifiers);
            if (binding.Key == step && binding.Modifiers == ModifierKeys.None) repeat.Begin(Environment.TickCount64);
            return IntPtr.Zero;
        }

        private void Release()
        {
            repeat.Stop();
            foreach (var id in registered.Keys) platform.Unregister(handle, id);
            registered.Clear(); attempted = false;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; enabled = false; timer.Stop(); Release(); source.RemoveHook(Hook);
        }
        private bool Down(int key) => platform.IsDown(key);
        private ModifierKeys Modifiers() => (Down(0x10) ? ModifierKeys.Shift : 0)
            | (Down(0x11) ? ModifierKeys.Control : 0) | (Down(0x12) ? ModifierKeys.Alt : 0)
            | (Down(0x5B) || Down(0x5C) ? ModifierKeys.Windows : 0);
        private sealed class WindowsPlatform : IGlobalHotkeyPlatform
        {
            public int ForegroundProcessId { get { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid; } }
            public bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
            public bool Register(IntPtr hwnd, int id, uint modifiers, uint key) => RegisterHotKey(hwnd, id, modifiers, key);
            public void Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    }

    public interface IGlobalHotkeyPlatform
    {
        int ForegroundProcessId { get; }
        bool IsDown(int key);
        bool Register(IntPtr hwnd, int id, uint modifiers, uint key);
        void Unregister(IntPtr hwnd, int id);
    }

    public sealed class HeldStepRepeat
    {
        private bool held;
        private long next;
        public void Begin(long now) { held = true; next = now + 350; }
        public void Stop() => held = false;
        public bool Poll(long now, bool keyDown)
        {
            if (!keyDown) Stop();
            if (!held || now < next) return false;
            next = now + 40; // Never catch up missed ticks or queue past repeats.
            return true;
        }
    }
}
