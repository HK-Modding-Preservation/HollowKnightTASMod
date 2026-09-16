using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HollowKnightTAS.Runtime.Ipc
{
    internal static class WindowsCurrentUserSid
    {
        private const uint TokenQuery = 0x0008;
        private const int TokenUser = 1;
        private const int ErrorInsufficientBuffer = 122;

        public static SecurityIdentifier Read()
        {
            if (!OpenProcessToken(
                    GetCurrentProcess(),
                    TokenQuery,
                    out var token))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "OpenProcessToken failed.");
            }

            try
            {
                GetTokenInformation(
                    token,
                    TokenUser,
                    IntPtr.Zero,
                    0,
                    out var required);
                var firstError =
                    Marshal.GetLastWin32Error();
                if (required <= 0
                    || firstError
                    != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception(
                        firstError,
                        "TokenUser size query failed.");
                }

                var buffer =
                    Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformation(
                            token,
                            TokenUser,
                            buffer,
                            required,
                            out _))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "TokenUser query failed.");
                    }

                    var user =
                        (TokenUserInformation)
                        Marshal.PtrToStructure(
                            buffer,
                            typeof(TokenUserInformation));
                    if (user.User.Sid == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "Current process token has no SID.");
                    }

                    return new SecurityIdentifier(
                        user.User.Sid);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenUserInformation
        {
            public SidAndAttributes User;
        }

        [DllImport(
            "kernel32.dll",
            ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport(
            "kernel32.dll",
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(
            IntPtr handle);

        [DllImport(
            "advapi32.dll",
            ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport(
            "advapi32.dll",
            ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);
    }
}
