using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace HollowKnightTAS.Runtime.Observation
{
    internal static class ObservationData
    {
        internal static Dictionary<string, object?> Map(params object?[] pairs)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i]!, pairs[i + 1]);
            return result;
        }

        internal static string Json(object value) => JsonConvert.SerializeObject(value, new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            FloatFormatHandling = FloatFormatHandling.String,
            Culture = CultureInfo.InvariantCulture
        });

        internal static object Vector(Vector2 value) => Map("x", value.x, "y", value.y);
        internal static object Vector(Vector3 value) => Map("x", value.x, "y", value.y, "z", value.z);
        internal static object Vector(Vector4 value) => Map("x", value.x, "y", value.y, "z", value.z, "w", value.w);
        internal static object Quaternion(Quaternion value) => Map("x", value.x, "y", value.y, "z", value.z, "w", value.w);
        internal static object Bounds(Bounds value) => Map("center", Vector(value.center), "size", Vector(value.size));
        internal static object Rect(Rect value) => Map("x", value.x, "y", value.y, "width", value.width, "height", value.height);
        internal static object Error(Exception error) => Map("type", error.GetType().FullName, "message", error.Message);
        internal static object Omitted(string reason, int? count = null) => Map("omitted", true, "reason", reason, "count", count);
        internal static string Path(Transform transform)
        {
            var parts = new Stack<string>();
            for (var current = transform; current != null; current = current.parent) parts.Push(current.name);
            return string.Join("/", parts.ToArray());
        }
    }

    // Reflection is restricted to instance fields. No property getter, arbitrary ToString,
    // IEnumerable implementation, or arbitrary nested object graph is invoked.
    internal sealed class ObservationValues
    {
        internal const int CollectionLimit = 512;
        internal const int StringLimit = 16384;
        internal const int ValueBudget = 16384;
        private readonly Func<UObject, object> reference;
        private readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
        private readonly Dictionary<string, FieldInfo?> namedCache = new Dictionary<string, FieldInfo?>();

        internal ObservationValues(Func<UObject, object> reference) { this.reference = reference; }

        internal object? Read(object? owner, string name)
        {
            if (owner == null) return null;
            var field = FindField(owner.GetType(), name, false);
            return field?.GetValue(owner);
        }

        internal object? ReadStatic(Type type, string name) => FindField(type, name, true)?.GetValue(null);
        internal bool HasField(Type type, string name, bool isStatic = false) => FindField(type, name, isStatic) != null;

        private FieldInfo? FindField(Type type, string name, bool isStatic)
        {
            string key = type.AssemblyQualifiedName + ":" + name + ":" + isStatic;
            if (namedCache.TryGetValue(key, out var found)) return found;
            for (var current = type; current != null; current = current.BaseType)
            {
                found = current.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance));
                if (found != null) break;
            }
            namedCache[key] = found;
            return found;
        }

        internal FieldInfo[] Fields(Type type)
        {
            if (fieldCache.TryGetValue(type, out var found)) return found;
            var result = new List<FieldInfo>();
            for (var current = type; current != null; current = current.BaseType)
                result.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            found = result.OrderBy(f => f.DeclaringType?.FullName, StringComparer.Ordinal).ThenBy(f => f.Name, StringComparer.Ordinal).ToArray();
            fieldCache[type] = found;
            return found;
        }

        internal object CaptureFields(object owner, Func<FieldInfo, bool>? predicate = null)
        {
            int budget = ValueBudget;
            var result = new List<object>();
            foreach (var field in Fields(owner.GetType()))
            {
                if (predicate != null && !predicate(field)) continue;
                object? value;
                try { value = Encode(field.GetValue(owner), 0, ref budget); }
                catch (Exception error) { value = ObservationData.Map("error", ObservationData.Error(error)); }
                result.Add(ObservationData.Map("name", field.Name, "declaringType", field.DeclaringType?.FullName,
                    "type", field.FieldType.FullName, "public", field.IsPublic, "value", value));
            }
            return ObservationData.Map("fieldCount", result.Count, "fields", result, "collectionLimit", CollectionLimit,
                "stringLimit", StringLimit, "valueBudget", ValueBudget, "remainingValueBudget", budget);
        }

        internal object? Encode(object? value)
        {
            int budget = ValueBudget;
            return Encode(value, 0, ref budget);
        }

        private object? Encode(object? value, int depth, ref int budget)
        {
            if (value == null) return null;
            if (budget-- <= 0) { budget = 0; return ObservationData.Omitted("perComponentValueBudget"); }
            if (value is UObject unityObject) return unityObject == null ? null : reference(unityObject);
            var type = value.GetType();
            if (type.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(type);
                return ObservationData.Map("enumType", type.FullName, "name", Enum.GetName(type, value),
                    "numeric", Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture));
            }
            if (value is string text)
                return text.Length <= StringLimit ? (object)text : ObservationData.Map("prefix", text.Substring(0, StringLimit),
                    "omitted", true, "reason", "stringLengthLimit", "originalLength", text.Length, "omittedCount", text.Length - StringLimit);
            if (value is char character) return new string(character, 1);
            if (value is bool || value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint
                || value is long || value is ulong || value is float || value is double || value is decimal) return value;
            if (value is Vector2 v2) return ObservationData.Vector(v2);
            if (value is Vector3 v3) return ObservationData.Vector(v3);
            if (value is Vector4 v4) return ObservationData.Vector(v4);
            if (value is Quaternion q) return ObservationData.Quaternion(q);
            if (value is Color color) return ObservationData.Map("r", color.r, "g", color.g, "b", color.b, "a", color.a);
            if (value is Color32 color32) return ObservationData.Map("r", color32.r, "g", color32.g, "b", color32.b, "a", color32.a);
            if (value is Rect rect) return ObservationData.Rect(rect);
            if (value is Bounds bounds) return ObservationData.Bounds(bounds);
            if (value is LayerMask mask) return mask.value;
            if (value is Vector2Int v2i) return ObservationData.Map("x", v2i.x, "y", v2i.y);
            if (value is Vector3Int v3i) return ObservationData.Map("x", v3i.x, "y", v3i.y, "z", v3i.z);
            if (value is Matrix4x4 matrix)
            {
                var elements = new float[16];
                for (int i = 0; i < 16; i++) elements[i] = matrix[i];
                return elements;
            }
            // Exact built-in arrays and List<T> only. A custom IList indexer can execute mod code.
            if (value is Array array && array.Rank != 1)
                return ObservationData.Omitted("multiDimensionalArray", array.Length);
            if (value is Array || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                var list = (IList)value;
                if (depth >= 4) return ObservationData.Omitted("collectionDepthLimit", list.Count);
                int count = Math.Min(list.Count, CollectionLimit);
                var items = new List<object?>(count);
                int index = 0;
                for (; index < count && budget > 0; index++) items.Add(Encode(list[index], depth + 1, ref budget));
                return ObservationData.Map("items", items, "count", list.Count, "omittedCount", list.Count - index,
                    "omittedReason", index < list.Count ? (budget <= 0 ? "perComponentValueBudget" : "collectionLengthLimit") : null);
            }
            // NamedVariable subclasses are PlayMaker's known value wrappers. Read their fields,
            // including raw FsmArray backing arrays, without invoking RawValue/Values getters.
            if (value is HutongGames.PlayMaker.NamedVariable named)
            {
                if (depth >= 4) return ObservationData.Omitted("knownWrapperDepthLimit");
                var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var field in Fields(type))
                {
                    if (field.Name == "value" || field.Name == "values" || field.Name.EndsWith("Values", StringComparison.Ordinal)
                        || field.Name == "objectReferences" || field.Name == "sourceArray" || field.Name == "type" || field.Name == "intValue" || field.Name == "enumName")
                        raw[field.Name] = Encode(field.GetValue(named), depth + 1, ref budget);
                }
                return ObservationData.Map("type", type.FullName, "name", Read(named, "name"), "useVariable", Read(named, "useVariable"),
                    "raw", raw, "valueSource", "existingBackingFields");
            }
            return ObservationData.Map("omitted", true, "reason", "unsupportedObjectGraph", "type", type.FullName);
        }
    }
}
