using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>A frozen replay source and its completed-Movie-frame capture boundaries.</summary>
    public sealed class StudioVideoExportPlan
    {
        private StudioVideoExportPlan(string movie, long startMovieFrame, long endMovieFrame,
            string? treeId, IReadOnlyDictionary<string, string>? originalHashes)
        {
            Movie = movie;
            StartMovieFrame = startMovieFrame;
            EndMovieFrame = endMovieFrame;
            TreeId = treeId;
            OriginalHashes = originalHashes == null ? null
                : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(originalHashes));
        }

        public string Movie { get; }
        /// <summary>Already completed boundary; capturing starts with the following frame.</summary>
        public long StartMovieFrame { get; }
        /// <summary>Completed boundary of the last frame to include in the video.</summary>
        public long EndMovieFrame { get; }
        public string? TreeId { get; }
        public IReadOnlyDictionary<string, string>? OriginalHashes { get; }

        public static StudioVideoExportPlan ForMovie(string movie)
        {
            var parsed = ParseMovie(movie);
            RequireExportable(parsed.Document, parsed.Frames);
            return new StudioVideoExportPlan(parsed.Canonical, 0, parsed.Frames, null, null);
        }

        public static StudioVideoExportPlan ForTimeline(TimelineTree tree, int firstNodeId, int secondNodeId)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            var nodes = ValidateTree(tree);
            if (!nodes.ContainsKey(firstNodeId) || !nodes.ContainsKey(secondNodeId))
                throw new InvalidDataException("导出节点不存在于所选时间线。");
            if (firstNodeId == secondNodeId)
                throw new InvalidDataException("请选择两个不同的导出节点。");

            var firstPath = PathTo(nodes, firstNodeId);
            var secondPath = PathTo(nodes, secondNodeId);
            TimelineNode start;
            TimelineNode end;
            IReadOnlyList<TimelineNode> path;
            if (secondPath.Any(node => node.Id == firstNodeId))
            {
                start = nodes[firstNodeId]; end = nodes[secondNodeId]; path = secondPath;
            }
            else if (firstPath.Any(node => node.Id == secondNodeId))
            {
                start = nodes[secondNodeId]; end = nodes[firstNodeId]; path = firstPath;
            }
            else throw new InvalidDataException("导出起止节点必须位于同一条祖先到后代的路径。");

            if (start.Frame >= end.Frame)
                throw new InvalidDataException("导出区间不能为空，终点必须晚于起点。");
            var source = ParseMovie(end.Movie);
            RequireExportable(source.Document, source.Frames);
            foreach (var node in path)
            {
                var ancestor = node.Id == end.Id ? source : ParseMovie(node.Movie);
                if (node.Frame < 0 || node.Frame > ancestor.Frames || node.Frame > source.Frames)
                    throw new InvalidDataException("时间线节点帧超出其完整序列。");
                if (!MovieV2Prefix.Matches(ancestor.Document, source.Document, node.Frame))
                    throw new InvalidDataException("时间线祖先的已执行输入或运行环境与终点不一致。");
            }

            // A node may retain future draft inputs. Keep them for replay identity,
            // while EndMovieFrame limits capture to the selected descendant.
            return new StudioVideoExportPlan(source.Canonical, start.Frame, end.Frame,
                tree.Id, tree.OriginalHashes);
        }

        private static (MovieV2Document Document, long Frames, string Canonical) ParseMovie(string movie)
        {
            if (string.IsNullOrWhiteSpace(movie)) throw new InvalidDataException("导出需要有效的 v2 序列。");
            var codec = new MovieV2Codec();
            var parsed = codec.Parse(new StringReader(movie), "<studio-video-export>");
            if (!parsed.Success || parsed.Document == null)
                throw new InvalidDataException("导出需要有效的 v2 序列：" + string.Join("；", parsed.Diagnostics));
            var report = new MovieV2Validator().Validate(parsed.Document, MovieV2ValidationContext.CreateDefault());
            if (!report.Success)
                throw new InvalidDataException("导出序列校验失败：" + string.Join("；", report.Diagnostics));
            return (parsed.Document, report.ExpandedFrames, codec.WriteCanonical(parsed.Document));
        }

        private static void RequireExportable(MovieV2Document movie, long frames)
        {
            if (frames < 1) throw new InvalidDataException("空序列无法导出视频。");
            if (movie.Runs.Any(run => run.FramesPerSecond != 50))
                throw new InvalidDataException("当前 MP4 导出仅支持全程 50 fps 的序列。");
        }

        private static Dictionary<int, TimelineNode> ValidateTree(TimelineTree tree)
        {
            if (string.IsNullOrWhiteSpace(tree.Id) || tree.Nodes == null || tree.OriginalHashes == null)
                throw new InvalidDataException("时间线数据不完整。");
            var nodes = new Dictionary<int, TimelineNode>();
            foreach (var node in tree.Nodes)
                if (node == null || node.Id < 0 || !nodes.TryAdd(node.Id, node))
                    throw new InvalidDataException("时间线节点编号无效或重复。");
            if (!nodes.TryGetValue(0, out var root) || root.ParentId != null || root.Frame != 0)
                throw new InvalidDataException("时间线起点无效。");
            foreach (var node in nodes.Values.Where(node => node.Id != 0))
                if (!node.ParentId.HasValue || !nodes.TryGetValue(node.ParentId.Value, out var parent)
                    || node.Frame < parent.Frame)
                    throw new InvalidDataException("时间线父链缺失或帧顺序无效。");

            var completed = new HashSet<int>();
            foreach (var node in nodes.Values)
            {
                var current = node;
                var visiting = new HashSet<int>();
                while (!completed.Contains(current.Id))
                {
                    if (!visiting.Add(current.Id)) throw new InvalidDataException("时间线父链存在循环。");
                    if (!current.ParentId.HasValue) break;
                    current = nodes[current.ParentId.Value];
                }
                completed.UnionWith(visiting);
            }
            return nodes;
        }

        private static IReadOnlyList<TimelineNode> PathTo(IReadOnlyDictionary<int, TimelineNode> nodes, int id)
        {
            var path = new List<TimelineNode>();
            var node = nodes[id];
            while (true)
            {
                path.Add(node);
                if (!node.ParentId.HasValue) break;
                node = nodes[node.ParentId.Value];
            }
            path.Reverse();
            return path;
        }
    }
}
