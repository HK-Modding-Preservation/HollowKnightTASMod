using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion.Controls
{
    public partial class InfoOverlaySettingsPanel : UserControl
    {
        private Point dragStart;
        private InfoOverlayItem? dragItem;
        private TextBox? expressionEditor;
        private bool insertingCompletion;
        public InfoOverlaySettingsPanel() => InitializeComponent();
        private MainViewModel? Model => DataContext as MainViewModel;
        private InfoOverlayItem? Selected => ItemsList.SelectedItem as InfoOverlayItem;
        private void OnResetExpression(object sender, RoutedEventArgs e)
        { if (Selected != null) Selected.Expression = InfoOverlayModel.DefaultExpression(Selected.Field); }
        private void OnExpressionLoaded(object sender, RoutedEventArgs e)
        {
            if (expressionEditor != null) expressionEditor.TextChanged -= OnExpressionTextChanged;
            expressionEditor = ExpressionBox.Template.FindName("PART_EditableTextBox", ExpressionBox) as TextBox;
            if (expressionEditor != null) expressionEditor.TextChanged += OnExpressionTextChanged;
        }
        private void OnExpressionTextChanged(object sender, TextChangedEventArgs e)
        {
            if (insertingCompletion || expressionEditor?.IsKeyboardFocused != true) return;
            // TextChanged can precede the caret move; refresh once WPF has finished the edit.
            Dispatcher.BeginInvoke(new System.Action(() => ShowCompletion(false)));
        }
        private void ShowCompletion(bool explicitRequest)
        {
            if (expressionEditor?.IsKeyboardFocused != true) { CompletionPopup.IsOpen = false; return; }
            var result = InfoExpressionCompletion.At(expressionEditor.Text, expressionEditor.CaretIndex);
            CompletionList.ItemsSource = result.Matches;
            CompletionList.SelectedIndex = 0;
            CompletionPopup.IsOpen = result.Matches.Length > 0 && (explicitRequest || result.Length > 0);
        }
        private void OnExpressionKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            { ShowCompletion(true); e.Handled = true; return; }
            if (!CompletionPopup.IsOpen) return;
            if (e.Key is Key.Enter or Key.Tab) { InsertCompletion(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CompletionPopup.IsOpen = false; e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down)
            {
                CompletionList.SelectedIndex = System.Math.Clamp(CompletionList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, CompletionList.Items.Count - 1);
                CompletionList.ScrollIntoView(CompletionList.SelectedItem); e.Handled = true;
            }
        }
        private void OnCompletionClick(object sender, MouseButtonEventArgs e)
        { if (Container(e.OriginalSource as DependencyObject) != null) { InsertCompletion(); e.Handled = true; } }
        private void InsertCompletion()
        {
            if (expressionEditor == null || CompletionList.SelectedItem is not string candidate) return;
            var result = InfoExpressionCompletion.At(expressionEditor.Text, expressionEditor.CaretIndex);
            insertingCompletion = true;
            try
            {
                expressionEditor.Select(result.Start, result.Length);
                expressionEditor.SelectedText = candidate;
                expressionEditor.CaretIndex = result.Start + candidate.Length;
                expressionEditor.Focus(); CompletionPopup.IsOpen = false;
            }
            finally { insertingCompletion = false; }
        }
        private void OnAdd(object sender, RoutedEventArgs e) { Model?.AddInfoItem(FieldPicker.SelectedItem as InfoField); ItemsList.SelectedIndex = ItemsList.Items.Count - 1; }
        private void OnAddCustom(object sender, RoutedEventArgs e) { Model?.AddInfoItem(InfoOverlayModel.Fields.Single(f => f.Id == "custom")); ItemsList.SelectedIndex = ItemsList.Items.Count - 1; }
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
