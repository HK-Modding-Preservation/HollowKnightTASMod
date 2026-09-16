using System.Windows.Input;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioHotkeysTests
    {
        [TestMethod]
        public void LibTasTransportKeysUseExpectedCommands()
        {
            Assert.AreEqual(StudioShortcut.FrameAdvance, StudioHotkeys.Resolve(Key.V, ModifierKeys.None, false));
            Assert.AreEqual(StudioShortcut.PlayPause, StudioHotkeys.Resolve(Key.Pause, ModifierKeys.None, false));
            Assert.AreEqual(StudioShortcut.PlayPause, StudioHotkeys.Resolve(Key.Pause, ModifierKeys.None, true));
        }

        [TestMethod]
        public void TextEditingAndModifiedLettersNeverAdvanceFrames()
        {
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.V, ModifierKeys.None, true));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.V, ModifierKeys.Control, true));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.V, ModifierKeys.Shift, false));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.V, ModifierKeys.Alt, false));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.Tab, ModifierKeys.None, false));
        }

        [TestMethod]
        public void FileShortcutsRemainAvailableWhileEditing()
        {
            Assert.AreEqual(StudioShortcut.OpenMovie, StudioHotkeys.Resolve(Key.O, ModifierKeys.Control, true));
            Assert.AreEqual(StudioShortcut.SaveMovie, StudioHotkeys.Resolve(Key.S, ModifierKeys.Control, true));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.S, ModifierKeys.None, true));
        }
    }
}
