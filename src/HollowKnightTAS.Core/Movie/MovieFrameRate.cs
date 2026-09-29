using System;
using System.Globalization;

namespace HollowKnightTAS.Core.Movie
{
    // Decimal is exact at the input boundary; persistence and the native clock use
    // the reduced integer ratio. Never round unsupported precision silently.
    public static class MovieFrameRate
    {
        public const int Scale = 1000000;
        public static bool IsValid(decimal value) => value >= 1 && value <= 1000
            && decimal.Truncate(value * Scale) == value * Scale;

        public static bool TryParse(string text, out decimal value)
        {
            value = 0;
            text = text?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Length > 64) return false;
            var point = text.IndexOf('.');
            if (point >= 0 && text.TrimEnd('0').Length - point - 1 > 6) return false;
            return decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && IsValid(value);
        }

        public static decimal Parse(string text)
        {
            if (!TryParse(text, out var value))
                throw new FormatException("FPS 必须在 1–1000 之间，最多 6 位小数。");
            return value;
        }

        public static void ToRatio(decimal value, out int numerator, out int denominator)
        {
            if (!IsValid(value)) throw new ArgumentOutOfRangeException(nameof(value));
            numerator = (int)(value * Scale);
            denominator = Scale;
            var a = numerator;
            var b = denominator;
            while (b != 0) { var r = a % b; a = b; b = r; }
            numerator /= a;
            denominator /= a;
        }

        public static decimal FromRatio(int numerator, int denominator)
        {
            if (numerator <= 0 || numerator > 1000 * Scale || denominator <= 0 || denominator > Scale)
                throw new ArgumentOutOfRangeException(nameof(numerator));
            var value = (decimal)numerator / denominator;
            if (!IsValid(value) || value * denominator != numerator)
                throw new ArgumentOutOfRangeException(nameof(numerator));
            return value;
        }
    }
}
