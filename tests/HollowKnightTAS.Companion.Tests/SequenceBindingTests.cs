using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class SequenceBindingTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Flags)!.SetValue(owner, value);
    private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Flags)!.GetValue(owner)!;
    private static Task Save(MainViewModel vm, string path) => (Task)typeof(MainViewModel).GetMethod("SaveSequenceToPathAsync", Flags)!.Invoke(vm, new object[] { path })!;
    private static string Movie() => new MovieV2Codec().WriteCanonical(new MovieV2Document("test",
        new MovieV2Header("game", "api", "mod", MovieProtocolV2.NativeProfileId, MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
        new[] { new NativeFrameRun(500, Array.Empty<GameInputSample>(), new MovieSourceSpan("test", 1, 1, 1), 50, true) }));
    private static InitialSaveSnapshot Snapshot(byte value) => new(new Dictionary<string, byte[]> { ["user1.dat"] = new[] { value } });

    [TestMethod] public async Task OpenOfflineSaveAutosaveAndSaveAsPreserveBindingAndFutureDraft()
    {
        using var f = new Fixture();
        var path = Path.Combine(f.Root, "portable.hktaspack");
        var snapshot = Snapshot(3);
        await SequencePackage.WriteAsync(path, Movie(), snapshot);
        await f.Vm.OpenMovieFileAsync(path);
        Assert.IsTrue(Get<bool>(f.Vm, "draftRequiresRestart"), "Opening before launch must not be replaced by automatic New.");
        Assert.AreEqual(snapshot.Id, f.Coordinator.SequenceInitialSaves!.Id);
        Assert.AreEqual(snapshot.Id, f.Vm.SelectedTimelineTree!.InitialSavesId);
        Assert.AreEqual(Movie(), f.Vm.MovieText);
        var saveAs = Path.Combine(f.Root, "copy.hktaspack");
        await Save(f.Vm, saveAs);
        Set(f.Vm, "activeSequenceDirectory", f.Root); Set(f.Vm, "activeAutoSaveSeconds", 1);
        Set(f.Vm, "nextSequenceAutoSave", DateTime.MinValue); Set(f.Vm, "autoSaveName", "bound");
        await f.Vm.AutoSaveSequenceAsync();
        foreach (var saved in new[] { path, saveAs, Path.Combine(f.Root, "Autosave", "sequence-bound.hktaspack") })
        {
            var result = SequencePackage.Read(saved);
            Assert.AreEqual(snapshot.Id, result.InitialSaves!.Id);
            Assert.AreEqual(Movie(), result.Movie);
        }
        var corrupt = Path.Combine(f.Root, "bad.hktaspack"); File.WriteAllText(corrupt, "bad");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Vm.OpenMovieFileAsync(corrupt));
        Assert.AreEqual(snapshot.Id, Get<InitialSaveSnapshot>(f.Vm, "sequenceInitialSaves").Id);
        Assert.AreEqual(saveAs, Get<string>(f.Vm, "sequenceSavePath"));
        var legacy = Path.Combine(f.Root, "old.hktas"); File.WriteAllText(legacy, Movie());
        await f.Vm.OpenMovieFileAsync(legacy);
        Assert.IsNull(f.Coordinator.SequenceInitialSaves);
        StringAssert.Contains(f.Vm.SequenceBindingStatus, "未绑定");
    }

    [TestMethod] public async Task OpeningBoundSequenceAtFrameZeroRequiresFreshShadowEvenWhenUnarmed()
    {
        using var f = new Fixture(); f.SetFrameZero();
        var path = Path.Combine(f.Root, "different.hktaspack");
        await SequencePackage.WriteAsync(path, Movie(), Snapshot(8));
        await f.Vm.OpenMovieFileAsync(path);
        Assert.AreEqual("Unarmed", f.Coordinator.Mode);
        Assert.IsTrue(Get<bool>(f.Vm, "draftRequiresRestart"));
        Assert.Throws<InvalidOperationException>(() => f.Coordinator.ArmReplay(TimelineTree.Parse(Movie())));
        Assert.AreEqual(0L, f.Boot.NativeCompletedFrames);
    }

    [TestMethod] public async Task NewSequenceSavesFrameZeroSnapshotAfterShadowHasChanged()
    {
        using var f = new Fixture(); f.SetFrameZero();
        f.Vm.NewFullRunMovieCommand.Execute(null);
        StringAssert.Contains(f.Vm.Status, "录制已预置");
        var initial = f.Coordinator.SessionInitialSaves!.Id;
        File.WriteAllBytes(Path.Combine(f.Coordinator.ShadowRoot, "user1.dat"), new byte[] { 99 });
        var path = Path.Combine(f.Root, "new.hktaspack");
        await Save(f.Vm, path);
        Assert.AreEqual(initial, SequencePackage.Read(path).InitialSaves!.Id);
        f.Coordinator.VerifyOriginalSavesUnchanged();
        Assert.AreEqual(0L, f.Boot.NativeCompletedFrames);
    }

    [TestMethod] public async Task TimelineSwitchRestoresItsOwnBaselineWithoutDependingOnPackagePath()
    {
        using var f = new Fixture();
        var first = Path.Combine(f.Root, "first.hktaspack"); var second = Path.Combine(f.Root, "second.hktaspack");
        await SequencePackage.WriteAsync(first, Movie(), Snapshot(1));
        await SequencePackage.WriteAsync(second, Movie(), Snapshot(2));
        await f.Vm.OpenMovieFileAsync(first);
        var treeId = f.Vm.SelectedTimelineTreeId;
        await f.Vm.OpenMovieFileAsync(second);
        File.Move(first, first + ".moved");
        var method = typeof(MainViewModel).GetMethod("SwitchWorldlineAsync", Flags)!;
        await (Task)method.Invoke(f.Vm, new object[] { treeId!, 0 })!;
        Assert.AreEqual(Snapshot(1).Id, f.Coordinator.SequenceInitialSaves!.Id);
        Assert.IsTrue(Get<bool>(f.Vm, "draftRequiresRestart"));
        Assert.IsNull(Get<string?>(f.Vm, "sequenceSavePath"));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hktas-binding-" + Guid.NewGuid().ToString("N"));
        private readonly SessionRegistry sessions = new("binding-test");
        public StartupBootController Boot { get; } = new();
        private readonly AutomationBroker broker;
        public FullRunMovieCoordinator Coordinator { get; }
        public MainViewModel Vm { get; }
        private MemoryMappedFile? mapping;
        private MemoryMappedViewAccessor? view;
        private EventWaitHandle? ready;
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Coordinator = new FullRunMovieCoordinator(Boot);
            broker = new AutomationBroker(sessions, fullRunMovies: Coordinator, automationDirectory: Path.Combine(Root, "automation"));
            Vm = new MainViewModel(sessions, new MovieEditorService(), new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory), broker,
                startupBoot: Boot, fullRunMovies: Coordinator);
            Set(Vm, "initialSaveCacheRoot", Path.Combine(Root, "baselines"));
            Set(Vm, "worldlines", new StudioTimelineStore(Path.Combine(Root, "timelines.json")));
        }
        public void SetFrameZero()
        {
            var gate = Boot.BeginV2();
            var original = Path.Combine(Root, "original"); Directory.CreateDirectory(original);
            File.WriteAllBytes(Path.Combine(original, "user1.dat"), new byte[] { 1 });
            var saves = ProtectedSaveSession.Prepare("binding-test", original, Path.Combine(Root, "shadow"));
            gate.SetProtectedSaveSession(saves);
            Set(Coordinator, "gate", gate); Set(Coordinator, "saves", saves);
            Set(Coordinator, "bootstrap", new FullRunBootstrapStore(Path.Combine(Root, "boot")));
            mapping = MemoryMappedFile.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".V2State");
            view = mapping.CreateViewAccessor(); ready = EventWaitHandle.OpenExisting("Local\\HKTAS.Boot." + gate.Token + ".Ready");
            view.Write(92, 1); view.Write(76, 0); view.Write(40, 0L); ready.Set(); Boot.Refresh();
        }
        public void Dispose()
        {
            ready?.Dispose(); view?.Dispose(); mapping?.Dispose(); broker.Dispose(); Boot.Dispose(); sessions.Dispose();
            if (!Path.GetFullPath(Root).StartsWith(Path.Combine(Path.GetTempPath(), "hktas-binding-"), StringComparison.OrdinalIgnoreCase)) throw new Exception();
            foreach (var path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
