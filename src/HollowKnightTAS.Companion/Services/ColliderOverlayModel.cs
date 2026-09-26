using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace HollowKnightTAS.Companion.Services
{
    public readonly record struct ColliderOverlayPoint(double X, double Y);

    public sealed class ColliderOverlayPath
    {
        public ColliderOverlayPath(IReadOnlyList<ColliderOverlayPoint> points, bool closed)
        {
            Points = points;
            Closed = closed;
        }

        public IReadOnlyList<ColliderOverlayPoint> Points { get; }
        public bool Closed { get; }
    }

    public sealed class ColliderOverlayObject
    {
        public ColliderOverlayObject(string id, string classification,
            IReadOnlyList<ColliderOverlayPath> paths)
        {
            Id = id;
            Classification = classification;
            Paths = paths;
        }

        public string Id { get; }
        public string Classification { get; }
        public IReadOnlyList<ColliderOverlayPath> Paths { get; }
    }

    public sealed class ColliderOverlaySnapshot
    {
        public ColliderOverlaySnapshot(string snapshotId, int? nextOffset,
            IReadOnlyList<ColliderOverlayObject> objects)
        {
            SnapshotId = snapshotId;
            NextOffset = nextOffset;
            Objects = objects;
        }

        public string SnapshotId { get; }
        public int? NextOffset { get; }
        public IReadOnlyList<ColliderOverlayObject> Objects { get; }

        public ColliderOverlaySnapshot Append(ColliderOverlaySnapshot page)
        {
            if (!string.Equals(SnapshotId, page.SnapshotId, StringComparison.Ordinal))
                throw new InvalidOperationException("Collider pages belong to different snapshots.");
            return new ColliderOverlaySnapshot(SnapshotId, page.NextOffset,
                Objects.Concat(page.Objects).ToArray());
        }
    }

    public static class ColliderOverlayDecoder
    {
        public static ColliderOverlaySnapshot Decode(string snapshotId, string snapshotJson)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson))
                throw new FormatException("World snapshot JSON is empty.");
            using var document = JsonDocument.Parse(snapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new FormatException("World snapshot JSON must be an object.");

            int? nextOffset = null;
            if (TryInt(document.RootElement, "nextOffset", out var next) && next >= 0)
                nextOffset = next;

            var objects = new List<ColliderOverlayObject>();
            if (!document.RootElement.TryGetProperty("objects", out var objectArray)
                || objectArray.ValueKind != JsonValueKind.Array)
                return new ColliderOverlaySnapshot(snapshotId, nextOffset, objects);

            foreach (var objectElement in objectArray.EnumerateArray())
            {
                if (objectElement.ValueKind != JsonValueKind.Object) continue;
                var id = TryString(objectElement, "id") ?? string.Empty;
                var objectClassification = TryString(objectElement, "classification")
                    ?? TryString(objectElement, "kind") ?? "other";

                if (objectElement.TryGetProperty("colliders", out var colliders)
                    && colliders.ValueKind == JsonValueKind.Array)
                {
                    var componentIndex = 0;
                    foreach (var collider in colliders.EnumerateArray())
                    {
                        if (collider.ValueKind != JsonValueKind.Object) continue;
                        var paths = new List<ColliderOverlayPath>();
                        var colliderClassification = TryString(collider, "classification");
                        var classification = string.IsNullOrWhiteSpace(colliderClassification)
                            ? objectClassification : colliderClassification!;
                        if (collider.TryGetProperty("screenPaths", out var screenPaths))
                            ReadPaths(screenPaths, paths);
                        if (paths.Count != 0)
                        {
                            var suffix = TryInt(collider, "componentIndex", out var index)
                                ? index.ToString(CultureInfo.InvariantCulture)
                                : componentIndex.ToString(CultureInfo.InvariantCulture);
                            objects.Add(new ColliderOverlayObject(id + ":" + suffix,
                                classification, paths));
                        }
                        componentIndex++;
                    }
                }
                else if (objectElement.TryGetProperty("screenPaths", out var screenPaths))
                {
                    var paths = new List<ColliderOverlayPath>();
                    ReadPaths(screenPaths, paths);
                    if (paths.Count != 0)
                        objects.Add(new ColliderOverlayObject(id, objectClassification, paths));
                }
            }

            return new ColliderOverlaySnapshot(snapshotId, nextOffset, objects);
        }

        private static void ReadPaths(JsonElement value, List<ColliderOverlayPath> destination)
        {
            if (value.ValueKind != JsonValueKind.Array) return;
            foreach (var path in value.EnumerateArray())
            {
                if (path.ValueKind != JsonValueKind.Object
                    || !path.TryGetProperty("points", out var points)
                    || points.ValueKind != JsonValueKind.Array) continue;
                var decoded = new List<ColliderOverlayPoint>();
                foreach (var point in points.EnumerateArray())
                {
                    if (!TryPoint(point, out var result)) continue;
                    decoded.Add(result);
                }
                if (decoded.Count >= 2)
                {
                    var closed = TryBool(path, "closed", out var valueClosed) && valueClosed;
                    destination.Add(new ColliderOverlayPath(decoded, closed));
                }
            }
        }

        private static bool TryPoint(JsonElement element, out ColliderOverlayPoint result)
        {
            result = default;
            double x, y;
            if (element.ValueKind == JsonValueKind.Array
                && element.GetArrayLength() >= 2)
            {
                if (!TryNumber(element[0], out x) || !TryNumber(element[1], out y)) return false;
            }
            else if (element.ValueKind == JsonValueKind.Object
                && TryDouble(element, "x", out x) && TryDouble(element, "y", out y))
            {
            }
            else return false;
            if (double.IsNaN(x) || double.IsInfinity(x)
                || double.IsNaN(y) || double.IsInfinity(y)) return false;
            result = new ColliderOverlayPoint(x, y);
            return true;
        }

        private static bool TryNumber(JsonElement element, out double value)
        {
            value = 0;
            if (element.ValueKind == JsonValueKind.Number) return element.TryGetDouble(out value);
            return element.ValueKind == JsonValueKind.String
                && double.TryParse(element.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value);
        }

        private static string? TryString(JsonElement parent, string name)
            => parent.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static bool TryDouble(JsonElement parent, string name, out double value)
        {
            value = 0;
            if (!parent.TryGetProperty(name, out var element)) return false;
            if (element.ValueKind == JsonValueKind.Number) return element.TryGetDouble(out value);
            return element.ValueKind == JsonValueKind.String
                && double.TryParse(element.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value);
        }

        private static bool TryInt(JsonElement parent, string name, out int value)
        {
            value = 0;
            return parent.TryGetProperty(name, out var element)
                && element.TryGetInt32(out value);
        }

        private static bool TryBool(JsonElement parent, string name, out bool value)
        {
            value = false;
            if (!parent.TryGetProperty(name, out var element)
                || (element.ValueKind != JsonValueKind.True
                    && element.ValueKind != JsonValueKind.False)) return false;
            value = element.GetBoolean();
            return true;
        }
    }

    public static class ColliderOverlayColors
    {
        public static Color For(string? classification)
        {
            var value = classification ?? string.Empty;
            if (value.Equals("knight", StringComparison.OrdinalIgnoreCase)
                || value.Equals("player", StringComparison.OrdinalIgnoreCase)) return Colors.Yellow;
            if (value.Equals("enemy", StringComparison.OrdinalIgnoreCase)) return Colors.Red;
            if (value.Equals("attack", StringComparison.OrdinalIgnoreCase)) return Colors.Cyan;
            if (value.Equals("terrain", StringComparison.OrdinalIgnoreCase)) return Colors.LimeGreen;
            if (value.Equals("trigger", StringComparison.OrdinalIgnoreCase)) return Colors.LightBlue;
            if (value.Equals("hazard", StringComparison.OrdinalIgnoreCase)) return Colors.MediumPurple;
            return Colors.Orange;
        }
    }
}
