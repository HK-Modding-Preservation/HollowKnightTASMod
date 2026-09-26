using System.Windows.Input;

namespace HollowKnightTAS.Companion.ViewModels
{
    public enum StudioShortcut { None, OpenMovie, SaveMovie, PlayPause, FrameAdvance, Play }

    public static class StudioHotkeys
    {
        public static int QuickSlotIndex(Key key, ModifierKeys modifiers)
        {
            return key >= Key.F1 && key <= Key.F10
                && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)
                ? (int)key - (int)Key.F1 : -1;
        }

        public static StudioShortcut Resolve(Key key, ModifierKeys modifiers, bool editingText,
            Key pauseKey = Key.Pause, Key advanceKey = Key.V, Key playKey = Key.P)
        {
            if (modifiers == ModifierKeys.Control)
                return key == Key.O ? StudioShortcut.OpenMovie : key == Key.S ? StudioShortcut.SaveMovie : StudioShortcut.None;
            if (modifiers != ModifierKeys.None) return StudioShortcut.None;
            if (key == pauseKey && (!editingText || key == Key.Pause)) return StudioShortcut.PlayPause;
            if (key == advanceKey && !editingText) return StudioShortcut.FrameAdvance;
            if (key == playKey && !editingText) return StudioShortcut.Play;
            return StudioShortcut.None;
        }
    }
}
