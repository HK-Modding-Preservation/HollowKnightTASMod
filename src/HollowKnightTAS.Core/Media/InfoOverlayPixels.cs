using System;

namespace HollowKnightTAS.Core.Media
{
    public static class InfoOverlayPixels
    {
        /// <summary>Composite a top-down premultiplied BGRA panel onto Unity's bottom-up RGB frame.</summary>
        public static void Blend(byte[] rgb, int width, int height, byte[] bgra, int panelWidth, int panelHeight,
            int stride, int left, int top)
        {
            if (width <= 0 || height <= 0 || panelWidth <= 0 || panelHeight <= 0 || left < 0 || top < 0
                || panelWidth > width - left || panelHeight > height - top
                || stride < checked(panelWidth * 4) || bgra.Length < checked(stride * panelHeight)
                || rgb.Length != checked(width * height * 3)) throw new ArgumentException("Invalid overlay pixel bounds.");
            for (int y = 0; y < panelHeight; y++)
            {
                int target = ((height - 1 - top - y) * width + left) * 3;
                for (int x = 0; x < panelWidth; x++, target += 3)
                {
                    int source = y * stride + x * 4;
                    int inverse = 255 - bgra[source + 3];
                    rgb[target] = (byte)Math.Min(255, bgra[source + 2] + (rgb[target] * inverse + 127) / 255);
                    rgb[target + 1] = (byte)Math.Min(255, bgra[source + 1] + (rgb[target + 1] * inverse + 127) / 255);
                    rgb[target + 2] = (byte)Math.Min(255, bgra[source] + (rgb[target + 2] * inverse + 127) / 255);
                }
            }
        }
    }
}
