using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.Win32;

namespace HollowKnightTAS.Companion;

public sealed class SequenceSavesWindow : Window
{
    private readonly TextBox editor = new() { AcceptsReturn = true, AcceptsTab = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private bool dirty, loading, saving, applied;

    public SequenceSavesWindow(MainViewModel vm)
    {
        Title = UiText.T("序列存档 · JSON"); Width = 900; Height = 700; MinWidth = 550; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var original = vm.SequenceInitialSaves!;
        var draft = original;
        var files = new ComboBox { ItemsSource = original.Hashes.Keys.OrderBy(n => n).ToArray(), Margin = new Thickness(0, 0, 0, 8) };
        string? selected = null;
        string loadedText = "";
        SaveJsonDocument? document = null;
        var save = new Button { Content = UiText.T("保存修改到序列") };
        var export = new Button { Content = UiText.T("导出存档…") };
        var panel = new DockPanel { Margin = new Thickness(14) };
        var help = new TextBlock { Text = UiText.T("编辑初始存档 JSON；保存修改后，请在主窗口保存为新的序列包。导出包含所有存档文件。"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(help, Dock.Top); panel.Children.Add(help);
        DockPanel.SetDock(files, Dock.Top); panel.Children.Add(files);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(export); buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = UiText.T("关闭"), IsCancel = true });
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        panel.Children.Add(editor); Content = panel;
        System.Windows.Automation.AutomationProperties.SetAutomationId(editor, "HktasStudio.SaveJson");
        System.Windows.Automation.AutomationProperties.SetAutomationId(save, "HktasStudio.ApplySaveJson");
        bool Capture()
        {
            try
            {
                if (dirty && selected != null && document != null && editor.Text != loadedText)
                    draft = SaveJsonDocument.Replace(draft, selected, document.Encode(editor.Text));
                loadedText = editor.Text;
                dirty = false;
                return true;
            }
            catch (Exception ex) { status.Text = UiText.T("JSON 保存失败：") + ex.Message; return false; }
        }
        files.SelectionChanged += (_, _) =>
        {
            if (loading) return;
            if (!Capture())
            {
                loading = true; files.SelectedItem = selected; loading = false;
                return;
            }
            selected = files.SelectedItem as string;
            loading = true;
            try
            {
                document = new SaveJsonDocument(draft.CopyFiles().Single(p => p.Key == selected).Value);
                editor.Text = loadedText = document.Json; editor.IsReadOnly = false;
                status.Text = UiText.T("修改仅作用于序列副本；播放将从起点重新开始。");
            }
            catch (Exception ex)
            {
                document = null; editor.Clear(); editor.IsReadOnly = true;
                status.Text = UiText.T("此文件无法作为 JSON 编辑，仍可原样导出：") + ex.Message;
            }
            finally { loading = false; }
        };
        editor.TextChanged += (_, _) => { if (!loading) dirty = true; };
        save.Click += async (_, _) =>
        {
            if (!Capture()) return;
            if (draft.Id == original.Id) { applied = true; Close(); return; }
            saving = true; panel.IsEnabled = false;
            try { await vm.ApplyEditedInitialSavesAsync(original, draft); applied = true; }
            catch (Exception ex) { status.Text = UiText.T("修改失败：") + ex.Message; }
            finally { saving = false; panel.IsEnabled = true; if (applied) Close(); }
        };
        export.Click += (_, _) =>
        {
            if (!Capture()) return;
            var dialog = new OpenFolderDialog { Title = UiText.T("选择导出位置（将创建新文件夹）") };
            if (dialog.ShowDialog(this) != true) return;
            try { status.Text = UiText.T("存档已导出：") + SaveJsonDocument.Export(draft, dialog.FolderName); }
            catch (Exception ex) { status.Text = UiText.T("导出失败：") + ex.Message; }
        };
        Closing += (_, e) =>
        {
            if (saving) { e.Cancel = true; return; }
            if (!applied && (dirty || draft.Id != original.Id))
                e.Cancel = MessageBox.Show(this, UiText.T("放弃尚未保存到序列的修改？"), Title,
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes;
        };
        files.SelectedIndex = 0;
    }
}
