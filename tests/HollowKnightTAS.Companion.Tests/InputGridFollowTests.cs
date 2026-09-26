using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Ipc;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class InputGridFollowTests
    {
        [TestMethod]
        public void NativeFaultWithoutFrameAdvanceRefreshesPlaybackControls()
        {
            using var sessions = new SessionRegistry("grid-fault-test");
            using var broker = new AutomationBroker(sessions);
            using var boot = new StartupBootController();
            var vm = new MainViewModel(sessions, new MovieEditorService(),
                new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory),
                broker, startupBoot: boot);
            var gate = boot.BeginV2();
            using var mapping = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".V2State");
            using var view = mapping.CreateViewAccessor();
            boot.Refresh();
            var changes = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PlayPauseLabel)) changes++; };
            view.Write(88, 41);
            view.Write(76, 3);
            boot.Refresh();
            Assert.AreEqual(1, changes, "A fault must notify even when frames and pending state do not change.");
            StringAssert.Contains(vm.PlayPauseLabel, "需重启");
            StringAssert.Contains(vm.Status, "41");
            Assert.IsFalse(vm.TogglePauseCommand.CanExecute(null));
            Assert.IsFalse(vm.StepCommand.CanExecute(null));
        }

        [TestMethod]
        public void FullRunGridFollowsMovieFrameAcrossPagesAndCanBePausedForInspection()
        {
            using var sessions = new SessionRegistry("grid-follow-test");
            using var broker = new AutomationBroker(sessions);
            using var boot = new StartupBootController();
            var vm = new MainViewModel(sessions, new MovieEditorService(),
                new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory),
                broker, startupBoot: boot);
            boot.BeginV2();
            var header = new MovieV2Header("game", "api", "mod", "profile",
                MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450);
            vm.MovieText = new MovieV2Codec().WriteCanonical(new MovieV2Document(
                "grid-follow.hktas", header, new[]
                {
                    new NativeFrameRun(1200, Array.Empty<GameInputSample>(),
                        new MovieSourceSpan("grid-follow.hktas", 1, 1, 1))
                }));
            vm.RefreshGridCommand.Execute(null);
            Assert.AreEqual(1200, vm.InputRows.Count);

            var scrolledTo = -1L;
            vm.InputGridPositionChanged += frame => scrolledTo = frame;
            ReceiveFrame(vm, 620, 740);
            Assert.AreEqual(0L, vm.InputRows[0].Tick);
            Assert.AreEqual("▶", vm.InputRows.Single(row => row.Tick == 620).Current);
            Assert.AreEqual(620L, scrolledTo);
            StringAssert.Contains(vm.FrameCounterText, "Movie frame: 620");

            vm.GridStart = "1000";
            vm.GoToGridFrameCommand.Execute(null);
            Assert.AreEqual(1000L, scrolledTo);
            ReceiveFrame(vm, 620, 740);
            Assert.AreEqual(1000L, scrolledTo, "Unchanged paused frame must not pull a user back from manual navigation.");
            vm.ShowCurrentGridFrame();
            vm.AutoFollowGrid = false;
            ReceiveFrame(vm, 1150, 1300);
            Assert.AreEqual(0L, vm.InputRows[0].Tick);
            Assert.AreEqual(620L, scrolledTo);
            StringAssert.Contains(vm.FrameCounterText, "Movie frame: 1150");
            vm.FollowGridFrameCommand.Execute(null);
            Assert.AreEqual(0L, vm.InputRows[0].Tick);
            Assert.AreEqual("▶", vm.InputRows[1150].Current);
            Assert.AreEqual(1150L, scrolledTo);

            vm.AutoFollowGrid = true;
            ReceiveFrame(vm, 1200, 1350);
            Assert.AreEqual("▶", vm.InputRows.Single(row => row.Tick == 1199).Current);
            Assert.AreEqual(1199L, scrolledTo);
        }

        private static void ReceiveFrame(MainViewModel vm, long movieFrame, long nativeFrame)
        {
            var handle = typeof(MainViewModel).GetMethod("HandleTypedEvent",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            handle.Invoke(vm, new object[]
            {
                IpcMessageTypes.FullRunState,
                new Dictionary<string, string>
                {
                    ["movieFrame"] = movieFrame.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["nativeFrame"] = nativeFrame.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            });
        }
    }
}
