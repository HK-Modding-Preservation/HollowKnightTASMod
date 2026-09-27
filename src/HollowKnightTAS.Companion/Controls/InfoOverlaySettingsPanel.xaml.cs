using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion.Controls
{
    public partial class InfoOverlaySettingsPanel : UserControl
    {
        private Point dragStart;
        private InfoOverlayItem? dragItem;
        public InfoOverlaySettingsPanel() => InitializeComponent();
        private MainViewModel? Model => DataContext as MainViewModel;
        private InfoOverlayItem? Selected => ItemsList.SelectedItem as InfoOverlayItem;
        private void OnAdd(object sender, RoutedEventArgs e) { Model?.AddInfoItem(FieldPicker.SelectedItem as InfoField); ItemsList.SelectedIndex = ItemsList.Items.Count - 1; }
        private void OnRemove(object sender, RoutedEventArgs e) => Model?.RemoveInfoItem(Selected);
        private void OnUp(object sender, RoutedEventArgs e) => Model?.MoveInfoItem(Selected, ItemsList.SelectedIndex - 1);
        private void OnDown(object sender, RoutedEventArgs e) => Model?.MoveInfoItem(Selected, ItemsList.SelectedIndex + 1);
        private void OnReset(object sender, RoutedEventArgs e) { Model?.ResetInfoSettings(); ItemsList.SelectedIndex = 0; }
        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            dragStart = e.GetPosition(ItemsList);
            dragItem = Container(e.OriginalSource as DependencyObject)?.DataContext as InfoOverlayItem;
        }
        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragItem == null) return;
            var delta = e.GetPosition(ItemsList) - dragStart;
            if (System.Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && System.Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var item = dragItem; dragItem = null;
            DragDrop.DoDragDrop(ItemsList, item, DragDropEffects.Move);
        }
        private void OnDrop(object sender, DragEventArgs e)
        {
            var item = e.Data.GetData(typeof(InfoOverlayItem)) as InfoOverlayItem;
            var target = Container(e.OriginalSource as DependencyObject)?.DataContext as InfoOverlayItem;
            if (target != null && item != null && Model?.InfoSettings.Items.Contains(item) == true)
            { Model.MoveInfoItem(item, Model.InfoSettings.Items.IndexOf(target)); ItemsList.SelectedItem = item; e.Handled = true; }
        }
        private static ListBoxItem? Container(DependencyObject? value)
        {
            while (value != null && value is not ListBoxItem) value = VisualTreeHelper.GetParent(value);
            return value as ListBoxItem;
        }
    }
}
