using System;
using System.Globalization;

namespace HollowKnightTAS.Core.Inspector
{
    /// <summary>Shared by the desktop overlay and the video compositor.</summary>
    public static class InfoOverlayText
    {
        public static string Format(string id, string kind, string expression, bool usesExpression,
            int precision, bool readyAtZero, string unit, Func<string, object?> read, Func<string, string> translate)
        {
            string Suffix() => string.IsNullOrEmpty(unit) ? "" : " " + unit;
            string Number(string key, int digits) => TryNumber(read(key), out var n)
                ? n.ToString("F" + digits, CultureInfo.InvariantCulture) : "—";
            if (!usesExpression && kind == "pair")
            {
                var a = Number(id == "position" ? "x" : "vx", precision);
                var b = Number(id == "position" ? "y" : "vy", precision);
                return a == "—" || b == "—" ? "—" : "X " + a + "   Y " + b + Suffix();
            }
            if (!usesExpression && kind == "health")
            {
                var a = Number("health", 0); var b = Number("maxHealth", 0);
                return a == "—" || b == "—" ? "—" : a + " / " + b;
            }
            var value = read(usesExpression ? "watch:" + expression : id);
            if (usesExpression)
            {
                if (value is string text) return text;
                if (value is bool flag) return translate(flag ? "是" : "否");
            }
            else
            {
                if (kind == "text") return value as string ?? "—";
                if (kind == "bool" || kind == "direction")
                    return value is bool flag ? translate(kind == "direction" ? (flag ? "右" : "左") : (flag ? "是" : "否")) : "—";
            }
            if (!TryNumber(value, out var number)) return "—";
            if (!usesExpression && kind == "cooldown")
            {
                if (number <= 0 && readyAtZero) return translate("就绪");
                number = Math.Max(0, number);
            }
            return number.ToString("F" + (!usesExpression && kind == "integer" ? 0 : precision), CultureInfo.InvariantCulture) + Suffix();
        }
        private static bool TryNumber(object? value, out double number)
        {
            number = 0;
            if (!(value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint
                || value is long || value is ulong || value is float || value is double || value is decimal)) return false;
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }
    }
}
