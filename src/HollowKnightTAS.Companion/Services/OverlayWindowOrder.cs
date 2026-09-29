using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HollowKnightTAS.Companion.Services;

internal static class OverlayWindowOrder
{
    internal static void Show(System.Windows.Window window, IntPtr owner)
    {
        if (window.IsVisible) return;
        // WPF/USER32 showing an owned window can raise its owner even without activation.
        // Show detached, then restore ownership and position only the overlay.
        var interop = new WindowInteropHelper(window);
        interop.Owner = IntPtr.Zero;
        try { window.Show(); }
        finally { interop.Owner = owner; }
    }
    internal static void FollowOwner(IntPtr overlay, IntPtr owner)
    {
        if (overlay == IntPtr.Zero || owner == IntPtr.Zero || !IsWindow(owner)) return;
        // Insert immediately above the game, below whatever is already above it.
        // WPF Show() may raise the overlay even with ShowActivated=false.
        // Never raise the owner along with its owned overlay.
        var previous = GetWindow(owner, 3); // GW_HWNDPREV; zero means HWND_TOP.
        if (previous == overlay) return;
        SetWindowPos(overlay, previous, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
}
