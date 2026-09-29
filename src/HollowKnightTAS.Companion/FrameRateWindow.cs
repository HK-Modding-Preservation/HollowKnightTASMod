using System.Globalization;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Companion.Services;
using System.Windows;
using System.Windows.Controls;

namespace HollowKnightTAS.Companion
{
    public sealed class FrameRateWindow : Window
    {
        public string Value { get; private set; }
        public FrameRateWindow(string title, string value)
        {
            Title = UiText.T(title); Value = value; Width = 400; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = UiText.T("每秒帧数（1–1000，最多 6 位小数）"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var input = new TextBox { Text = value }; panel.Children.Add(input);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) }; panel.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = UiText.T("确定"), IsDefault = true };
            ok.Click += (_, _) =>
            {
                if (!MovieFrameRate.TryParse(input.Text, out var fps))
                { error.Text = UiText.T("请输入 1–1000 之间的数字，最多 6 位小数。"); return; }
                Value = fps.ToString("0.######", CultureInfo.InvariantCulture); DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(new Button { Content = UiText.T("取消"), IsCancel = true });
            panel.Children.Add(buttons); Content = panel;
            Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        }
    }
}
