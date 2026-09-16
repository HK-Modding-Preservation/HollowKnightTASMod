using System.Windows.Input;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioHotkeysTests
    {
        [TestMethod]
        public void CustomTransportKeysRespectTextFocusAndModifiers()
        {
            Assert.AreEqual(StudioShortcut.PlayPause, StudioHotkeys.Resolve(Key.P, ModifierKeys.None, false, Key.P, Key.Space));
            Assert.AreEqual(StudioShortcut.FrameAdvance, StudioHotkeys.Resolve(Key.Space, ModifierKeys.None, false, Key.P, Key.Space));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.P, ModifierKeys.None, true, Key.P, Key.Space));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.Space, ModifierKeys.None, true, Key.P, Key.Space));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.P, ModifierKeys.Control, false, Key.P, Key.Space));
            Assert.AreEqual(StudioShortcut.None, StudioHotkeys.Resolve(Key.V, ModifierKeys.None, false, Key.P, Key.Space));
        }

        [TestMethod]
        public void QuickSlotsUseOnlyF1ToF10WithOptionalShift()
        {
            for (var i = 0; i < 10; i++)
            {
                var key = (Key)((int)Key.F1 + i);
                Assert.AreEqual(i, StudioHotkeys.QuickSlotIndex(key, ModifierKeys.None));
                Assert.AreEqual(i, StudioHotkeys.QuickSlotIndex(key, ModifierKeys.Shift));
                Assert.AreEqual(-1, StudioHotkeys.QuickSlotIndex(key, ModifierKeys.Control));
                Assert.AreEqual(-1, StudioHotkeys.QuickSlotIndex(key, ModifierKeys.Alt | ModifierKeys.Shift));
            }
            Assert.AreEqual(-1, StudioHotkeys.QuickSlotIndex(Key.F11, ModifierKeys.None));
            Assert.AreEqual(-1, StudioHotkeys.QuickSlotIndex(Key.V, ModifierKeys.None));
        }

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
