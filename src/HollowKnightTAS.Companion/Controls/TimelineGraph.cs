using System;
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
        private void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTimelineNode)) Schedule();
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
            var nodes = tree.Nodes.OrderBy(n => n.Id).ToArray();
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
            canvas.Height = Math.Max(240, (depth.Values.Max() + 1) * 108 + 48);
            var path = vm!.WorldlinePath.Select(n => n.Id).ToHashSet();
            var accent = new SolidColorBrush(Color.FromRgb(93, 214, 192));
            var muted = new SolidColorBrush(Color.FromRgb(66, 82, 104));
            foreach (var node in nodes.Where(n => n.ParentId != null))
            {
                var parent = node.ParentId!.Value;
                double x1 = positions[parent] + 84, y1 = 24 + depth[parent] * 108 + 64;
                double x2 = positions[node.Id] + 84, y2 = 24 + depth[node.Id] * 108;
                var line = new Polyline { Stroke = path.Contains(node.Id) ? accent : muted, StrokeThickness = 2,
                    Points = new PointCollection { new(x1, y1), new(x1, (y1 + y2) / 2), new(x2, (y1 + y2) / 2), new(x2, y2) }, IsHitTestVisible = false };
                canvas.Children.Add(line);
            }
            foreach (var node in nodes)
            {
                bool leaf = !children[node.Id].Any();
                var button = new Button
                {
                    Content = new TextBlock { Text = node.Label + "\n" + (leaf ? "世界线 " + node.Id : "分支节点"), TextAlignment = TextAlignment.Center },
                    Width = 168, Height = 64, Margin = new Thickness(0), Padding = new Thickness(8),
                    Background = new SolidColorBrush(vm.SelectedTimelineNode?.Id == node.Id ? Color.FromRgb(43, 96, 86) : Color.FromRgb(32, 42, 56)),
                    BorderBrush = path.Contains(node.Id) ? accent : muted,
                    BorderThickness = new Thickness(vm.SelectedTimelineNode?.Id == node.Id ? 3 : 1),
                    ToolTip = "选择节点；点击叶子切换世界线。恢复需点击上方按钮。"
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "HktasStudio.TimelineNode." + node.Id);
                button.Click += (_, _) => vm.SelectTimelineNode(node.Id);
                Canvas.SetLeft(button, positions[node.Id]); Canvas.SetTop(button, 24 + depth[node.Id] * 108);
                canvas.Children.Add(button);
            }
        }
    }
}
