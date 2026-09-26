using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests;

[TestClass]
public sealed class SequenceSavingTests
{
    private static void Field(MainViewModel vm, string name, object value) => typeof(MainViewModel)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    [TestMethod]
    public async Task AutosaveKeepsFullDraftSeparateSkipsUnchangedAndHonorsDisable()
    {
        var root = Path.Combine(Path.GetTempPath(), "hktas-sequence-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Field(vm, "activeSequenceDirectory", root);
            Field(vm, "activeAutoSaveSeconds", 1);
            Field(vm, "autoSaveName", "test");
            Field(vm, "movieText", "complete draft including future input 中文");
            var original = Path.Combine(root, "manual.hktas");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(original, "manual original");
            Field(vm, "sequenceSavePath", original);
            await vm.AutoSaveSequenceAsync();
            var saved = Path.Combine(root, "Autosave", "sequence-test.hktas");
            Assert.AreEqual("complete draft including future input 中文", await File.ReadAllTextAsync(saved));
            Assert.AreEqual("manual original", await File.ReadAllTextAsync(original));
            File.SetLastWriteTimeUtc(saved, DateTime.UnixEpoch);
            Field(vm, "nextSequenceAutoSave", DateTime.MinValue);
            await vm.AutoSaveSequenceAsync();
            Assert.AreEqual(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(saved));
            Field(vm, "movieText", "new draft");
            Field(vm, "nextSequenceAutoSave", DateTime.MinValue);
            Field(vm, "activeAutoSaveSeconds", 0);
            await vm.AutoSaveSequenceAsync();
            Assert.AreEqual(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(saved));
            Field(vm, "activeAutoSaveSeconds", 1);
            await vm.AutoSaveSequenceAsync();
            Assert.AreEqual("new draft", await File.ReadAllTextAsync(saved));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SavedSettingsCanBeDeserializedAfterRestart()
    {
        var type = typeof(MainViewModel).GetNestedType("SequenceSettings", BindingFlags.NonPublic)!;
        var settings = System.Text.Json.JsonSerializer.Deserialize("{\"Directory\":\"C:/Sequences\",\"Seconds\":120}", type)!;
        Assert.AreEqual("C:/Sequences", type.GetProperty("Directory")!.GetValue(settings));
        Assert.AreEqual(120, type.GetProperty("Seconds")!.GetValue(settings));
    }

    [TestMethod]
    public async Task AutosaveFailureIsReportedAndCanRetry()
    {
        var root = Path.GetTempFileName();
        try
        {
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            Field(vm, "activeSequenceDirectory", root);
            Field(vm, "activeAutoSaveSeconds", 1);
            Field(vm, "movieText", "draft");
            await vm.AutoSaveSequenceAsync();
            StringAssert.Contains(vm.SequenceSaveStatus, "失败");
            Assert.IsFalse((bool)typeof(MainViewModel).GetField("sequenceSaving", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!);
            Assert.AreEqual("", await File.ReadAllTextAsync(root));
        }
        finally { File.Delete(root); }
    }
}
