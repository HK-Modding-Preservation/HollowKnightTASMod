using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class UiTextTests
    {
        [TestMethod]
        public void LanguagePersistsAndComposedStatusKeepsItsData()
        {
            var path = Path.Combine(Path.GetTempPath(), "hktas-language-" + Guid.NewGuid(), "language.txt");
            try
            {
                var text = new UiText(path);
                Assert.AreEqual("设置", text.Translate("Setting"));
                text.LanguageIndex = 1;
                Assert.AreEqual(1, new UiText(path).LanguageIndex);
                Assert.AreEqual("12 frames. Selected input frames copied.", text.Translate("共 12 帧。已复制所选输入帧。"));
                Assert.AreEqual("F2 · Empty sequence (not saved) · Start · Frame 0 · Branch 3",
                    text.Translate("F2 · 当前空序列（未存档） · 起点 · Frame 0 · 世界线 3"));
                Assert.AreEqual("Play to frame 346 and pause", text.Translate("播放到第 346 帧并暂停"));
                const string filename = @"C:\用户\我的序列.hktas";
                Assert.AreEqual(filename, text.Translate(filename));
                text.LanguageIndex = 0;
                Assert.AreEqual("原生帧已暂停 · 回放中", text.Translate("Native frame paused · Replay"));
                Assert.AreEqual(0, new UiText(path).LanguageIndex);
                File.WriteAllText(path, "invalid");
                Assert.AreEqual(0, new UiText(path).LanguageIndex);
            }
            finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
        }

        [TestMethod]
        public void FailedLanguageSaveKeepsPreviousSelectionAndNotifies()
        {
            var parent = Path.Combine(Path.GetTempPath(), "hktas-language-" + Guid.NewGuid());
            File.WriteAllText(parent, "block directory creation");
            try
            {
                var text = new UiText(Path.Combine(parent, "language.txt"));
                var notified = false;
                text.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(UiText.SettingsError);
                text.LanguageIndex = 1;
                Assert.AreEqual(0, text.LanguageIndex);
                Assert.IsTrue(notified);
                StringAssert.StartsWith(text.SettingsError, "语言设置保存失败：");
                Assert.AreEqual("block directory creation", File.ReadAllText(parent));
            }
            finally { File.Delete(parent); }
        }

        [TestMethod]
        public void CatalogHasOneLanguageAndPreservesTemplateArguments()
        {
            using var stream = typeof(UiText).Assembly.GetManifestResourceStream("HollowKnightTAS.Companion.UiText.json")!;
            var entries = JsonSerializer.Deserialize<UiText.Entry[]>(stream)!;
            Assert.AreEqual(entries.Length, entries.Select(e => e.Key).Distinct().Count());
            Assert.AreEqual(entries.Length, entries.Select(e => e.Source).Distinct().Count());
            foreach (var entry in entries)
            {
                Assert.IsFalse(Regex.IsMatch(entry.En, "[\u4e00-\u9fff]"), entry.Source);
                string[] Arguments(string s) => Regex.Matches(s, @"\{\d\}").Select(m => m.Value).Order().ToArray();
                CollectionAssert.AreEqual(Arguments(entry.Source), Arguments(entry.En), entry.Source);
                CollectionAssert.AreEqual(Arguments(entry.Source), Arguments(entry.Zh), entry.Source);
            }
        }
    }
}
