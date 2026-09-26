using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class GlobalStudioHotkeysTests
    {
        [TestMethod]
        public void HeldStepHasDelayStopsOnReleaseAndNeverCatchesUpMissedTicks()
        {
            var repeat = new HeldStepRepeat();
            Assert.IsFalse(repeat.Poll(1000, true));
            repeat.Begin(1000);
            Assert.IsFalse(repeat.Poll(1349, true));
            Assert.IsTrue(repeat.Poll(1350, true));
            Assert.IsFalse(repeat.Poll(1350, true));
            Assert.IsTrue(repeat.Poll(50000, true));
            Assert.IsFalse(repeat.Poll(50001, true));
            Assert.IsFalse(repeat.Poll(50040, false));
            Assert.IsFalse(repeat.Poll(60000, true), "A new press must explicitly rearm repetition.");
            repeat.Begin(60000); repeat.Stop();
            Assert.IsFalse(repeat.Poll(61000, true));
        }

        [TestMethod]
        public async Task RepeatedCommandsDoNotQueueBehindPendingStep()
        {
            var pending = new TaskCompletionSource();
            var count = 0;
            var command = new AsyncRelayCommand(async () => { count++; await pending.Task; });
            command.Execute(null);
            for (var i = 0; i < 100; i++) command.Execute(null);
            Assert.AreEqual(1, count);
            Assert.IsFalse(command.CanExecute(null));
            pending.SetResult();
            for (var i = 0; i < 100 && !command.CanExecute(null); i++) await Task.Delay(10);
            Assert.IsTrue(command.CanExecute(null));
            Assert.AreEqual(1, count, "Releasing a pending step must not replay dropped key repeats.");
        }

        [TestMethod]
        public void GlobalRoutingProtectsStudioDialogsAndReleasesRegistrations()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // Hidden message window, fake registration API: no global keys are captured.
                    using var source = new HwndSource(new HwndSourceParameters("hotkey-offline-test")
                        { Width = 0, Height = 0, WindowStyle = 0 });
                    var platform = new FakePlatform();
                    var received = new List<(Key, ModifierKeys)>();
                    var status = "";
                    using var keys = new GlobalStudioHotkeys(source.Handle, () => true,
                        (key, mods) => received.Add((key, mods)), s => status = s, platform);
                    keys.Configure(false, Key.Pause, Key.V);
                    Assert.AreEqual(0, platform.Registered.Count);
                    keys.Configure(true, Key.Pause, Key.V);
                    SendMessage(source.Handle, 0x0312, (IntPtr)0x5A02, IntPtr.Zero);
                    Assert.AreEqual((Key.P, ModifierKeys.None), received[0]);
                    received.Clear();
                    Assert.AreEqual(23, platform.Registered.Count);
                    SendMessage(source.Handle, 0x0312, (IntPtr)0x5A01, IntPtr.Zero);
                    Assert.AreEqual((Key.V, ModifierKeys.None), received[0]);
                    SendMessage(source.Handle, 0x0312, (IntPtr)0x5A04, IntPtr.Zero);
                    Assert.AreEqual((Key.F1, ModifierKeys.Shift), received[1]);
                    platform.ForegroundProcessId = Environment.ProcessId;
                    SendMessage(source.Handle, 0x0312, (IntPtr)0x5A00, IntPtr.Zero);
                    Assert.AreEqual(2, received.Count, "Ignore stale messages after focus returns to Studio.");
                    keys.Refresh();
                    Assert.AreEqual(0, platform.Registered.Count);
                    platform.ForegroundProcessId = -1;
                    platform.Reject = true;
                    keys.Refresh();
                    Assert.IsTrue(status.Contains("注册失败"));
                    Assert.AreEqual(0, platform.Registered.Count);
                    platform.Reject = false;
                    keys.Configure(true, Key.Space, Key.B);
                    Assert.AreEqual(23, platform.Registered.Count);
                    SendMessage(source.Handle, 0x0312, (IntPtr)0x5A01, IntPtr.Zero);
                    Assert.AreEqual((Key.B, ModifierKeys.None), received[2]);
                    keys.Configure(false, Key.Space, Key.B);
                    Assert.AreEqual(0, platform.Registered.Count);
                    keys.Configure(true, Key.Space, Key.B);
                    keys.Dispose();
                    Assert.AreEqual(0, platform.Registered.Count);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
            if (failure != null) throw new AssertFailedException(failure.ToString());
        }

        private sealed class FakePlatform : IGlobalHotkeyPlatform
        {
            public int ForegroundProcessId { get; set; } = -1;
            public bool Reject { get; set; }
            public HashSet<int> Registered { get; } = new();
            public bool IsDown(int key) => false;
            public bool Register(IntPtr hwnd, int id, uint modifiers, uint key)
            { Assert.AreEqual(0x4000u, modifiers & 0x4000u); return !Reject && Registered.Add(id); }
            public void Unregister(IntPtr hwnd, int id) => Registered.Remove(id);
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}
