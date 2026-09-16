using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
                MainTabs.SelectedItem = tab;
        }

        private void OnCloseStudio(object sender, RoutedEventArgs e) => Close();

        private void OnStudioKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var editingText = Keyboard.FocusedElement is TextBoxBase or PasswordBox
                || Keyboard.FocusedElement is ComboBox { IsEditable: true };
            ICommand? command = StudioHotkeys.Resolve(key, Keyboard.Modifiers, editingText) switch
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
