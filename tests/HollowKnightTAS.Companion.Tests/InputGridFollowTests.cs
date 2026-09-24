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
            Assert.AreEqual(500, vm.InputRows.Count);

            var scrolledTo = -1L;
            vm.InputGridPositionChanged += frame => scrolledTo = frame;
            ReceiveFrame(vm, 620, 740);
            Assert.AreEqual(540L, vm.InputRows[0].Tick);
            Assert.AreEqual("▶", vm.InputRows.Single(row => row.Tick == 620).Current);
            Assert.AreEqual(620L, scrolledTo);
            StringAssert.Contains(vm.FrameCounterText, "Movie frame: 620");

            vm.AutoFollowGrid = false;
            ReceiveFrame(vm, 1150, 1300);
            Assert.AreEqual(540L, vm.InputRows[0].Tick);
            Assert.AreEqual(620L, scrolledTo);
            StringAssert.Contains(vm.FrameCounterText, "Movie frame: 1150");
            vm.FollowGridFrameCommand.Execute(null);
            Assert.AreEqual(1150L, vm.InputRows[0].Tick);
            Assert.AreEqual("▶", vm.InputRows[0].Current);
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
