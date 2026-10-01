using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using HollowKnightTAS.Core.Media;

namespace HollowKnightTAS.Runtime.Media
{
    /// <summary>Renders directly into captured video pixels; never changes a Unity object or desktop window.</summary>
    public sealed class VideoInfoOverlay : IDisposable
    {
        private readonly InfoOverlayVideoSettings settings;
        private readonly Font font;
        public VideoInfoOverlay(InfoOverlayVideoSettings settings)
        {
            settings.Validate(); this.settings = settings;
            font = new Font("Microsoft YaHei UI", (float)settings.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        public void Composite(byte[] rgb, int width, int height, IReadOnlyDictionary<string, object?> values)
        {
            if (settings.Rows.Count == 0) return;
            var texts = settings.Rows.Select(row => settings.Format(row, key => values.TryGetValue(key, out var value) ? value : null)).ToArray();
            float labelWidth = 0, valueWidth = 0, lineHeight;
            using (var measure = new Bitmap(1, 1))
            using (var graphics = Graphics.FromImage(measure))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                lineHeight = (float)Math.Ceiling(font.GetHeight(graphics));
                for (int i = 0; i < texts.Length; i++)
                {
                    labelWidth = Math.Max(labelWidth, graphics.MeasureString(settings.Rows[i].Label, font).Width);
                    valueWidth = Math.Max(valueWidth, graphics.MeasureString(texts[i], font).Width);
                }
            }
            labelWidth = Math.Min(labelWidth, width * .45f);
            int panelWidth = Math.Max(1, Math.Min(width, (int)Math.Ceiling(labelWidth + valueWidth + 34)));
            int panelHeight = Math.Max(1, Math.Min(height, (int)Math.Ceiling(lineHeight * texts.Length + 20)));
            using (var bitmap = new Bitmap(panelWidth, panelHeight, PixelFormat.Format32bppPArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                using (var background = new SolidBrush(Color.FromArgb((int)(255 * settings.BackgroundOpacity), 12, 16, 22)))
                using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                {
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    graphics.FillRectangle(background, 0, 0, panelWidth, panelHeight);
                    for (int i = 0; i < texts.Length && 10 + i * lineHeight < panelHeight; i++)
                    using (var brush = new SolidBrush(ColorTranslator.FromHtml(settings.Rows[i].Color)))
                    {
                        graphics.DrawString(settings.Rows[i].Label, font, brush,
                            new RectangleF(10, 10 + i * lineHeight, labelWidth + 1, lineHeight), format);
                        graphics.DrawString(texts[i], font, brush,
                            new RectangleF(24 + labelWidth, 10 + i * lineHeight, Math.Max(1, panelWidth - labelWidth - 34), lineHeight), format);
                    }
                }
                var bits = bitmap.LockBits(new Rectangle(0, 0, panelWidth, panelHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var pixels = new byte[checked(bits.Stride * panelHeight)]; Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length);
                    int left = (int)Math.Max(0, Math.Min(width - panelWidth, settings.Right ? width - panelWidth - settings.MarginX : settings.MarginX));
                    int top = (int)Math.Max(0, Math.Min(height - panelHeight, settings.Bottom ? height - panelHeight - settings.MarginY : settings.MarginY));
                    InfoOverlayPixels.Blend(rgb, width, height, pixels, panelWidth, panelHeight, bits.Stride, left, top);
                }
                finally { bitmap.UnlockBits(bits); }
            }
        }
        public void Dispose() => font.Dispose();
    }
}
