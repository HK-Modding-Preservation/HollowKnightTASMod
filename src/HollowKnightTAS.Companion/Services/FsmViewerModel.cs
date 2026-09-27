using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HollowKnightTAS.Companion.Services
{
    public sealed record FsmEdge(string Event, string Target);
    public sealed record FsmNode(string Name, FsmEdge[] Edges, string[] Actions, bool ActionsLoaded);
    public sealed record FsmGraph(string Start, FsmNode[] Nodes, FsmEdge[] Globals)
    {
        public static FsmGraph Parse(JsonElement graph)
        {
            FsmEdge[] Edges(JsonElement array) => array.EnumerateArray().Select(e => new FsmEdge(Text(e, "event"), Text(e, "toState"))).ToArray();
            return new(Text(graph, "startState"), graph.GetProperty("states").EnumerateArray().Select(s => new FsmNode(
                Text(s, "name"), Edges(s.GetProperty("transitions")),
                s.GetProperty("actionTypes").ValueKind == JsonValueKind.Array
                    ? s.GetProperty("actionTypes").EnumerateArray().Select(a => a.GetString() ?? "?").ToArray() : Array.Empty<string>(),
                Flag(s, "actionsLoaded"))).ToArray(), Edges(graph.GetProperty("globalTransitions")));
        }
        internal static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString()! : "";
        internal static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.True;
    }
    public sealed class FsmSelection : INotifyPropertyChanged
    {
        public string Id { get; init; } = "";
        public string ObjectId { get; init; } = "";
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public string Scene { get; init; } = "";
        public string[] Ancestors { get; init; } = Array.Empty<string>();
        public string StableKey => Scene + "\n" + Path + "\n" + Name;
        public string Label => Path + " / " + Name + " [" + Id.Split(':').Last() + "]";
        private bool selected;
        public bool Selected { get => selected; set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public sealed record FsmObject(string Id, string Path, string Scene, bool Enemy)
    {
        public override string ToString() => (Enemy ? "◆ " : "") + Scene + " / " + Path + " [" + Id.Split(':').Last() + "]";
    }
    public sealed class FsmCard
    {
        public FsmSelection Target { get; init; } = new();
        public string Version { get; set; } = "";
        public FsmGraph? Graph { get; set; }
        public string Current { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Live { get; set; }
    }
}
