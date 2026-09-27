using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace HollowKnightTAS.Core.Inspector
{
    /// <summary>A bounded read-only field path, not a CLR expression evaluator.</summary>
    public sealed class InfoWatchQuery
    {
        public const int MaximumLength = 512;
        private readonly List<Segment> segments = new List<Segment>();
        private InfoWatchQuery(string expression) { Expression = expression; }
        public string Expression { get; }
        public string Root { get; private set; } = "";
        public string ObjectPath { get; private set; } = "";
        public string ComponentName { get; private set; } = "";
        public string FsmName { get; private set; } = "";
        public string VariableName { get; private set; } = "";

        public static InfoWatchQuery Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaximumLength)
                throw new FormatException("Enter a read-only field path (maximum 512 characters).");
            var query = new InfoWatchQuery(expression);
            int offset = 0;
            void Space() { while (offset < expression.Length && char.IsWhiteSpace(expression[offset])) offset++; }
            void Expect(char token)
            { Space(); if (offset >= expression.Length || expression[offset++] != token) throw new FormatException("Expected '" + token + "'."); }
            string Identifier()
            {
                Space(); var start = offset;
                if (offset >= expression.Length || !(char.IsLetter(expression[offset]) || expression[offset] == '_'))
                    throw new FormatException("Expected a field name.");
                offset++;
                while (offset < expression.Length && (char.IsLetterOrDigit(expression[offset]) || expression[offset] == '_')) offset++;
                return expression.Substring(start, offset - start);
            }
            string Quoted()
            {
                Expect('"'); var text = new StringBuilder(); bool closed = false;
                while (offset < expression.Length)
                {
                    var next = expression[offset++];
                    if (next == '"') { closed = true; break; }
                    if (next == '\\')
                    {
                        if (offset >= expression.Length) break;
                        next = expression[offset++];
                        if (next != '"' && next != '\\') throw new FormatException("Only escaped quotes and backslashes are supported.");
                    }
                    if (char.IsControl(next)) throw new FormatException("Control characters are not supported.");
                    text.Append(next);
                }
                if (!closed || text.Length == 0) throw new FormatException("Expected a nonempty quoted name.");
                return text.ToString();
            }
            query.Root = Identifier();
            if (query.Root == "component" || query.Root == "fsm")
            {
                Expect('('); query.ObjectPath = Quoted(); Expect(',');
                if (query.Root == "component") query.ComponentName = Quoted();
                else { query.FsmName = Quoted(); Expect(','); query.VariableName = Quoted(); }
                Expect(')');
                if (!query.ObjectPath.StartsWith("/", StringComparison.Ordinal))
                    throw new FormatException("Use an absolute object hierarchy path beginning with /.");
            }
            else if (query.Root != "hero" && query.Root != "player" && query.Root != "game"
                && query.Root != "position" && query.Root != "velocity")
                throw new FormatException("Root must be hero, player, game, position, velocity, component(...), or fsm(...).");
            Space();
            while (offset < expression.Length)
            {
                if (query.segments.Count == 16) throw new FormatException("Field paths may contain at most 16 segments.");
                Expect('.'); var name = Identifier(); Space(); int? index = null;
                if (offset < expression.Length && expression[offset] == '[')
                {
                    offset++; Space(); var start = offset;
                    while (offset < expression.Length && expression[offset] >= '0' && expression[offset] <= '9') offset++;
                    if (offset == start || offset - start > 6) throw new FormatException("Use a nonnegative array/list index (up to six digits).");
                    index = int.Parse(expression.Substring(start, offset - start), CultureInfo.InvariantCulture); Expect(']');
                }
                query.segments.Add(new Segment(name, index)); Space();
            }
            if (query.segments.Count == 0 && query.Root != "fsm") throw new FormatException("Add a field name after the root.");
            return query;
        }

        public object? Read(object? root)
        {
            object? value = root;
            foreach (var segment in segments)
            {
                if (value == null) throw new InvalidOperationException("Target is not available in the current scene.");
                var field = FindField(value.GetType(), segment.Name);
                if (field == null) throw new InvalidOperationException("Field not found: " + value.GetType().Name + "." + segment.Name + ". Methods and property getters are not evaluated.");
                value = field.GetValue(value);
                if (segment.Index is int index)
                {
                    if (value is Array array && array.Rank == 1)
                    { if (index >= array.Length) throw new IndexOutOfRangeException("Array index is out of range."); value = array.GetValue(index); }
                    else if (value != null && value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(List<>))
                    { var list = (IList)value; if (index >= list.Count) throw new IndexOutOfRangeException("List index is out of range."); value = list[index]; }
                    else throw new InvalidOperationException("Indexing supports arrays and List<T> fields only.");
                }
            }
            if (value == null) throw new InvalidOperationException("Value is unavailable.");
            if (value is string text) return text.Length <= 512 ? text : text.Substring(0, 512) + "…";
            if (value is char character) return character.ToString();
            var type = value.GetType();
            if (type.IsEnum) return Enum.Format(type, value, "G");
            if (value is float single && (float.IsNaN(single) || float.IsInfinity(single))
                || value is double number && (double.IsNaN(number) || double.IsInfinity(number)))
                throw new InvalidOperationException("Value is not a finite number.");
            if (type.IsPrimitive && !(value is IntPtr) && !(value is UIntPtr) || value is decimal) return value;
            throw new InvalidOperationException("Select a scalar field (number, bool, string or enum), for example .x on a vector.");
        }
        private static FieldInfo? FindField(Type type, string name)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var field = current.GetField(name, flags) ?? current.GetField("<" + name + ">k__BackingField", flags);
                if (field != null) return field;
            }
            return null;
        }
        private sealed class Segment
        {
            public Segment(string name, int? index) { Name = name; Index = index; }
            public string Name { get; }
            public int? Index { get; }
        }
    }
}
