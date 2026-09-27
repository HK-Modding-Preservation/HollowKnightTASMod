using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;

namespace HollowKnightTAS.Companion
{
    public sealed class RngSeedWindow : Window
    {
        public RngSeedWindow(MainViewModel vm, long frame, int? seed)
        {
            Title = UiText.T("修改当前帧 RNG 种子") + " · " + frame;
            Width = 470; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = UiText.T("在此帧开始前重设一次种子。留空表示不修改 RNG。"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var input = new TextBox { Text = seed?.ToString(CultureInfo.InvariantCulture) ?? "", MaxLength = 12 };
            System.Windows.Automation.AutomationProperties.SetAutomationId(input, "HktasStudio.RngSeed");
            panel.Children.Add(input);
            var error = new TextBlock { Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = UiText.T("确定"), IsDefault = true };
            ok.Click += (_, _) =>
            {
                if (!vm.TrySetGridRngSeed(frame, input.Text)) { error.Text = UiText.T(vm.GridStatus); return; }
                DialogResult = true;
            };
            var clear = new Button { Content = UiText.T("清除种子") };
            clear.Click += (_, _) => { input.Clear(); input.Focus(); };
            buttons.Children.Add(clear); buttons.Children.Add(ok);
            buttons.Children.Add(new Button { Content = UiText.T("取消"), IsCancel = true });
            panel.Children.Add(buttons); Content = panel;
            Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        }
    }
}
