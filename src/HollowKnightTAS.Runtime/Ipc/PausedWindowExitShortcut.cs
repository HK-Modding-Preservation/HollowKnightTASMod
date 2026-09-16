using System;
using System.Runtime.InteropServices;

namespace HollowKnightTAS.Runtime.Ipc
{
    // Only the completed-frame pause pump calls this. It does not dispatch
    // window messages, update Unity input, or grant a gameplay frame.
    internal static class PausedWindowExitShortcut
    {
        internal static bool IsRequested()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return false;

            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return false;
            GetWindowThreadProcessId(window, out var owner);
            var current = GetCurrentProcessId();
            if (owner == 0 || owner != current)
                return false;

            // Use only the high bit (currently held). The low "pressed since"
            // bit is shared and unreliable; never consume it as an input edge.
            var alt = GetAsyncKeyState(0x12);
            var f4 = GetAsyncKeyState(0x73);
            return Matches(owner, current, alt, f4,
                window == GetForegroundWindow());
        }

        internal static bool Matches(uint owner, uint current, short alt,
            short f4, bool foregroundUnchanged)
        {
            return owner != 0 && owner == current && foregroundUnchanged
                && alt < 0 && f4 < 0;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
    }
}
