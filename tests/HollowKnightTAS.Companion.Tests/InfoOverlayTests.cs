using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class InfoOverlayTests
    {
        private static System.Collections.Generic.IReadOnlyDictionary<string, JsonElement> Values(string values)
            => InfoOverlayModel.Decode("{\"schemaVersion\":1,\"values\":" + values + "}");

        [TestMethod]
        public void DefaultsExposeEightUsefulRowsWithoutQuickSlotConflict()
        {
            var settings = InfoOverlaySettings.Defaults(); settings.Validate();
            Assert.IsTrue(settings.Enabled); Assert.AreEqual(8, settings.Items.Count);
            CollectionAssert.AreEqual(new[] { "frame", "room", "position", "velocity", "dash", "shade", "healthPair", "soul" }, settings.Items.Select(x => x.Field).ToArray());
            Assert.AreEqual("F11", settings.Hotkey);
            Assert.AreEqual("右上", settings.Anchor);
            Assert.AreEqual(8d, settings.MarginX); Assert.AreEqual(8d, settings.MarginY);
        }
        [TestMethod]
        public void PausedValuesDoNotChangeAndNewSnapshotsReplaceRatherThanMerge()
        {
            var row = InfoOverlayModel.NewItem("position");
            var playing = Values("{\"x\":24.3819,\"y\":8.15}");
            Assert.AreEqual("X 24.382   Y 8.150", InfoOverlayModel.Format(row, playing));
            Assert.AreEqual("X 24.382   Y 8.150", InfoOverlayModel.Format(row, playing));
            Assert.AreEqual("—", InfoOverlayModel.Format(row, Values("{\"room\":\"Menu_Title\"}")));
            Assert.AreEqual("X 5.000   Y 6.000", InfoOverlayModel.Format(row, Values("{\"x\":5,\"y\":6}")));
        }
        [TestMethod]
        public void CooldownsClampDisplayOnlyAndReadyIsConfigurable()
        {
            var row = InfoOverlayModel.NewItem("dash"); row.ReadyAtZero = false;
            var values = Values("{\"dash\":-0.001}");
            Assert.AreEqual("0.00 s", InfoOverlayModel.Format(row, values));
            Assert.AreEqual(-.001, values["dash"].GetDouble());
            row.ReadyAtZero = true;
            Assert.AreEqual(UiText.T("就绪"), InfoOverlayModel.Format(row, values));
            Assert.AreEqual("—", InfoOverlayModel.Format(row, Values("{\"dash\":null}")));
            row.Precision = 3; row.Unit = "sec";
            Assert.AreEqual("0.820 sec", InfoOverlayModel.Format(row, Values("{\"dash\":0.82}")));
        }
        [TestMethod]
        public void WrongTypesAndMissingHeroDoNotLookReady()
        {
            foreach (var payload in new[] { "{}", "{\"dash\":\"bad\"}", "{\"dash\":false}" })
                Assert.AreEqual("—", InfoOverlayModel.Format(InfoOverlayModel.NewItem("dash"), Values(payload)));
            Assert.AreEqual("—", InfoOverlayModel.Format(InfoOverlayModel.NewItem("healthPair"), Values("{\"health\":5}")));
        }
        [TestMethod]
        public void SettingsRoundTripPreservesOrderHiddenRowsCustomFormattingAndEmptyList()
        {
            var path = Path.Combine(Path.GetTempPath(), "hktas-info-" + Guid.NewGuid().ToString("N"), "settings.json");
            try
            {
                var settings = InfoOverlaySettings.Defaults();
                settings.Items.Move(0, 3); settings.Items[0].Enabled = false; settings.Items[1].Label = "Knight";
                settings.Items[1].Color = "#FFAABB"; settings.Items[1].Precision = 5;
                settings.Anchor = "右下"; settings.MarginX = 28; settings.Hotkey = "F12";
                settings.Save(path); var loaded = InfoOverlaySettings.Load(path);
                Assert.AreEqual(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(loaded));
                loaded.Items.Clear(); loaded.Save(path); Assert.AreEqual(0, InfoOverlaySettings.Load(path).Items.Count);
            }
            finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
        }
        [TestMethod]
        public void InvalidEditsCannotReplaceLastGoodFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "hktas-info-" + Guid.NewGuid().ToString("N"), "settings.json");
            try
            {
                var settings = InfoOverlaySettings.Defaults(); settings.Save(path); var original = File.ReadAllText(path);
                settings.Items[0].Color = "red";
                Assert.ThrowsExactly<InvalidDataException>(() => settings.Save(path));
                Assert.AreEqual(original, File.ReadAllText(path));
                settings.Items[0].Color = ""; settings.MarginX = double.NaN;
                Assert.ThrowsExactly<InvalidDataException>(() => settings.Validate());
            }
            finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
        }

        [TestMethod]
        public void CustomWatchesFormatEachScalarTypeAndKeepIndependentExpressions()
        {
            var row = InfoOverlayModel.NewItem("custom"); row.Expression = "hero.cooldown"; row.Precision = 2;
            Assert.AreEqual("0.82", InfoOverlayModel.Format(row, Values("{\"watch:hero.cooldown\":0.82}")));
            row.Expression = "hero.flag";
            Assert.AreEqual(UiText.T("是"), InfoOverlayModel.Format(row, Values("{\"watch:hero.flag\":true}")));
            row.Expression = "game.state";
            Assert.AreEqual("PLAYING", InfoOverlayModel.Format(row, Values("{\"watch:game.state\":\"PLAYING\"}")));
            Assert.AreEqual("—", InfoOverlayModel.Format(row, Values("{\"watch:hero.flag\":true}")));
        }
        [TestMethod]
        public void MigrationMovesOnlyOldDefaultPositionAndPreservesUserRows()
        {
            var path = Path.GetTempFileName();
            try
            {
                var settings = InfoOverlaySettings.Defaults(); settings.Version = 1;
                settings.Anchor = "左上"; settings.MarginX = 16; settings.MarginY = 160;
                settings.Items.Add(InfoOverlayModel.NewItem("facingRight"));
                File.WriteAllText(path, JsonSerializer.Serialize(settings));
                var migrated = InfoOverlaySettings.Load(path);
                Assert.AreEqual(2, migrated.Version); Assert.AreEqual("右上", migrated.Anchor);
                Assert.AreEqual(8d, migrated.MarginY); Assert.AreEqual(9, migrated.Items.Count);
                settings.MarginX = 42;
                File.WriteAllText(path, JsonSerializer.Serialize(settings));
                var customized = InfoOverlaySettings.Load(path);
                Assert.AreEqual("左上", customized.Anchor); Assert.AreEqual(42d, customized.MarginX);
            }
            finally { File.Delete(path); }
        }
    }
}
