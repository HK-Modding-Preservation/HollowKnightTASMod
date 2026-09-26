using System;
using HollowKnightTAS.Companion.Services;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion.Controls
{
    public sealed class TimelineGraph : ScrollViewer
    {
        private readonly Canvas canvas = new();
        private MainViewModel? vm;
        private bool scheduled;
        public TimelineGraph()
        {
            PropertyChangedEventManager.AddHandler(UiText.Current, LanguageChanged, string.Empty);
            Content = canvas;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Background = new SolidColorBrush(Color.FromRgb(13, 19, 27));
            DataContextChanged += (_, e) =>
            {
                if (vm != null) vm.PropertyChanged -= Changed;
                vm = e.NewValue as MainViewModel;
                if (vm != null) vm.PropertyChanged += Changed;
                Schedule();
            };
            Loaded += (_, _) => { vm?.InitializeWorldlines(); Schedule(); };
        }
        private void LanguageChanged(object? sender, PropertyChangedEventArgs e) => Schedule();
        private void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTimelineNode)
                || e.PropertyName == nameof(MainViewModel.SelectedTimelineTreeId)
                || e.PropertyName == nameof(MainViewModel.SelectedTimelineTree)
                || e.PropertyName == nameof(MainViewModel.WorldlinePath)
                || e.PropertyName == nameof(MainViewModel.VideoStartNodeId)
                || e.PropertyName == nameof(MainViewModel.VideoEndNodeId)
                || e.PropertyName == nameof(MainViewModel.VideoRangeStatus)) Schedule();
        }
        private void Schedule()
        {
            if (scheduled) return;
            scheduled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { scheduled = false; Draw(); }));
        }
        private void Draw()
        {
            canvas.Children.Clear();
            var tree = vm?.SelectedTimelineTree;
            if (tree == null) return;
            var nodes = tree.Nodes.OrderBy(n => tree.PathTo(n.Id).Count).ThenBy(n => n.Id).ToArray();
            var children = nodes.Where(n => n.ParentId != null).ToLookup(n => n.ParentId!.Value);
            var depth = new Dictionary<int, int>();
            foreach (var node in nodes) depth[node.Id] = node.ParentId == null ? 0 : depth[node.ParentId.Value] + 1;
            var positions = new Dictionary<int, double>();
            // DFS leaf ordering keeps each subtree contiguous, including interleaved save times.
            var stack = new Stack<int>(); stack.Push(0); int column = 0;
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!children[id].Any()) positions[id] = 24 + column++ * 202;
                else foreach (var child in children[id].Reverse()) stack.Push(child.Id);
            }
            foreach (var node in nodes.Reverse())
                if (children[node.Id].Any()) positions[node.Id] = children[node.Id].Average(c => positions[c.Id]);
            canvas.Width = Math.Max(450, column * 202 + 48);
            const double nodeHeight = 80;
            const double rowPitch = 124;
            canvas.Height = Math.Max(240, (depth.Values.Max() + 1) * rowPitch + 48);
            var path = vm!.WorldlinePath.Select(n => n.Id).ToHashSet();
            var accent = new SolidColorBrush(Color.FromRgb(93, 214, 192));
            var muted = new SolidColorBrush(Color.FromRgb(66, 82, 104));
            foreach (var node in nodes.Where(n => n.ParentId != null))
            {
                var parent = node.ParentId!.Value;
                double x1 = positions[parent] + 84, y1 = 24 + depth[parent] * rowPitch + nodeHeight;
                double x2 = positions[node.Id] + 84, y2 = 24 + depth[node.Id] * rowPitch;
                var line = new Polyline { Stroke = path.Contains(node.Id) ? accent : muted, StrokeThickness = 2,
                    Points = new PointCollection { new(x1, y1), new(x1, (y1 + y2) / 2), new(x2, (y1 + y2) / 2), new(x2, y2) }, IsHitTestVisible = false };
                canvas.Children.Add(line);
            }
            foreach (var node in nodes)
            {
                bool leaf = !children[node.Id].Any();
                bool videoStart = vm.VideoStartNodeId == node.Id;
                bool videoEnd = vm.VideoEndNodeId == node.Id;
                var startBrush = new SolidColorBrush(Color.FromRgb(235, 203, 139));
                var endBrush = new SolidColorBrush(Color.FromRgb(136, 191, 247));
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var endpointLabels = new List<string>();
                if (videoStart || videoEnd)
                {
                    var badges = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) };
                    void AddBadge(string source, string automationName, Brush brush)
                    {
                        var label = UiText.T(source);
                        endpointLabels.Add(label);
                        var badge = new TextBlock { Text = label, Foreground = brush, FontWeight = FontWeights.SemiBold, FontSize = 10,
                            Margin = new Thickness(3, 0, 3, 0) };
                        System.Windows.Automation.AutomationProperties.SetAutomationId(badge, automationName + node.Id);
                        badges.Children.Add(badge);
                    }
                    if (videoStart) AddBadge("导出起点", "HktasStudio.VideoStartNode.", startBrush);
                    if (videoEnd) AddBadge("导出终点", "HktasStudio.VideoEndNode.", endBrush);
                    content.Children.Add(badges);
                }
                var nodeText = UiText.T(node.Label) + "\n" + UiText.T(leaf ? "世界线 " + node.Id : "分支节点");
                content.Children.Add(new TextBlock { Text = nodeText, TextAlignment = TextAlignment.Center });
                var button = new Button
                {
                    Content = content,
                    Width = 168, Height = nodeHeight, Margin = new Thickness(0), Padding = new Thickness(6),
                    Background = new SolidColorBrush(vm.SelectedTimelineNode?.Id == node.Id ? Color.FromRgb(43, 96, 86) : Color.FromRgb(32, 42, 56)),
                    BorderBrush = videoStart ? startBrush : videoEnd ? endBrush : path.Contains(node.Id) ? accent : muted,
                    BorderThickness = new Thickness(vm.SelectedTimelineNode?.Id == node.Id ? 3 : 1),
                    ToolTip = UiText.T("选择节点；点击叶子切换世界线。恢复需点击上方按钮。")
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "HktasStudio.TimelineNode." + node.Id);
                System.Windows.Automation.AutomationProperties.SetName(button, nodeText + (endpointLabels.Count > 0 ? " · " + string.Join(" / ", endpointLabels) : string.Empty));
                button.Click += (_, _) => vm.SelectTimelineNode(node.Id);
                Canvas.SetLeft(button, positions[node.Id]); Canvas.SetTop(button, 24 + depth[node.Id] * rowPitch);
                canvas.Children.Add(button);
            }
        }
    }
}
