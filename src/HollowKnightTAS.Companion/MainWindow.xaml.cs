using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System;
using System.Linq;
using System.Windows.Media;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnNavigate(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string name } && FindName(name) is TabItem tab)
            {
                tab.Visibility = Visibility.Visible;
                MainTabs.SelectedItem = tab;
                if (name == "InputGridTab" && DataContext is MainViewModel vm) vm.RefreshGridCommand.Execute(null);
            }
        }

        private void OnCloseStudio(object sender, RoutedEventArgs e) => Close();

        private void OnHideTools(object sender, RoutedEventArgs e)
        {
            MainTabs.SelectedItem = InputGridTab;
            foreach (var tab in new[] { ControlTab, AuthoringTab, EventLogTab, DiffTab, CapabilitiesTab, ShortcutSettingsTab })
                tab.Visibility = Visibility.Collapsed;
        }

        private void OnInputGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || InputGrid.SelectedItems.Count == 0) return;
            var rows = InputGrid.SelectedItems.Cast<InputGridRow>().OrderBy(r => r.Tick).ToArray();
            // Selection gaps are not silently converted into edits of unselected rows.
            vm.GridStart = rows[0].Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vm.GridCount = rows.Length == rows[^1].Tick - rows[0].Tick + 1 ? rows.Length.ToString() : "非连续选区";
        }

        private void OnInputGridClick(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || DataContext is not MainViewModel vm) return;
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not DataGridCell)
                element = VisualTreeHelper.GetParent(element);
            if (element is not DataGridCell cell || cell.DataContext is not InputGridRow row) return;
            if (cell.Column.SortMemberPath == "Axes" && e.ClickCount == 2)
            {
                InputGrid.SelectedItem = row;
                vm.GridStart = row.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
                vm.GridCount = "1";
                new AxisEditorWindow(vm, row.Input) { Owner = this }.ShowDialog();
                e.Handled = true;
                return;
            }
            if (!Enum.TryParse<TasAction>(cell.Column.SortMemberPath, out var action)) return;
            if (!InputGrid.SelectedItems.Contains(row)) InputGrid.SelectedItem = row;
            vm.GridAction = action;
            if (vm.ToggleGridCommand.CanExecute(null)) vm.ToggleGridCommand.Execute(null);
            e.Handled = true;
        }

        private void OnEditGridAxes(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            var first = vm.InputRows.FirstOrDefault(r => r.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture) == vm.GridStart);
            new AxisEditorWindow(vm, first?.Input) { Owner = this }.ShowDialog();
        }

        private void OnInputGridKeyDown(object sender, KeyEventArgs e)
        {
            if (OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) { e.Handled = true; return; }
            if (DataContext is not MainViewModel vm) return;
            ICommand? command = Keyboard.Modifiers == ModifierKeys.Control ? e.Key switch
            {
                Key.C => vm.CopyGridCommand, Key.V => vm.PasteGridCommand,
                Key.Z => vm.UndoGridCommand, Key.Y => vm.RedoGridCommand, _ => null
            } : Keyboard.Modifiers == ModifierKeys.None ? e.Key switch
            {
                Key.Insert => vm.InsertGridCommand, Key.Delete => vm.DeleteGridCommand, _ => null
            } : null;
            if (command == null) return;
            e.Handled = true;
            if (!e.IsRepeat && command.CanExecute(null)) command.Execute(null);
        }

        private void OnStudioKeyDown(object sender, KeyEventArgs e)
        {
            if (OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) { e.Handled = true; return; }
            if (DataContext is not MainViewModel viewModel) return;
            var key = e.Key switch
            {
                Key.System => e.SystemKey,
                Key.ImeProcessed => e.ImeProcessedKey,
                Key.DeadCharProcessed => e.DeadCharProcessedKey,
                _ => e.Key
            };
            var quickSlot = StudioHotkeys.QuickSlotIndex(key, Keyboard.Modifiers);
            if (quickSlot >= 0)
            {
                e.Handled = true;
                if (e.IsRepeat) return;
                viewModel.SelectedQuickSlot = quickSlot;
                MainTabs.SelectedItem = SavesTab;
                var slotCommand = Keyboard.Modifiers == ModifierKeys.Shift
                    ? viewModel.SaveQuickSlotCommand : viewModel.LoadQuickSlotCommand;
                if (slotCommand.CanExecute(null)) slotCommand.Execute(null);
                return;
            }
            var editingText = Keyboard.FocusedElement is TextBoxBase or PasswordBox
                || Keyboard.FocusedElement is ComboBox;
            ICommand? command = StudioHotkeys.Resolve(key, Keyboard.Modifiers, editingText,
                viewModel.ConfiguredPause, viewModel.ConfiguredAdvance) switch
            {
                StudioShortcut.OpenMovie => viewModel.OpenMovieCommand,
                StudioShortcut.SaveMovie => viewModel.SaveMovieCommand,
                StudioShortcut.PlayPause => viewModel.TogglePauseCommand,
                StudioShortcut.FrameAdvance => viewModel.StepCommand,
                _ => null
            };
            if (command == null) return;
            e.Handled = true;
            if (!e.IsRepeat && command.CanExecute(null)) command.Execute(null);
        }
    }
}
