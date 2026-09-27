using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HollowKnightTAS.Companion.Services;

namespace HollowKnightTAS.Companion.Controls
{
    public sealed class FsmGraphView : UserControl
    {
        private readonly GraphCanvas canvas = new();
        private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 340 };
        private readonly TextBlock status = new(), detail = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox search = new() { Width = 140 };
        private readonly ScaleTransform scale = new(1, 1);
        private Point? drag;
        private double startX, startY;
        private bool fitPending;
        public FsmGraphView()
        {
            var root = new StackPanel(); var bar = new WrapPanel { Margin = new Thickness(4) };
            void Button(string label, Action action) { var b = new Button { Content = UiText.T(label), Margin = new Thickness(3), Padding = new Thickness(7, 3, 7, 3) }; b.Click += (_, _) => action(); bar.Children.Add(b); }
            Button("−", () => Zoom(scale.ScaleX / 1.2)); Button("+", () => Zoom(scale.ScaleX * 1.2));
            Button("适应", Fit); Button("定位当前状态", () => Locate(canvas.Card?.Current ?? ""));
            Button("放大/还原", () => { scroll.Height = scroll.Height < 500 ? 680 : 340; Fit(); });
            search.ToolTip = UiText.T("搜索状态"); bar.Children.Add(search);
            Button("搜索状态", () => Locate(canvas.Positions.Keys.FirstOrDefault(n => n.Contains(search.Text, StringComparison.OrdinalIgnoreCase)) ?? ""));
            root.Children.Add(bar); root.Children.Add(status);
            canvas.LayoutTransform = scale; scroll.Content = canvas; root.Children.Add(scroll);
            root.Children.Add(new ScrollViewer { Content = detail, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            canvas.NodeClicked += node => detail.Text = node.Name + "\n" + string.Join("\n", node.Edges.Select(edge => edge.Event + " → " + edge.Target)) + "\n" + (node.ActionsLoaded
                ? string.Join("\n", node.Actions.Select((a, i) => $"{i}: {a}")) : UiText.T("动作尚未加载，观察不会触发初始化。"));
            scroll.SizeChanged += (_, _) => { if (fitPending) Fit(); };
            scroll.PreviewMouseWheel += (_, e) => { Zoom(scale.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15)); e.Handled = true; };
            canvas.MouseLeftButtonDown += (_, e) => { drag = e.GetPosition(scroll); startX = scroll.HorizontalOffset; startY = scroll.VerticalOffset; canvas.CaptureMouse(); };
            canvas.MouseMove += (_, e) => { if (drag is Point point && e.LeftButton == MouseButtonState.Pressed) { var now = e.GetPosition(scroll); scroll.ScrollToHorizontalOffset(startX + point.X - now.X); scroll.ScrollToVerticalOffset(startY + point.Y - now.Y); } };
            canvas.MouseLeftButtonUp += (_, _) => { drag = null; canvas.ReleaseMouseCapture(); };
            canvas.LostMouseCapture += (_, _) => drag = null;
        }
        public void Update(FsmCard card)
        {
            var layoutVersion = canvas.LayoutVersion;
            canvas.Update(card); status.Text = card.Status;
            bool changed = layoutVersion != canvas.LayoutVersion;
            if (changed) { fitPending = true; Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(Fit)); }
        }
        private void Zoom(double zoom) { scale.ScaleX = scale.ScaleY = Math.Clamp(zoom, .15, 2.5); }
        private void Fit()
        {
            double width = scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth - 20;
            double height = scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight - 20;
            if (canvas.Width > 0 && width > 0 && height > 0) { fitPending = false; Zoom(Math.Min(1, Math.Min(width / canvas.Width, height / canvas.Height))); }
        }
        private void Locate(string name)
        {
            if (!canvas.Positions.TryGetValue(name, out var rect)) return;
            Zoom(Math.Max(.8, scale.ScaleX)); scroll.UpdateLayout();
            scroll.ScrollToHorizontalOffset((rect.X + rect.Width / 2) * scale.ScaleX - scroll.ViewportWidth / 2);
            scroll.ScrollToVerticalOffset((rect.Y + rect.Height / 2) * scale.ScaleY - scroll.ViewportHeight / 2);
        }
        public sealed class GraphCanvas : FrameworkElement
        {
            public FsmCard? Card { get; private set; }
            public string GraphVersion { get; private set; } = "";
            public string LayoutVersion { get; private set; } = "";
            public Dictionary<string, Rect> Positions { get; } = new();
            public event Action<FsmNode>? NodeClicked;
            private const string Global = "\0global";
            public GraphCanvas()
            {
                MouseLeftButtonDown += (_, e) =>
                {
                    var point = e.GetPosition(this);
                    var name = Positions.FirstOrDefault(p => p.Value.Contains(point)).Key;
                    var node = Card?.Graph?.Nodes.FirstOrDefault(n => n.Name == name);
                    if (node != null) NodeClicked?.Invoke(node);
                };
            }
            public void Update(FsmCard card)
            {
                if (GraphVersion != card.Version)
                {
                    GraphVersion = card.Version;
                    var topology = card.Graph == null ? "" : System.Text.Json.JsonSerializer.Serialize(new {
                        card.Graph.Start, card.Graph.Globals, Nodes = card.Graph.Nodes.Select(n => new { n.Name, n.Edges }) });
                    if (topology != LayoutVersion) { LayoutVersion = topology; LayoutGraph(card.Graph); }
                }
                Card = card; InvalidateVisual();
            }
            private void LayoutGraph(FsmGraph? graph)
            {
                Positions.Clear(); if (graph == null) { Width = 400; Height = 150; return; }
                var nodes = graph.Nodes.GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.First());
                var depth = new Dictionary<string, int>(); var queue = new Queue<string>();
                void Add(string name, int level) { if (nodes.ContainsKey(name) && !depth.ContainsKey(name)) { depth.Add(name, level); queue.Enqueue(name); } }
                Add(graph.Start, 0);
                foreach (var edge in graph.Globals) Add(edge.Target, 0);
                void Drain() { while (queue.Count > 0) { var name = queue.Dequeue(); foreach (var edge in nodes[name].Edges) Add(edge.Target, depth[name] + 1); } }
                Drain(); foreach (var name in nodes.Keys) { Add(name, 0); Drain(); }
                foreach (var group in depth.GroupBy(p => p.Value).OrderBy(g => g.Key))
                {
                    int row = 0;
                    foreach (var item in group) Positions[item.Key] = new Rect(50 + group.Key * 310, 130 + row++ * 110, 190, 56);
                }
                if (graph.Globals.Length > 0) Positions[Global] = new Rect(50, 25, 190, 48);
                Width = Math.Max(420, Positions.Values.Select(r => r.Right + 90).DefaultIfEmpty(420).Max());
                Height = Math.Max(180, Positions.Values.Select(r => r.Bottom + 80).DefaultIfEmpty(180).Max());
            }
            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 26, 32)), null, new Rect(0, 0, ActualWidth, ActualHeight));
                var graph = Card?.Graph; if (graph == null) { Text(dc, UiText.T("等待状态图…"), new Point(20, 20), Brushes.Silver, 300); return; }
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(106, 130, 158)), 1.4);
                void Edge(string from, FsmEdge edge, int index)
                {
                    if (!Positions.TryGetValue(from, out var a) || !Positions.TryGetValue(edge.Target, out var b)) return;
                    var start = new Point(a.Right, a.Top + 14 + index % 3 * 12);
                    var end = new Point(b.Left, b.Top + 28);
                    Point p1, p2;
                    if (a == b) { end = new Point(a.Right - 25, a.Top); p1 = new Point(a.Right + 65, a.Top - 50); p2 = new Point(a.Right - 25, a.Top - 50); }
                    else if (b.Left <= a.Left) { p1 = new Point(a.Right + 70, a.Bottom + 45 + index * 6); p2 = new Point(b.Left - 65, b.Bottom + 45 + index * 6); }
                    else { p1 = new Point(start.X + 60, start.Y); p2 = new Point(end.X - 60, end.Y); }
                    var geometry = new StreamGeometry(); using (var ctx = geometry.Open()) { ctx.BeginFigure(start, false, false); ctx.BezierTo(p1, p2, end, true, false); }
                    dc.DrawGeometry(null, pen, geometry);
                    var direction = end - p2; if (direction.Length > 0) direction.Normalize(); var cross = new Vector(-direction.Y, direction.X);
                    dc.DrawLine(pen, end, end - direction * 10 + cross * 4); dc.DrawLine(pen, end, end - direction * 10 - cross * 4);
                    var middle = new Point((start.X + 3 * p1.X + 3 * p2.X + end.X) / 8, (start.Y + 3 * p1.Y + 3 * p2.Y + end.Y) / 8);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(225, 22, 26, 32)), null, new Rect(middle.X - 45, middle.Y - 9, 100, 18));
                    Text(dc, edge.Event, new Point(middle.X - 43, middle.Y - 8), Brushes.LightSteelBlue, 100, 10);
                }
                foreach (var node in graph.Nodes) for (int i = 0; i < node.Edges.Length; i++) Edge(node.Name, node.Edges[i], i);
                for (int i = 0; i < graph.Globals.Length; i++) Edge(Global, graph.Globals[i], i);
                foreach (var pair in Positions)
                {
                    bool active = Card!.Live && pair.Key == Card.Current;
                    dc.DrawRoundedRectangle(active ? new SolidColorBrush(Color.FromRgb(22, 114, 101)) : new SolidColorBrush(Color.FromRgb(44, 51, 63)),
                        new Pen(active ? Brushes.Aquamarine : Brushes.SlateGray, active ? 3 : 1), pair.Value, 6, 6);
                    Text(dc, pair.Key == Global ? UiText.T("全局转移") : pair.Key, new Point(pair.Value.X + 9, pair.Value.Y + 7), Brushes.White, 173);
                    string label = active ? UiText.T("当前") : pair.Key == graph.Start ? UiText.T("起始") : "";
                    Text(dc, label, new Point(pair.Value.X + 9, pair.Value.Y + 32), Brushes.Aquamarine, 173, 11);
                }
            }
            private void Text(DrawingContext dc, string value, Point point, Brush brush, double width, double size = 13)
            {
                var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = width, MaxTextHeight = size + 5, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(text, point);
            }
        }
    }
}
