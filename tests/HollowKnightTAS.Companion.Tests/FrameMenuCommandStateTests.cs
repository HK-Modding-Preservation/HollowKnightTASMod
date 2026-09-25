using System;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class FrameMenuCommandStateTests
    {
        [TestMethod]
        public async Task NativePauseDuringSeekPublishesEnabledControlsAfterOperationEnds()
        {
            using var sessions = new SessionRegistry("seek-controls-test");
            using var boot = new StartupBootController();
            var coordinator = new FullRunMovieCoordinator(boot);
            using var broker = new AutomationBroker(sessions, fullRunMovies: coordinator);
            var vm = new MainViewModel(sessions, new MovieEditorService(),
                new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory),
                broker, startupBoot: boot, fullRunMovies: coordinator);
            var gate = boot.BeginV2();
            typeof(FullRunMovieCoordinator).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, gate);
            typeof(FullRunMovieCoordinator).GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, "Replay");
            using var mapping = MemoryMappedFile.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".V2State");
            using var view = mapping.CreateViewAccessor();
            using var ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
            view.Write(92, 1);
            view.Write(76, 2); // Running during seek.
            boot.Refresh();
            var setBusy = typeof(MainViewModel).GetMethod("SetGridApplying", BindingFlags.Instance | BindingFlags.NonPublic)!;
            setBusy.Invoke(vm, new object[] { true });
            bool publishedPlay = false, publishedStep = false;
            string publishedLabel = vm.PlayPauseLabel;
            vm.TogglePauseCommand.CanExecuteChanged += (_, _) => publishedPlay = vm.TogglePauseCommand.CanExecute(null);
            vm.StepCommand.CanExecuteChanged += (_, _) => publishedStep = vm.StepCommand.CanExecute(null);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PlayPauseLabel)) publishedLabel = vm.PlayPauseLabel; };

            view.Write(40, 66L);
            view.Write(76, 0); // Runtime automatically pauses at the target.
            ready.Set();
            boot.Refresh();
            Assert.IsFalse(publishedPlay);
            Assert.IsFalse(publishedStep);
            setBusy.Invoke(vm, new object[] { false });
            Assert.IsTrue(publishedPlay, "Bindings must receive enabled state after seek releases the UI.");
            Assert.IsTrue(publishedStep);
            Assert.AreEqual("Play 继续", publishedLabel);

            // The same finally path must also recover controls on seek validation errors.
            await vm.FrameMenuAsync("seek", -1);
            StringAssert.Contains(vm.GridStatus, "目标帧超出序列");
            Assert.IsTrue(publishedPlay);
            Assert.IsTrue(publishedStep);
            Assert.AreEqual("Play 继续", publishedLabel);
        }
    }
}
