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
    public sealed class QuitGameCommandTests
    {
        [TestMethod]
        public void QuitRemainsAvailableAtFrameZeroAndAfterFullRunCompletion()
        {
            using var sessions = new SessionRegistry("quit-command-test");
            using var boot = new StartupBootController();
            var coordinator = new FullRunMovieCoordinator(boot);
            using var broker = new AutomationBroker(sessions, fullRunMovies: coordinator);
            var vm = new MainViewModel(sessions, new MovieEditorService(),
                new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory),
                broker, startupBoot: boot, fullRunMovies: coordinator);
            Assert.IsFalse(vm.QuitGameCommand.CanExecute(null));

            var gate = boot.BeginV2();
            typeof(FullRunMovieCoordinator).GetField("gate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, gate);
            Assert.AreEqual("Unarmed", coordinator.Mode);
            Assert.IsTrue(vm.QuitGameCommand.CanExecute(null));

            typeof(FullRunMovieCoordinator).GetField("mode",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, "Replay");
            using var state = MemoryMappedFile.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".V2State");
            using var view = state.CreateViewAccessor();
            view.Write(76, 4); // Native gate's finished mode.
            Assert.AreEqual("Completed", coordinator.Mode);
            Assert.IsFalse(coordinator.IsArmed);
            Assert.IsTrue(vm.QuitGameCommand.CanExecute(null));
        }

        [TestMethod]
        public async Task QuitAtUnarmedFrameZeroUsesTheOwnedGameExitPath()
        {
            using var sessions = new SessionRegistry("quit-frame-zero-test");
            using var boot = new StartupBootController();
            var coordinator = new FullRunMovieCoordinator(boot);
            using var broker = new AutomationBroker(sessions, fullRunMovies: coordinator);
            var gate = boot.BeginV2();
            typeof(FullRunMovieCoordinator).GetField("gate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, gate);
            using var state = MemoryMappedFile.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".V2State");
            using var view = state.CreateViewAccessor();
            view.Write(92, 1); // Protected save guard is armed.
            using var ready = EventWaitHandle.OpenExisting(
                "Local\\HKTAS.Boot." + gate.Token + ".Ready");
            ready.Set();
            boot.Refresh();

            var exits = 0;
            var vm = new MainViewModel(sessions, new MovieEditorService(),
                new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory),
                broker, startupBoot: boot, fullRunMovies: coordinator,
                exitProtectedGameProcess: () => exits++);
            Assert.IsTrue(vm.QuitGameCommand.CanExecute(null));
            var quit = typeof(MainViewModel).GetMethod("QuitGameAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)quit.Invoke(vm, null)!;
            Assert.AreEqual(1, exits);

            view.Write(88, 7); // Native gate fault: graceful Runtime IPC is unavailable.
            boot.Refresh();
            await (Task)quit.Invoke(vm, null)!;
            Assert.AreEqual(2, exits);
        }
    }
}
