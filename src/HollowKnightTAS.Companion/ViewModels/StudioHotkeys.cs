using System.Windows.Input;

namespace HollowKnightTAS.Companion.ViewModels
{
    public enum StudioShortcut { None, OpenMovie, SaveMovie, PlayPause, FrameAdvance }

    public static class StudioHotkeys
    {
        public static StudioShortcut Resolve(Key key, ModifierKeys modifiers, bool editingText)
        {
            if (modifiers == ModifierKeys.Control)
                return key == Key.O ? StudioShortcut.OpenMovie : key == Key.S ? StudioShortcut.SaveMovie : StudioShortcut.None;
            if (modifiers != ModifierKeys.None) return StudioShortcut.None;
            if (key == Key.Pause) return StudioShortcut.PlayPause;
            if (key == Key.V && !editingText) return StudioShortcut.FrameAdvance;
            return StudioShortcut.None;
        }
    }
}
