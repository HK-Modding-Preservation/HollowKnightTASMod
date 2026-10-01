using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using HollowKnightTAS.Core.Media;

namespace HollowKnightTAS.Runtime.Media
{
    /// <summary>Uses Windows GDI+ directly: Unity's System.Drawing assembly contains unsupported stubs.</summary>
    public sealed class VideoInfoOverlay : IDisposable
    {
        private readonly InfoOverlayVideoSettings settings;
        private IntPtr family, font;
        // GDI+ stays initialized for the process, while every renderer releases its own native resources.
        private static readonly IntPtr Token = Start();
        public VideoInfoOverlay(InfoOverlayVideoSettings settings)
        {
            settings.Validate(); this.settings = settings;
            _ = Token;
            Check(GdipCreateFontFamilyFromName("Microsoft YaHei UI", IntPtr.Zero, out family));
            try { Check(GdipCreateFont(family, (float)settings.FontSize, 0, 2, out font)); }
            catch { GdipDeleteFontFamily(family); family = IntPtr.Zero; throw; }
        }
        public void Composite(byte[] rgb, int width, int height, IReadOnlyDictionary<string, object?> values)
        {
            if (settings.Rows.Count == 0) return;
            var texts = settings.Rows.Select(row => settings.Format(row, key => values.TryGetValue(key, out var value) ? value : null)).ToArray();
            float labelWidth = 0, valueWidth = 0, lineHeight;
            using (var measure = new Surface(1, 1))
            {
                Check(GdipGetFontHeight(font, measure.Graphics, out lineHeight));
                lineHeight = (float)Math.Ceiling(lineHeight);
                for (int i = 0; i < texts.Length; i++)
                {
                    labelWidth = Math.Max(labelWidth, Measure(measure.Graphics, settings.Rows[i].Label));
                    valueWidth = Math.Max(valueWidth, Measure(measure.Graphics, texts[i]));
                }
            }
            labelWidth = Math.Min(labelWidth, width * .45f);
            int panelWidth = Math.Max(1, Math.Min(width, (int)Math.Ceiling(labelWidth + valueWidth + 34)));
            int panelHeight = Math.Max(1, Math.Min(height, (int)Math.Ceiling(lineHeight * texts.Length + 20)));
            using (var surface = new Surface(panelWidth, panelHeight))
            {
                IntPtr background;
                Check(GdipCreateSolidFill(((uint)(255 * settings.BackgroundOpacity) << 24) | 0x000c1016, out background));
                try { Check(GdipFillRectangle(surface.Graphics, background, 0, 0, panelWidth, panelHeight)); }
                finally { GdipDeleteBrush(background); }
                for (int i = 0; i < texts.Length && 10 + i * lineHeight < panelHeight; i++)
                {
                    IntPtr brush;
                    Check(GdipCreateSolidFill(0xff000000 | uint.Parse(settings.Rows[i].Color.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture), out brush));
                    try
                    {
                        Draw(surface.Graphics, settings.Rows[i].Label, brush, new Rect(10, 10 + i * lineHeight, labelWidth + 1, lineHeight));
                        Draw(surface.Graphics, texts[i], brush, new Rect(24 + labelWidth, 10 + i * lineHeight, Math.Max(1, panelWidth - labelWidth - 34), lineHeight));
                    }
                    finally { GdipDeleteBrush(brush); }
                }
                Check(GdipFlush(surface.Graphics, 1));
                var pixels = new byte[checked(panelWidth * panelHeight * 4)];
                Marshal.Copy(surface.Pixels, pixels, 0, pixels.Length);
                int left = (int)Math.Max(0, Math.Min(width - panelWidth, settings.Right ? width - panelWidth - settings.MarginX : settings.MarginX));
                int top = (int)Math.Max(0, Math.Min(height - panelHeight, settings.Bottom ? height - panelHeight - settings.MarginY : settings.MarginY));
                InfoOverlayPixels.Blend(rgb, width, height, pixels, panelWidth, panelHeight, panelWidth * 4, left, top);
            }
        }
        private float Measure(IntPtr graphics, string text)
        {
            var layout = new Rect(0, 0, 100000, 100000);
            Check(GdipMeasureString(graphics, text, text.Length, font, ref layout, IntPtr.Zero, out var bounds, out _, out _));
            return bounds.Width;
        }
        private void Draw(IntPtr graphics, string text, IntPtr brush, Rect layout)
        {
            IntPtr format;
            Check(GdipCreateStringFormat(0x1000, 0, out format)); // NoWrap
            try
            {
                Check(GdipSetStringFormatTrimming(format, 3)); // EllipsisCharacter
                Check(GdipDrawString(graphics, text, text.Length, font, ref layout, format, brush));
            }
            finally { GdipDeleteStringFormat(format); }
        }
        public void Dispose()
        {
            if (font != IntPtr.Zero) { GdipDeleteFont(font); font = IntPtr.Zero; }
            if (family != IntPtr.Zero) { GdipDeleteFontFamily(family); family = IntPtr.Zero; }
        }
        private sealed class Surface : IDisposable
        {
            internal IntPtr Pixels, Image, Graphics;
            internal Surface(int width, int height)
            {
                try
                {
                    var bytes = new byte[checked(width * height * 4)];
                    Pixels = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, Pixels, bytes.Length);
                    Check(GdipCreateBitmapFromScan0(width, height, width * 4, 0xE200B, Pixels, out Image)); // 32bpp premultiplied ARGB
                    Check(GdipGetImageGraphicsContext(Image, out Graphics));
                    Check(GdipSetTextRenderingHint(Graphics, 3)); // AntiAliasGridFit
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (Graphics != IntPtr.Zero) { GdipDeleteGraphics(Graphics); Graphics = IntPtr.Zero; }
                if (Image != IntPtr.Zero) { GdipDisposeImage(Image); Image = IntPtr.Zero; }
                if (Pixels != IntPtr.Zero) { Marshal.FreeHGlobal(Pixels); Pixels = IntPtr.Zero; }
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Rect
        {
            internal float X, Y, Width, Height;
            internal Rect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Startup
        {
            internal uint Version;
            internal IntPtr Callback;
            internal int SuppressThread, SuppressCodecs;
        }
        private static IntPtr Start() { var input = new Startup { Version = 1 }; Check(GdiplusStartup(out var token, ref input, IntPtr.Zero)); return token; }
        private static void Check(int status) { if (status != 0) throw new InvalidOperationException("Video information GDI+ status " + status); }
        private const string Dll = "gdiplus.dll";
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdiplusStartup(out IntPtr token, ref Startup input, IntPtr output);
        [DllImport(Dll, ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GdipCreateFontFamilyFromName(string name, IntPtr collection, out IntPtr family);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipCreateFont(IntPtr family, float size, int style, int unit, out IntPtr font);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipDeleteFontFamily(IntPtr family);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipDeleteFont(IntPtr font);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipGetFontHeight(IntPtr font, IntPtr graphics, out float height);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr pixels, out IntPtr bitmap);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipSetTextRenderingHint(IntPtr graphics, int hint);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipCreateSolidFill(uint argb, out IntPtr brush);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipFillRectangle(IntPtr graphics, IntPtr brush, float x, float y, float width, float height);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipDeleteBrush(IntPtr brush);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipFlush(IntPtr graphics, int intention);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipDeleteGraphics(IntPtr graphics);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipDisposeImage(IntPtr image);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipCreateStringFormat(int flags, ushort language, out IntPtr format);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipSetStringFormatTrimming(IntPtr format, int trimming);
        [DllImport(Dll, ExactSpelling = true)] private static extern int GdipDeleteStringFormat(IntPtr format);
        [DllImport(Dll, ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GdipDrawString(IntPtr graphics, string text, int length, IntPtr font, ref Rect layout, IntPtr format, IntPtr brush);
        [DllImport(Dll, ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font, ref Rect layout, IntPtr format, out Rect bounds, out int fitted, out int lines);
    }
}
