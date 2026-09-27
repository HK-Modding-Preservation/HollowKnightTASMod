using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Companion.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioThemeTests
    {
        [TestMethod]
        public void AuthoringThemeUsesLightTextOnDarkPanels()
        {
            Exception? failure = null;
            var languagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-language.txt");
            var originalLanguage = File.Exists(languagePath) ? File.ReadAllBytes(languagePath) : null;
            var languageIndex = UiText.Current.LanguageIndex;
            var thread = new Thread(() =>
            {
                try
                {
                    UiText.Current.LanguageIndex = 0;
                    // Reserve space for the native window frame at the 900x600 minimum.
                    const double contentWidth = 880;
                    const double contentHeight = 560;
                    // Use the production theme, but never instantiate App: its dispatcher
                    // startup acquires the real single-instance lock and launches services.
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var theme = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "studio-theme.xaml"));
                    XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    var resources = new XElement(wpf + "ResourceDictionary",
                        new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                        theme.Root!.Element(wpf + "Application.Resources")!.Elements());
                    app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
                    // No registration server or game connection is started. Local settings
                    // may be read by the VM, but no settings/save command is executed.
                    using var sessions = new SessionRegistry("studio-ui-test");
                    using var broker = new AutomationBroker(sessions, automationDirectory:
                        Path.Combine(AppContext.BaseDirectory, "studio-ui-test"));
                    var vm = new MainViewModel(sessions, new MovieEditorService(),
                        new CapabilityBroker(), new NativeHostLauncher(AppContext.BaseDirectory), broker);
                    vm.MovieText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                        "fixtures", "actions-v1.canonical.hktas"));
                    vm.RefreshGridCommand.Execute(null);
                    var window = new MainWindow();
                    window.DataContext = vm;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var follow = Find<CheckBox>(window).Single(box =>
                        System.Windows.Automation.AutomationProperties.GetAutomationId(box) == "HktasStudio.AutoFollowGrid");
                    follow.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)!.UpdateTarget();
                    Assert.IsTrue(follow.IsChecked == true, "Follow playback must be checked when Studio opens.");
                    var insertCount = Find<TextBox>(window).Single(box =>
                        System.Windows.Automation.AutomationProperties.GetAutomationId(box) == "HktasStudio.InsertFrameCount");
                    insertCount.Text = "12";
                    insertCount.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    Assert.AreEqual("12", vm.GridInsertCount);
                    var restoreBar = Find<ProgressBar>(window).Single(bar =>
                        System.Windows.Data.BindingOperations.GetBinding(bar, ProgressBar.ValueProperty)?.Path.Path
                            == nameof(MainViewModel.RestoreProgress));
                    Assert.AreEqual(System.Windows.Data.BindingMode.OneWay,
                        System.Windows.Data.BindingOperations.GetBinding(restoreBar, ProgressBar.ValueProperty)!.Mode);
                    Assert.IsFalse(restoreBar.IsIndeterminate);
                    typeof(MainViewModel).GetMethod("SetRestoreProgress",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, new object[] { 0.5d });
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    restoreBar.GetBindingExpression(ProgressBar.ValueProperty)!.UpdateTarget();
                    Assert.AreEqual(0.5d, restoreBar.Value, "The real window must accept the read-only progress binding and reflect its value.");
                    var bindingUpdate = typeof(MainViewModel).GetMethod("UpdateInputBindingLabels",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    var grid = (DataGrid)window.FindName("InputGrid");
                    var attackColumn = grid.Columns.Single(column => column.SortMemberPath == "Attack");
                    Assert.AreEqual("?", ((TextBlock)attackColumn.Header).Text);
                    bindingUpdate.Invoke(vm, new object[] { new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["inputBinding.Attack"] = "J", ["inputBinding.Jump"] = "Space",
                        ["inputBinding.Left"] = "A", ["inputBinding.Right"] = "D",
                        ["inputBinding.Up"] = "W", ["inputBinding.Down"] = "S",
                        ["inputBinding.Submit"] = "Return", ["inputBinding.Cancel"] = "Escape",
                        ["inputBinding.Dash"] = "K", ["inputBinding.Cast"] = "U",
                        ["inputBinding.QuickCast"] = "I", ["inputBinding.SuperDash"] = "L",
                        ["inputBinding.DreamNail"] = "O"
                    } });
                    Assert.AreEqual("J", ((TextBlock)attackColumn.Header).Text);
                    Assert.AreEqual("Attack", attackColumn.SortMemberPath, "Changing key labels must not change editing semantics.");
                    Assert.IsTrue(((TextBlock)attackColumn.Header).ToolTip.ToString()!.Contains("攻击"));
                    Assert.AreEqual(DataGridLengthUnitType.Pixel, attackColumn.Width.UnitType);
                    var compactWidth = attackColumn.Width.Value;
                    var boundKeys = vm.InputBindingLabels;
                    bindingUpdate.Invoke(vm, new object[] { new System.Collections.Generic.Dictionary<string, string>() });
                    bindingUpdate.Invoke(vm, new object[] { boundKeys.ToDictionary(pair => "inputBinding." + pair.Key, pair => pair.Value) });
                    Assert.AreEqual(compactWidth, attackColumn.Width.Value, "Reconnecting after restore must preserve compact widths.");
                    var root = (FrameworkElement)window.Content;
                    var tabs = Find<TabControl>(root).Single();
                    var visibleTabs = tabs.Items.OfType<TabItem>()
                        .Where(tab => tab.Visibility == Visibility.Visible)
                        .Select(tab => tab.Header?.ToString())
                        .ToArray();
                    Assert.AreEqual(tabs.Items.Count, visibleTabs.Length, "All remaining pages are directly visible.");
                    CollectionAssert.AreEqual(new[] { "输入编辑器", "时间线", "操作手册", "设置" }, visibleTabs);
                    Assert.IsFalse(grid.Columns.Any(c => c.SortMemberPath is "Channels" or "Samples"));
                    Assert.IsFalse(visibleTabs.Contains("Movie Text"));
                    Assert.IsNull(window.FindName("SavesTab"));

                    tabs.SelectedIndex = 0;
                    root.Measure(new Size(contentWidth, contentHeight));
                    root.Arrange(new Rect(0, 0, contentWidth, contentHeight));
                    root.UpdateLayout();
                    Assert.AreEqual(contentWidth - root.Margin.Left - root.Margin.Right, root.ActualWidth, 0.1);
                    Assert.AreEqual(contentHeight - root.Margin.Top - root.Margin.Bottom, root.ActualHeight, 0.1);
                    var inputGrid = (DataGrid)window.FindName("InputGrid");
                    Assert.IsFalse(System.Windows.Input.InputMethod.GetIsInputMethodEnabled(inputGrid),
                        "The input grid handles shortcuts, not IME text composition.");
                    Assert.IsTrue(inputGrid.ActualHeight > 0, "InputGrid must retain usable height at minimum size.");
                    Assert.IsTrue(inputGrid.Columns.OfType<DataGridCheckBoxColumn>().All(column => column.ElementStyle != null),
                        "Input columns must use the dark checkbox style instead of the white system control.");
                    Assert.IsFalse(Find<Menu>(root).Any());

                    var toolbar = LogicalTreeHelper.GetChildren(root).OfType<WrapPanel>().Single();
                    var quitButton = toolbar.Children.OfType<Button>().Single(button =>
                        System.Windows.Automation.AutomationProperties.GetAutomationId(button)
                        == "HktasStudio.QuitGameButton");
                    Assert.AreSame(vm.QuitGameCommand, quitButton.Command);
                    foreach (var button in toolbar.Children.OfType<Button>())
                    {
                        var buttonBounds = button.TransformToAncestor(root)
                            .TransformBounds(new Rect(button.RenderSize));
                        Assert.IsTrue(buttonBounds.Left >= -0.5 && buttonBounds.Top >= -0.5
                            && buttonBounds.Right <= root.ActualWidth + 0.5
                            && buttonBounds.Bottom <= root.ActualHeight + 0.5,
                            $"Toolbar button '{button.Content}' exceeds the content area.");
                    }

                    Assert.AreEqual(Color.FromRgb(17, 24, 33), ((SolidColorBrush)window.Background).Color);
                    Assert.AreEqual(Color.FromRgb(238, 244, 250), ((SolidColorBrush)window.Foreground).Color);
                    var tab = (TabItem)tabs.SelectedItem;
                    Assert.AreEqual(Color.FromRgb(238, 244, 250), ((SolidColorBrush)tab.Foreground).Color);
                    foreach (var check in Find<CheckBox>(tab))
                        Assert.AreEqual(Color.FromRgb(238, 244, 250), ((SolidColorBrush)check.Foreground).Color);
                    var bitmap = new RenderTargetBitmap((int)contentWidth, (int)contentHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "studio-theme-preview.png")))
                        encoder.Save(output);
                    foreach (var scale in new[] { 1.25, 1.5 })
                    {
                        var scaled = new RenderTargetBitmap((int)(contentWidth * scale),
                            (int)(contentHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        scaled.Render(root);
                        var scaledEncoder = new PngBitmapEncoder();
                        scaledEncoder.Frames.Add(BitmapFrame.Create(scaled));
                        using var output = File.Create(Path.Combine(AppContext.BaseDirectory,
                            $"studio-theme-{(int)(scale * 100)}.png"));
                        scaledEncoder.Save(output);
                    }
                    foreach (var name in new[] { "WorldlinesTab", "HelpTab", "ShortcutSettingsTab" })
                    {
                        var commonTab = (TabItem)window.FindName(name);
                        tabs.SelectedItem = commonTab;
                        root.Measure(new Size(contentWidth, contentHeight));
                        root.Arrange(new Rect(0, 0, contentWidth, contentHeight));
                        root.UpdateLayout();
                        foreach (var button in Find<Button>(commonTab))
                        {
                            // Settings intentionally scrolls; verify each control can be brought into view.
                            if (name == "ShortcutSettingsTab")
                            {
                                button.BringIntoView();
                                root.UpdateLayout();
                            }
                            var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                            Assert.IsTrue(bounds.Right <= root.ActualWidth + 0.5
                                && bounds.Bottom <= root.ActualHeight + 0.5 && bounds.Top >= 0,
                                $"{name}: '{button.Content}' is clipped at minimum size.");
                        }
                        if (name == "WorldlinesTab")
                        {
                            var diagnostic = new RenderTargetBitmap(880, 560, 96, 96, PixelFormats.Pbgra32);
                            diagnostic.Render(root);
                            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(diagnostic));
                            using (var image = File.Create(Path.Combine(AppContext.BaseDirectory, "studio-timeline-minimum.png"))) png.Save(image);
                            Assert.IsTrue(Find<ListBox>(commonTab).Single().ActualHeight >= 40,
                                "The timeline node list must remain usable at minimum size; height=" + Find<ListBox>(commonTab).Single().ActualHeight);
                        }
                        else if (name == "HelpTab")
                        {
                            Assert.IsTrue(Find<ScrollViewer>(commonTab).Single().ActualHeight >= 50,
                                "The in-app manual must remain scrollable at minimum size.");
                            Assert.IsTrue(Find<TextBlock>(commonTab).Any(block => block.Text == "快速开始"));
                        }
                        var scaled = new RenderTargetBitmap(1320, 840, 144, 144, PixelFormats.Pbgra32);
                        scaled.Render(root);
                        var preview = new PngBitmapEncoder();
                        preview.Frames.Add(BitmapFrame.Create(scaled));
                        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"studio-{name}-150.png"));
                        preview.Save(output);
                    }
                    // Switch without rebuilding the window or view model, including existing status text.
                    UiText.Current.LanguageIndex = 1;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    CollectionAssert.AreEqual(new[] { "Input Editor", "Timeline", "Manual", "Setting" },
                        tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString()).ToArray());
                    Assert.IsTrue(Find<TextBlock>(root).Any(t => t.Text == "Default FPS"));
                    Assert.IsFalse(Find<TextBlock>(root).Any(t => t.Text.Contains("共 ")));
                    root.UpdateLayout();
                    var englishImage = new RenderTargetBitmap(1320, 840, 144, 144, PixelFormats.Pbgra32);
                    englishImage.Render(root);
                    var englishEncoder = new PngBitmapEncoder(); englishEncoder.Frames.Add(BitmapFrame.Create(englishImage));
                    using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "studio-setting-en.png"))) englishEncoder.Save(file);
                    foreach (var page in tabs.Items.OfType<TabItem>())
                    {
                        tabs.SelectedItem = page;
                        root.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        foreach (var block in Find<TextBlock>(page))
                            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(block.Text, "[\u4e00-\u9fff]"),
                                $"Untranslated English page text: {block.Text}");
                    }
                    UiText.Current.LanguageIndex = 0;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    tabs.SelectedItem = window.FindName("InputGridTab");
                    Assert.AreEqual(tabs.Items.Count, tabs.Items.OfType<TabItem>().Count(item => item.Visibility == Visibility.Visible));
                    vm.GridStart = "0";
                    vm.GridCount = "2";
                    vm.RefreshGridCommand.Execute(null);
                    Assert.AreEqual(2, inputGrid.SelectedItems.Count);
                    vm.GridAction = HollowKnightTAS.Core.Input.TasAction.Attack;
                    vm.ToggleGridCommand.Execute(null);
                    Assert.AreEqual(2, inputGrid.SelectedItems.Count, "Editing must preserve the visible multi-frame selection.");
                    Assert.AreEqual("2", vm.GridCount);
                    vm.UndoGridCommand.Execute(null);
                    Assert.AreEqual(2, inputGrid.SelectedItems.Count, "Undo must also retain the selection.");
                    VerifyAxisDialog(vm);
                    Assert.AreEqual(0, sessions.ConnectedCount, "Offline UI test must not connect to a game.");
                    var rngMovie = new HollowKnightTAS.Core.Movie.MovieV2Document("rng-ui",
                        new HollowKnightTAS.Core.Movie.MovieV2Header("game", "api", "mod",
                            HollowKnightTAS.Core.Movie.MovieProtocolV2.NativeProfileId,
                            HollowKnightTAS.Core.Movie.MovieProtocolV2.ActionSchemaId, false, new string('a', 64), 800, 450),
                        new[] { new HollowKnightTAS.Core.Movie.NativeFrameRun(10,
                            Array.Empty<HollowKnightTAS.Core.Movie.GameInputSample>(),
                            new HollowKnightTAS.Core.Movie.MovieSourceSpan("rng-ui", 1, 1, 1), authored: true) });
                    vm.MovieText = new HollowKnightTAS.Core.Movie.MovieV2Codec().WriteCanonical(rngMovie);
                    vm.RefreshGridCommand.Execute(null);
                    var rngColumn = inputGrid.Columns.OfType<DataGridTextColumn>().Single(c => c.SortMemberPath == "RngSeed");
                    Assert.AreEqual("RngSeed", ((System.Windows.Data.Binding)rngColumn.Binding).Path.Path);
                    var rngDialog = new RngSeedWindow(vm, 4, null);
                    rngDialog.Loaded += (_, _) => rngDialog.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var seedBox = Find<TextBox>(rngDialog).Single();
                        var ok = Find<Button>(rngDialog).Single(b => b.IsDefault);
                        seedBox.Text = "2147483648";
                        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.IsTrue(rngDialog.IsVisible);
                        Assert.IsNull(vm.InputRows[4].RngSeed);
                        seedBox.Text = "0";
                        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    Assert.IsTrue(rngDialog.ShowDialog());
                    Assert.AreEqual(0, vm.InputRows[4].RngSeed);
                    vm.UndoGridCommand.Execute(null); Assert.IsNull(vm.InputRows[4].RngSeed);
                    vm.RedoGridCommand.Execute(null); Assert.AreEqual(0, vm.InputRows[4].RngSeed);
                    window.Close();
                    app.Shutdown();
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    UiText.Current.LanguageIndex = languageIndex;
                    if (originalLanguage == null) File.Delete(languagePath);
                    else File.WriteAllBytes(languagePath, originalLanguage);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Theme rendering timed out.");
            if (failure != null) throw new AssertFailedException("Theme rendering failed: " + failure, failure);
        }

        private static void VerifyAxisDialog(MainViewModel vm)
        {
            vm.GridStart = "0";
            vm.GridCount = "2";
            var original = vm.MovieText;
            var dialog = new AxisEditorWindow(vm, vm.InputRows[0].Input);
            // The opened editor must retain its original selection even if the VM changes.
            vm.GridStart = "10";
            vm.GridCount = "1";
            Exception? error = null;
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    var x = (TextBox)dialog.FindName("AxisX");
                    var y = (TextBox)dialog.FindName("AxisY");
                    ((CheckBox)dialog.FindName("EnabledAxes")).IsChecked = true;
                    x.Text = "10001";
                    y.Text = "-7500";
                    var apply = Find<Button>(dialog).Single(button => button.IsDefault);
                    apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual(original, vm.MovieText, "Rejected axes must not modify the draft.");
                    Assert.IsTrue(dialog.IsVisible, "Invalid input must leave the editor open.");
                    Assert.IsFalse(string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text));
                    x.Text = "2500";
                    apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { error = exception; dialog.Close(); }
            }));
            var accepted = dialog.ShowDialog();
            if (error != null) throw new AssertFailedException("Axis dialog failed: " + error, error);
            Assert.AreEqual(true, accepted);
            Assert.AreEqual("2500, -7500", vm.InputRows[0].Axes);
            Assert.AreEqual("2500, -7500", vm.InputRows[1].Axes);
            Assert.AreEqual("—", vm.InputRows[2].Axes);
            Assert.IsTrue(new MovieEditorService().Validate(vm.MovieText).Success);
            vm.UndoGridCommand.Execute(null);
            Assert.AreEqual(original, vm.MovieText, "Axis edit must use the same draft undo history.");
        }

        private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T match) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var value in Find<T>(child)) yield return value;
        }
    }
}
