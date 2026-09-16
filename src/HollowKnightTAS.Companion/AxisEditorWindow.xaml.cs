using System.Globalization;
using System.Windows;
using HollowKnightTAS.Companion.ViewModels;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion
{
    public partial class AxisEditorWindow : Window
    {
        private readonly MainViewModel viewModel;
        private readonly string start, count;
        public AxisEditorWindow(MainViewModel viewModel, FrameRunCommand? first)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            start = viewModel.GridStart;
            count = viewModel.GridCount;
            SelectionLabel.Text = $"从帧 {viewModel.GridStart} 起，修改 {viewModel.GridCount} 帧。显示选区首帧的值。";
            EnabledAxes.IsChecked = first?.HasAnalogAxes ?? false;
            AxisX.Text = (first?.AxisX ?? 0).ToString(CultureInfo.InvariantCulture);
            AxisY.Text = (first?.AxisY ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(AxisX.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(AxisY.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            { ErrorText.Text = "请输入整数，范围 -10000…10000。"; return; }
            if (!viewModel.TryEditGridAxes(start, count, EnabledAxes.IsChecked == true, x, y))
            { ErrorText.Text = viewModel.GridStatus; return; }
            DialogResult = true;
        }
    }
}
