using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class InfoOverlayWindow : Window
    {
        private readonly System.Windows.Controls.Canvas surface = new();
        private readonly System.Windows.Controls.Border panel = new() { Padding = new Thickness(10), CornerRadius = new CornerRadius(4) };
        private InfoOverlaySettings settings = InfoOverlaySettings.Defaults();
        private bool adjusting;
        private Point? dragStart;
        private Point panelStart;
        public event Action<double, double>? PositionChanged;
        private IntPtr handle;
        private int nativeLeft, nativeTop, nativeWidth, nativeHeight;
        private bool hasNativeBounds;

        public InfoOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = true;
            Focusable = false;
            Topmost = false;
            Content = surface;
            surface.ClipToBounds = true;
            surface.Children.Add(panel);
            SizeChanged += (_, _) => PlacePanel();
            panel.MouseLeftButtonDown += (_, e) =>
            {
                if (!adjusting) return;
                dragStart = e.GetPosition(surface);
                panelStart = new Point(System.Windows.Controls.Canvas.GetLeft(panel), System.Windows.Controls.Canvas.GetTop(panel));
                panel.CaptureMouse(); e.Handled = true;
            };
            panel.MouseMove += (_, e) =>
            {
                if (dragStart is not Point start) return;
                var now = e.GetPosition(surface);
                System.Windows.Controls.Canvas.SetLeft(panel, Math.Clamp(panelStart.X + now.X - start.X, 0, Math.Max(0, surface.ActualWidth - panel.ActualWidth)));
                System.Windows.Controls.Canvas.SetTop(panel, Math.Clamp(panelStart.Y + now.Y - start.Y, 0, Math.Max(0, surface.ActualHeight - panel.ActualHeight)));
            };
            panel.MouseLeftButtonUp += (_, e) =>
            {
                if (dragStart == null) return;
                dragStart = null; panel.ReleaseMouseCapture(); e.Handled = true;
                var x = System.Windows.Controls.Canvas.GetLeft(panel); var y = System.Windows.Controls.Canvas.GetTop(panel);
                PositionChanged?.Invoke(settings.Anchor.StartsWith("右", StringComparison.Ordinal) ? Math.Max(0, surface.ActualWidth - panel.ActualWidth - x) : x,
                    settings.Anchor.EndsWith("下", StringComparison.Ordinal) ? Math.Max(0, surface.ActualHeight - panel.ActualHeight - y) : y);
            };
            SourceInitialized += (_, _) => ConfigureNativeWindow();
        }

        public void Update(InfoOverlaySettings settings, System.Collections.Generic.IReadOnlyDictionary<string, System.Text.Json.JsonElement>? values)
        {
            this.settings = settings;
            var rows = new System.Windows.Controls.StackPanel();
            var labelWidth = 0d;
            foreach (var item in settings.Items)
            {
                if (!item.Enabled) continue;
                var field = System.Linq.Enumerable.FirstOrDefault(InfoOverlayModel.Fields, f => f.Id == item.Field);
                var label = string.IsNullOrEmpty(item.Label) ? UiText.T(field?.Name ?? item.Field) : item.Label;
                labelWidth = Math.Max(labelWidth, new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), settings.FontSize, Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip).Width + 14);
            }
            labelWidth = Math.Min(labelWidth, Math.Max(100, surface.ActualWidth * .45));
            if (adjusting) rows.Children.Add(new System.Windows.Controls.TextBlock { Text = UiText.T("拖动面板调整位置"), Foreground = Brushes.Gold, FontSize = 12 });
            foreach (var item in settings.Items)
            {
                if (!item.Enabled) continue;
                var field = System.Linq.Enumerable.FirstOrDefault(InfoOverlayModel.Fields, f => f.Id == item.Field);
                if (field == null) continue;
                var row = new System.Windows.Controls.Grid();
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(labelWidth) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                var brush = new SolidColorBrush(InfoOverlayModel.ParseColor(string.IsNullOrEmpty(item.Color) ? settings.TextColor : item.Color));
                var label = new System.Windows.Controls.TextBlock { Text = string.IsNullOrEmpty(item.Label) ? UiText.T(field.Name) : item.Label,
                    FontSize = settings.FontSize, FontFamily = new FontFamily("Microsoft YaHei UI"), Foreground = brush, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 14, 0) };
                var value = new System.Windows.Controls.TextBlock { Text = InfoOverlayModel.Format(item, values),
                    FontSize = settings.FontSize, FontFamily = new FontFamily("Microsoft YaHei UI"), Foreground = brush };
                System.Windows.Controls.Grid.SetColumn(value, 1);
                row.Children.Add(label); row.Children.Add(value); rows.Children.Add(row);
            }
            panel.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * settings.BackgroundOpacity), 12, 16, 22));
            panel.Child = rows;
            PlacePanel();
        }

        public void SetAdjusting(bool value)
        {
            adjusting = value;
            if (!value) { dragStart = null; panel.ReleaseMouseCapture(); }
            panel.Cursor = value ? System.Windows.Input.Cursors.SizeAll : null;
            if (handle != IntPtr.Zero) ConfigureNativeWindow();
        }

        private void PlacePanel()
        {
            if (dragStart != null) return;
            panel.MaxWidth = Math.Max(1, surface.ActualWidth);
            panel.MaxHeight = Math.Max(1, surface.ActualHeight);
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var maxX = Math.Max(0, surface.ActualWidth - panel.DesiredSize.Width);
            var maxY = Math.Max(0, surface.ActualHeight - panel.DesiredSize.Height);
            System.Windows.Controls.Canvas.SetLeft(panel, Math.Clamp(settings.Anchor.StartsWith("右", StringComparison.Ordinal) ? maxX - settings.MarginX : settings.MarginX, 0, maxX));
            System.Windows.Controls.Canvas.SetTop(panel, Math.Clamp(settings.Anchor.EndsWith("下", StringComparison.Ordinal) ? maxY - settings.MarginY : settings.MarginY, 0, maxY));
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
                    nativeWidth, nativeHeight, SwpNoActivate);
        }

        public void SetOwner(IntPtr owner)
        {
            if (owner == IntPtr.Zero) return;
            if (handle == IntPtr.Zero) handle = new WindowInteropHelper(this).EnsureHandle();
            new WindowInteropHelper(this).Owner = owner;
        }

        public void ShowOverlay()
        {
            if (!IsVisible) Show();
            if (handle == IntPtr.Zero)
                handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, IntPtr.Zero,
                    hasNativeBounds ? nativeLeft : 0,
                    hasNativeBounds ? nativeTop : 0,
                    hasNativeBounds ? nativeWidth : 0,
                    hasNativeBounds ? nativeHeight : 0,
                    SwpNoActivate | (hasNativeBounds ? 0u : SwpNoMove | SwpNoSize) | SwpShowWindow);
        }

        private void ConfigureNativeWindow()
        {
            handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, ExStyleIndex).ToInt64();
            SetWindowLongPtr(handle, ExStyleIndex, new IntPtr((style & ~WsExTransparent)
                | (adjusting ? 0 : WsExTransparent) | WsExNoActivate | WsExToolWindow));
        }

        private const int ExStyleIndex = -20;
        private const long WsExTransparent = 0x20;
        private const long WsExNoActivate = 0x08000000;
        private const long WsExToolWindow = 0x80;
        private const uint SwpNoActivate = 0x0010;
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
