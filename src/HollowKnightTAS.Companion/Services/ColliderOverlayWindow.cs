using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class ColliderOverlayWindow : Window
    {
        private readonly ColliderOverlaySurface surface = new();
        private IntPtr handle;
        private IntPtr gameOwner;
        private int nativeLeft, nativeTop, nativeWidth, nativeHeight;
        private bool hasNativeBounds;

        public ColliderOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            Focusable = false;
            Topmost = false;
            Content = surface;
            SourceInitialized += (_, _) => ConfigureNativeWindow();
        }

        public void SetSnapshot(ColliderOverlaySnapshot? snapshot)
        {
            surface.Snapshot = snapshot;
            surface.InvalidateVisual();
        }

        public void SetBounds(int left, int top, int width, int height, uint dpi)
        {
            nativeLeft = left;
            nativeTop = top;
            nativeWidth = width;
            nativeHeight = height;
            hasNativeBounds = true;
            var scale = dpi == 0 ? 1d : 96d / dpi;
            Left = left * scale;
            Top = top * scale;
            Width = width * scale;
            Height = height * scale;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, IntPtr.Zero, nativeLeft, nativeTop,
                    nativeWidth, nativeHeight, SwpNoActivate | SwpNoZOrder | SwpNoOwnerZOrder);
        }

        public void SetOwner(IntPtr owner)
        {
            if (owner == IntPtr.Zero || owner == gameOwner) return;
            if (handle == IntPtr.Zero) handle = new WindowInteropHelper(this).EnsureHandle();
            new WindowInteropHelper(this).Owner = owner;
            gameOwner = owner;
        }

        public void ShowOverlay()
        {
            OverlayWindowOrder.Show(this, gameOwner);
            if (handle == IntPtr.Zero)
                handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, IntPtr.Zero,
                    hasNativeBounds ? nativeLeft : 0,
                    hasNativeBounds ? nativeTop : 0,
                    hasNativeBounds ? nativeWidth : 0,
                    hasNativeBounds ? nativeHeight : 0,
                    SwpNoActivate | SwpNoZOrder | SwpNoOwnerZOrder | (hasNativeBounds ? 0u : SwpNoMove | SwpNoSize) | SwpShowWindow);
            OverlayWindowOrder.FollowOwner(handle, gameOwner);
        }

        private void ConfigureNativeWindow()
        {
            handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, ExStyleIndex).ToInt64();
            SetWindowLongPtr(handle, ExStyleIndex, new IntPtr(style
                | WsExTransparent | WsExNoActivate | WsExToolWindow));
        }

        private sealed class ColliderOverlaySurface : FrameworkElement
        {
            public ColliderOverlaySurface() => ClipToBounds = true;
            public ColliderOverlaySnapshot? Snapshot { get; set; }

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                var snapshot = Snapshot;
                if (snapshot == null || ActualWidth <= 0 || ActualHeight <= 0) return;
                foreach (var item in snapshot.Objects)
                {
                    var brush = new SolidColorBrush(ColliderOverlayColors.For(item.Classification));
                    brush.Freeze();
                    var pen = new Pen(brush, 1.5);
                    pen.Freeze();
                    foreach (var path in item.Paths)
                    {
                        if (path.Points.Count < 2) continue;
                        var geometry = new StreamGeometry();
                        using (var context = geometry.Open())
                        {
                            var first = path.Points[0];
                            context.BeginFigure(new Point(first.X * ActualWidth,
                                first.Y * ActualHeight), false, path.Closed);
                            for (var index = 1; index < path.Points.Count; index++)
                            {
                                var point = path.Points[index];
                                context.LineTo(new Point(point.X * ActualWidth,
                                    point.Y * ActualHeight), true, false);
                            }
                        }
                        geometry.Freeze();
                        drawingContext.DrawGeometry(null, pen, geometry);
                    }
                }
            }
        }

        private const int ExStyleIndex = -20;
        private const long WsExTransparent = 0x20;
        private const long WsExNoActivate = 0x08000000;
        private const long WsExToolWindow = 0x80;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y,
            int width, int height, uint flags);
    }
}
