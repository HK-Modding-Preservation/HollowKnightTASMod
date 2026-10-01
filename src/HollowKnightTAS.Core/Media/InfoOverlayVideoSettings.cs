using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Inspector;

namespace HollowKnightTAS.Core.Media
{
    public sealed class InfoOverlayVideoSettings
    {
        public double FontSize { get; set; } = 16;
        public double BackgroundOpacity { get; set; } = .65;
        public double MarginX { get; set; } = 8;
        public double MarginY { get; set; } = 8;
        public bool Right { get; set; } = true;
        public bool Bottom { get; set; }
        public bool English { get; set; }
        public List<InfoOverlayVideoRow> Rows { get; set; } = new List<InfoOverlayVideoRow>();
        public void Validate()
        {
            bool Range(double v, double min, double max) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max;
            if (!Range(FontSize, 10, 40) || !Range(BackgroundOpacity, 0, 1) || !Range(MarginX, 0, 10000)
                || !Range(MarginY, 0, 10000) || Rows == null || Rows.Count > 32)
                throw new InvalidDataException("Invalid video information overlay settings.");
            foreach (var row in Rows)
            {
                if (row == null || row.Id == null || row.Id.Length > 32 || row.Id.Any(char.IsControl)
                    || !new[] { "number", "integer", "text", "pair", "cooldown", "health", "bool", "direction", "custom" }.Contains(row.Kind)
                    || row.Precision < 0 || row.Precision > 6 || row.Label == null || row.Label.Length > 48 || row.Label.Any(char.IsControl)
                    || row.Unit == null || row.Unit.Length > 16 || row.Unit.Any(char.IsControl)
                    || row.Color == null || row.Color.Length != 7 || row.Color[0] != '#'
                    || !uint.TryParse(row.Color.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("Invalid video information overlay row.");
                InfoWatchExpression.Parse(row.Expression);
            }
        }
        public string[] Watches() => Rows.Where(r => r.UsesExpression).Select(r => r.Expression).Distinct().ToArray();
        public string Format(InfoOverlayVideoRow row, Func<string, object?> read) => InfoOverlayText.Format(
            row.Id, row.Kind, row.Expression, row.UsesExpression, row.Precision, row.ReadyAtZero, row.Unit, read,
            text => !English ? text : text == "是" ? "Yes" : text == "否" ? "No" : text == "右" ? "Right" : text == "左" ? "Left" : "Ready");
    }
    public sealed class InfoOverlayVideoRow
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "number";
        public string Expression { get; set; } = "";
        public bool UsesExpression { get; set; }
        public string Label { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Color { get; set; } = "#FFFFFF";
        public int Precision { get; set; } = 3;
        public bool ReadyAtZero { get; set; } = true;
    }
}
