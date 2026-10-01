using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using HollowKnightTAS.Core.Inspector;

namespace HollowKnightTAS.Companion.Services
{
    public abstract class InfoNotify : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Change<T>(ref T field, T value, [CallerMemberName] string? name = null)
        { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
    }

    public sealed record InfoField(string Id, string Name, string Kind = "number", string Unit = "");

    public sealed class InfoOverlayItem : InfoNotify
    {
        private string field = "position", label = "", color = "#FFFFFF", unit = "";
        private int precision = 3;
        private bool enabled = true, readyAtZero = true;
        private string? expression;
        [JsonIgnore] public bool IsCustom => Field == "custom";
        [JsonIgnore] public bool UsesExpression => IsCustom || Expression != InfoOverlayModel.DefaultExpression(Field);
        public string Expression { get => expression ?? InfoOverlayModel.DefaultExpression(Field); set => Change(ref expression, value); }
        [JsonIgnore] public string FieldName => InfoOverlayModel.Fields.FirstOrDefault(f => f.Id == Field)?.Name ?? Field;
        public string Field { get => field; set => Change(ref field, value); }
        public string Label { get => label; set => Change(ref label, value); }
        public string Color { get => color; set => Change(ref color, value); }
        public string Unit { get => unit; set => Change(ref unit, value); }
        public int Precision { get => precision; set => Change(ref precision, value); }
        public bool Enabled { get => enabled; set => Change(ref enabled, value); }
        public bool ReadyAtZero { get => readyAtZero; set => Change(ref readyAtZero, value); }
    }

    public sealed class InfoOverlaySettings : InfoNotify
    {
        private bool enabled = true;
        private double fontSize = 16, opacity = .65, marginX = 8, marginY = 8;
        private string anchor = "右上", color = "#FFFFFF", hotkey = "F11";
        public int Version { get; set; } = 3;
        public bool Enabled { get => enabled; set => Change(ref enabled, value); }
        public double FontSize { get => fontSize; set => Change(ref fontSize, value); }
        public double BackgroundOpacity { get => opacity; set => Change(ref opacity, value); }
        public double MarginX { get => marginX; set => Change(ref marginX, value); }
        public double MarginY { get => marginY; set => Change(ref marginY, value); }
        public string Anchor { get => anchor; set => Change(ref anchor, value); }
        public string TextColor { get => color; set => Change(ref color, value); }
        public string Hotkey { get => hotkey; set => Change(ref hotkey, value); }
        public ObservableCollection<InfoOverlayItem> Items { get; set; } = new();
        [JsonIgnore] public static string[] Anchors { get; } = { "左上", "右上", "左下", "右下" };
        [JsonIgnore] public static string[] Hotkeys { get; } = { "F11", "F12", "无" };
        public static InfoOverlaySettings Defaults() => new()
        {
            Items = new(new[] { "frame", "room", "position", "velocity", "dash", "shade", "healthPair", "soul" }
                .Select(id => InfoOverlayModel.NewItem(id)))
        };

        public void Validate()
        {
            if (Version != 3 || !Anchors.Contains(Anchor) || !Hotkeys.Contains(Hotkey)
                || !double.IsFinite(FontSize) || FontSize < 10 || FontSize > 40
                || !double.IsFinite(BackgroundOpacity) || BackgroundOpacity < 0 || BackgroundOpacity > 1
                || !double.IsFinite(MarginX) || MarginX < 0 || MarginX > 10000
                || !double.IsFinite(MarginY) || MarginY < 0 || MarginY > 10000
                || !InfoOverlayModel.IsColor(TextColor) || Items == null || Items.Count > 32)
                throw new InvalidDataException("信息显示设置无效：检查字号、透明度、位置和颜色。");
            foreach (var item in Items)
            {
                if (item == null || !InfoOverlayModel.Fields.Any(f => f.Id == item.Field)
                    || item.Precision < 0 || item.Precision > 6 || item.Label == null || item.Label.Length > 48
                    || item.Label.Any(char.IsControl) || item.Unit == null || item.Unit.Length > 16
                    || item.Unit.Any(char.IsControl) || (!string.IsNullOrEmpty(item.Color) && !InfoOverlayModel.IsColor(item.Color)))
                    throw new InvalidDataException("信息条目无效：检查字段、小数位、名称、单位和颜色。");
                InfoWatchExpression.Parse(item.Expression);
            }
        }
        public static InfoOverlaySettings Load(string path)
        {
            if (!File.Exists(path)) return Defaults();
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("信息显示设置文件过大。");
            var result = JsonSerializer.Deserialize<InfoOverlaySettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("信息显示设置文件为空。");
            if (result.Version == 1)
            {
                // Move only the previous default placement; retain user-customized positions and rows.
                if (result.Anchor == "左上" && result.MarginX == 16 && result.MarginY == 160)
                { result.Anchor = "右上"; result.MarginX = 8; result.MarginY = 8; }
                result.Version = 2;
            }
            if (result.Version == 2)
            {
                // Older preset rows stored an unused custom query. Do not reinterpret it as an edit.
                if (result.Items != null)
                    foreach (var item in result.Items.Where(i => i != null && !i.IsCustom))
                        item.Expression = InfoOverlayModel.DefaultExpression(item.Field);
                result.Version = 3;
            }
            result.Validate(); return result;
        }
        public void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".new", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".new", path, true);
        }
    }

    public static class InfoOverlayModel
    {
        public static IReadOnlyList<InfoField> Fields { get; } = new[]
        {
            new InfoField("frame", "帧", "integer"), new InfoField("nativeFrame", "原生帧", "integer"),
            new InfoField("room", "房间", "text"), new InfoField("position", "坐标", "pair"),
            new InfoField("x", "坐标 X"), new InfoField("y", "坐标 Y"),
            new InfoField("velocity", "速度", "pair"), new InfoField("vx", "速度 X"), new InfoField("vy", "速度 Y"),
            new InfoField("dash", "冲刺冷却", "cooldown", "s"), new InfoField("shade", "暗影冲刺冷却", "cooldown", "s"),
            new InfoField("attack", "攻击冷却", "cooldown", "s"), new InfoField("healthPair", "生命", "health"),
            new InfoField("health", "当前生命", "integer"), new InfoField("maxHealth", "最大生命", "integer"),
            new InfoField("soul", "灵魂", "integer"), new InfoField("reserveSoul", "储备灵魂", "integer"),
            new InfoField("grounded", "着地", "bool"), new InfoField("facingRight", "朝向", "direction"),
            new InfoField("jumping", "跳跃中", "bool"), new InfoField("dashing", "冲刺中", "bool"),
            new InfoField("custom", "自定义字段", "custom")
        };
        public static string[] WatchExamples { get; } =
        {
            "hero.dashCooldownTimer", "hero.cState.wallSliding", "player.geo", "player.equippedCharms[0]",
            "game.gameState", "position.x", "velocity.y", "component(\"/Knight\", \"HeroController\").jump_steps",
            "fsm(\"/Knight\", \"Spell Control\", \"MP Cost\")"
        };
        public static string DefaultExpression(string id) => id switch
        {
            "custom" => "hero.dashCooldownTimer",
            "position" => "\"X \" + x + \"   Y \" + y",
            "velocity" => "\"X \" + vx + \"   Y \" + vy",
            "healthPair" => "health + \" / \" + maxHealth",
            _ => id
        };
        public static string[] Watches(InfoOverlaySettings settings) => settings.Items
            .Where(i => i.Enabled && i.UsesExpression).Select(i => i.Expression).Distinct().ToArray();
        public static InfoOverlayItem NewItem(string id)
        {
            var field = Fields.Single(f => f.Id == id);
            return new() { Field = id, Precision = field.Kind == "cooldown" ? 2 : 3, Unit = field.Unit, Color = "" };
        }
        public static bool IsColor(string? value) => value != null && value.Length == 7 && value[0] == '#'
            && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        public static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);
        public static IReadOnlyDictionary<string, JsonElement> Decode(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new FormatException("Unsupported info snapshot.");
            return document.RootElement.GetProperty("values").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        public static string Format(InfoOverlayItem item, IReadOnlyDictionary<string, JsonElement>? values)
        {
            var field = Fields.FirstOrDefault(f => f.Id == item.Field);
            if (field == null || values == null) return "—";
            if (item.UsesExpression)
            {
                if (!values.TryGetValue("watch:" + item.Expression, out var evaluated)) return "—";
                if (evaluated.ValueKind == JsonValueKind.String) return evaluated.GetString() ?? "—";
                if (evaluated.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return UiText.T(evaluated.GetBoolean() ? "是" : "否");
                if (!evaluated.TryGetDoubleSafe(out var n)) return "—";
                return n.ToString("F" + item.Precision, CultureInfo.InvariantCulture) + Suffix(item.Unit);
            }
            string Number(string key, int precision)
            {
                if (!values.TryGetValue(key, out var v) || !v.TryGetDoubleSafe(out var n)) return "—";
                return n.ToString("F" + precision, CultureInfo.InvariantCulture);
            }
            if (field.Kind == "pair")
            {
                var a = Number(field.Id == "position" ? "x" : "vx", item.Precision);
                var b = Number(field.Id == "position" ? "y" : "vy", item.Precision);
                return a == "—" || b == "—" ? "—" : "X " + a + "   Y " + b + Suffix(item.Unit);
            }
            if (field.Kind == "health")
            {
                var a = Number("health", 0); var b = Number("maxHealth", 0);
                return a == "—" || b == "—" ? "—" : a + " / " + b;
            }
            if (!values.TryGetValue(item.IsCustom ? "watch:" + item.Expression : field.Id, out var value) || value.ValueKind == JsonValueKind.Null) return "—";
            if (item.IsCustom)
            {
                if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "—";
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return UiText.T(value.GetBoolean() ? "是" : "否");
            }
            if (field.Kind == "text") return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "—" : "—";
            if (field.Kind is "bool" or "direction")
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "—";
                return UiText.T(field.Kind == "direction" ? (value.GetBoolean() ? "右" : "左") : (value.GetBoolean() ? "是" : "否"));
            }
            if (!value.TryGetDoubleSafe(out var number)) return "—";
            if (field.Kind == "cooldown")
            {
                if (number <= 0 && item.ReadyAtZero) return UiText.T("就绪");
                number = Math.Max(0, number);
            }
            return number.ToString("F" + (field.Kind == "integer" ? 0 : item.Precision), CultureInfo.InvariantCulture) + Suffix(item.Unit);
        }
        private static string Suffix(string text) => string.IsNullOrEmpty(text) ? "" : " " + text;
        private static bool TryGetDoubleSafe(this JsonElement value, out double number)
        { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number); }
    }
}
