using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion.Controls
{
    public partial class FsmViewerPanel : UserControl
    {
        private FsmViewerController? controller;
        private readonly Dictionary<string, (Expander Box, FsmGraphView View)> views = new();
        private string[] remembered = Array.Empty<string>();
        public FsmViewerPanel() => InitializeComponent();
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (controller != null || DataContext is not MainViewModel vm) return;
            controller = vm.CreateFsmViewer(); controller.Updated += Update;
            Objects.ItemsSource = controller.Objects; Targets.ItemsSource = controller.Targets;
            FilterChanged(this, null!);
        }
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (controller == null) return;
            remembered = controller.Targets.Where(t => t.Selected).Select(t => t.StableKey).ToArray();
            controller.Updated -= Update; controller.Dispose(); controller = null;
            Objects.ItemsSource = null; Targets.ItemsSource = null; views.Clear(); Graphs.Children.Clear();
        }
        private void Update()
        {
            if (controller == null) return;
            if (remembered.Length > 0 && controller.Targets.Count > 0)
            {
                foreach (var group in controller.Targets.GroupBy(t => t.StableKey).Where(g => g.Count() == 1))
                    if (remembered.Contains(group.Key)) group.First().Selected = true;
                remembered = Array.Empty<string>();
            }
            Status.Text = controller.Status;
            foreach (var key in views.Keys.Where(k => !controller.Cards.ContainsKey(k)).ToArray()) { Graphs.Children.Remove(views[key].Box); views.Remove(key); }
            foreach (var card in controller.Cards.Values)
            {
                if (!views.TryGetValue(card.Target.Id, out var item))
                {
                    var view = new FsmGraphView(); var box = new Expander { Header = card.Target.Label, IsExpanded = true,
                        Content = view, Margin = new Thickness(4), Padding = new Thickness(4) };
                    item = (box, view); views.Add(card.Target.Id, item); Graphs.Children.Add(box);
                }
                item.View.Update(card);
            }
            Resize();
        }
        private void AddObject(object sender, RoutedEventArgs e) { if (Objects.SelectedItem is FsmObject obj) controller?.SelectObject(obj); }
        private void Refresh(object sender, RoutedEventArgs e) => controller?.Refresh();
        private void Clear(object sender, RoutedEventArgs e) { if (controller != null) foreach (var t in controller.Targets) t.Selected = false; }
        private void SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (controller != null && controller.Targets.Count(t => t.Selected) > 32 && sender is CheckBox box && box.DataContext is FsmSelection target)
            { target.Selected = false; Status.Text = UiText.T("最多同时显示32张状态图。"); }
        }
        private void FilterChanged(object sender, TextChangedEventArgs e)
        {
            if (Targets?.ItemsSource == null) return;
            CollectionViewSource.GetDefaultView(Targets.ItemsSource).Filter = o => o is FsmSelection t &&
                (t.Label + t.Scene).Contains(Filter.Text, StringComparison.OrdinalIgnoreCase);
        }
        private void GraphsSizeChanged(object sender, SizeChangedEventArgs e) => Resize();
        private void Resize()
        { double width = Math.Max(320, Graphs.ActualWidth - 12); if (width > 1050) width = width / 2 - 8; foreach (var item in views.Values) item.Box.Width = width; }
    }
}
