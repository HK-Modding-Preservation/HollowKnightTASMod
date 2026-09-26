using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class TimelineNode
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public long Frame { get; set; }
        public string Movie { get; set; } = "";
        public bool IsBranchTip { get; set; }
        public bool IsUnverified { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        [JsonIgnore] public string Label => Id == 0 ? "起点 · Frame 0" : IsBranchTip ? $"最远进度 · Frame {Frame}" : IsUnverified ? $"未验证存档 {Id} · Frame {Frame}" : $"存档 {Id} · Frame {Frame}";
    }

    public sealed class TimelineTree
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "时间线 " + DateTime.Now.ToString("MM-dd HH:mm:ss");
        public int NextId { get; set; } = 1;
        public Dictionary<string, string> OriginalHashes { get; set; } = new();
        public List<TimelineNode> Nodes { get; set; } = new() { new TimelineNode() };
        [JsonIgnore] public IEnumerable<TimelineNode> Leaves => Nodes.Where(n => !Nodes.Any(c => c.ParentId == n.Id));
        public List<TimelineNode> PathTo(int id)
        {
            var path = new List<TimelineNode>();
            var byId = Nodes.ToDictionary(n => n.Id);
            var node = byId[id];
            while (true)
            {
                if (path.Count >= Nodes.Count) throw new InvalidDataException("时间线父子关系存在循环。");
                path.Add(node);
                if (node.ParentId == null) break;
                node = byId[node.ParentId.Value];
            }
            path.Reverse();
            return path;
        }
        public static MovieV2Document Parse(string movie) => new MovieV2Codec().Parse(new StringReader(movie), "<timeline>").Document
            ?? throw new InvalidDataException("时间线序列无效。");
        public static bool SameBaseline(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
            => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var hash) && hash == p.Value);

        public TimelineNode Add(long frame, string movie, IReadOnlyDictionary<string, string> hashes, bool unverified = false)
        {
            var candidate = Parse(movie);
            _ = MovieV2Prefix.Take(candidate, frame); // Also validates the frame bound.
            var root = Nodes.Single(n => n.Id == 0);
            if (root.Movie.Length == 0 || Parse(root.Movie).Header.EnvironmentSha256 == "none")
            {
                root.Movie = movie;
                OriginalHashes = new Dictionary<string, string>(hashes);
                foreach (var unresolved in Nodes.Where(n => n.Frame == 0 && n.Movie.Length != 0
                    && Parse(n.Movie).Header.EnvironmentSha256 == "none")) unresolved.Movie = movie;
            }
            if (!SameBaseline(OriginalHashes, hashes) || !MovieV2Prefix.Matches(Parse(root.Movie), candidate, 0))
                throw new InvalidOperationException("存档起点或运行环境不同，请新建序列以建立独立时间线。");
            // Match only history already executed at a node, never its future draft.
            var parent = Nodes.Where(n => !n.IsBranchTip && !n.IsUnverified && n.Frame <= frame)
                .OrderByDescending(n => n.Frame).ThenByDescending(n => n.Id)
                .First(n => MovieV2Prefix.Matches(Parse(n.Movie), candidate, n.Frame));
            var node = new TimelineNode { Id = NextId++, ParentId = parent.Id, Frame = frame, Movie = movie, IsUnverified = unverified };
            Nodes.Add(node);
            return node;
        }

        public TimelineNode UpdateTip(long frame, string movie, IReadOnlyDictionary<string, string> hashes, int? preferredTip)
        {
            var candidate = Parse(movie);
            _ = MovieV2Prefix.Take(candidate, frame);
            var root = Nodes.Single(n => n.Id == 0);
            if (root.Movie.Length == 0 || Parse(root.Movie).Header.EnvironmentSha256 == "none")
            {
                root.Movie = movie;
                OriginalHashes = new Dictionary<string, string>(hashes);
                foreach (var unresolved in Nodes.Where(n => n.Frame == 0 && n.Movie.Length != 0
                    && Parse(n.Movie).Header.EnvironmentSha256 == "none")) unresolved.Movie = movie;
            }
            // A bookmark becomes verified only when matching input has actually run.
            if (SameBaseline(OriginalHashes, hashes))
                foreach (var saved in Nodes.Where(n => n.IsUnverified && n.Frame <= frame))
                    if (MovieV2Prefix.Matches(Parse(saved.Movie), candidate, saved.Frame)) saved.IsUnverified = false;
            var tip = Nodes.FirstOrDefault(n => n.Id == preferredTip && n.IsBranchTip);
            if (tip != null && tip.Frame <= candidate.Runs.Sum(r => r.RepeatCount)
                && MovieV2Prefix.Matches(Parse(tip.Movie), candidate, tip.Frame)
                && SameBaseline(OriginalHashes, hashes))
            {
                _ = MovieV2Prefix.Take(candidate, frame);
                tip.Frame = Math.Max(tip.Frame, frame);
                tip.Movie = movie;
                // A new explicit save can become the tip's parent without changing
                // the tip id (quick-slot references remain stable).
                tip.ParentId = Nodes.Where(n => !n.IsBranchTip && !n.IsUnverified && n.Frame <= tip.Frame)
                    .OrderByDescending(n => n.Frame).ThenByDescending(n => n.Id)
                    .First(n => MovieV2Prefix.Matches(Parse(n.Movie), candidate, n.Frame)).Id;
                return tip;
            }
            var created = Add(frame, movie, hashes);
            created.IsBranchTip = true;
            return created;
        }

        public long MatchingSavedFrame(string movie)
        {
            var candidate = Parse(movie);
            var count = candidate.Runs.Sum(r => r.RepeatCount);
            return Nodes.Where(n => !n.IsBranchTip && !n.IsUnverified && n.Movie.Length > 0 && n.Frame <= count
                    && MovieV2Prefix.Matches(Parse(n.Movie), candidate, n.Frame))
                .Select(n => n.Frame).DefaultIfEmpty(0).Max();
        }
        public HashSet<int> Subtree(int id)
        {
            if (!Nodes.Any(n => n.Id == id)) throw new InvalidOperationException("节点不存在。");
            var children = Nodes.Where(n => n.ParentId != null).ToLookup(n => n.ParentId!.Value);
            var result = new HashSet<int>();
            var pending = new Stack<int>(); pending.Push(id);
            while (pending.Count > 0)
            {
                var next = pending.Pop(); result.Add(next);
                foreach (var child in children[next]) pending.Push(child.Id);
            }
            return result;
        }
        public void Delete(int id)
        {
            var removed = Subtree(id);
            // Deleting the root clears its whole history, retaining a fresh frame-zero anchor.
            Nodes.RemoveAll(n => n.Id != 0 && removed.Contains(n.Id));
        }
    }

    public sealed class TimelineLibrary
    {
        public int Version { get; set; } = 1;
        public bool LegacyImported { get; set; }
        public string ActiveTreeId { get; set; } = "";
        public Dictionary<int, string> QuickSlots { get; set; } = new();
        public List<TimelineTree> Trees { get; set; } = new();
    }

    public sealed class StudioTimelineStore
    {
        private readonly string path;
        public TimelineLibrary Library { get; private set; }
        public StudioTimelineStore(string path)
        {
            this.path = path;
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new InvalidDataException("时间线数据超过 256 MiB。");
                Library = JsonSerializer.Deserialize<TimelineLibrary>(File.ReadAllText(path)) ?? throw new InvalidDataException("时间线数据为空。");
                Validate(Library);
            }
            else Library = new TimelineLibrary();
            if (Library.Trees.Count == 0)
            {
                var tree = new TimelineTree(); Library.Trees.Add(tree); Library.ActiveTreeId = tree.Id;
            }
        }
        public void Update(Action<TimelineLibrary> edit)
        {
            // Publish only after atomic persistence succeeds. Disk errors cannot leave a phantom node.
            var next = JsonSerializer.Deserialize<TimelineLibrary>(JsonSerializer.Serialize(Library))!;
            edit(next); Validate(next);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next);
            if (bytes.Length > 256L * 1024 * 1024) throw new InvalidDataException("时间线数据超过 256 MiB，请清理不需要的分支。");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(bytes); file.Flush(true); }
                File.Move(temporary, path, true);
                Library = next;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void Validate(TimelineLibrary library)
        {
            if (library.Version != 1 || library.Trees.Select(t => t.Id).Distinct().Count() != library.Trees.Count)
                throw new InvalidDataException("时间线版本或标识无效。");
            foreach (var tree in library.Trees)
            {
                var ids = tree.Nodes.Select(n => n.Id).ToHashSet();
                if (ids.Count != tree.Nodes.Count || !ids.Contains(0) || tree.NextId <= ids.Max())
                    throw new InvalidDataException("时间线节点编号无效。");
                var root = tree.Nodes.Single(n => n.Id == 0);
                if (root.ParentId != null || root.Frame != 0) throw new InvalidDataException("时间线起点无效。");
                foreach (var node in tree.Nodes.Where(n => n.Id != 0))
                    if (node.ParentId == null || node.ParentId < 0 || node.ParentId == node.Id || !ids.Contains(node.ParentId.Value)
                        || node.Frame < tree.Nodes.Single(n => n.Id == node.ParentId).Frame || node.Movie.Length == 0)
                        throw new InvalidDataException("时间线父子关系无效。");
                foreach (var node in tree.Nodes) _ = tree.PathTo(node.Id);
                if (tree.Nodes.Any(n => n.IsBranchTip && tree.Nodes.Any(c => c.ParentId == n.Id)))
                    throw new InvalidDataException("最远进度必须是叶子节点。");
            }
        }
    }
}
