using System;
using System.Runtime.InteropServices;

namespace HollowKnightTAS.Runtime.Ipc
{
    internal static class PausedWindowMessagePump
    {
        // QS_SENDMESSAGE << 16, with PM_NOREMOVE (zero). Do not add
        // PM_QS_INPUT, PM_QS_POSTMESSAGE, PM_QS_PAINT or PM_REMOVE here.
        private const uint SentMessagesOnlyWithoutRemoval = 0x00400000;

        internal static void ServiceSentMessages()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return;

            // PeekMessage services incoming nonqueued SendMessage requests
            // (including window responsiveness/activation) on this thread.
            // Leave queued keyboard, mouse, timer and paint messages for the
            // native Unity loop. Never TranslateMessage/DispatchMessage or
            // invoke Unity's input/update loop from the paused boundary.
            PeekMessage(out _, IntPtr.Zero, 0, 0, SentMessagesOnlyWithoutRemoval);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Window;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PointX;
            public int PointY;
            public uint Private;
        }

        [DllImport("user32.dll", EntryPoint = "PeekMessageW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out NativeMessage message,
            IntPtr window, uint minimum, uint maximum, uint flags);
    }
}
