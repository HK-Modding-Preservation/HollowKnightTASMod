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
            var thread = new Thread(() =>
            {
                try
                {
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
                    var root = (FrameworkElement)window.Content;
                    var tabs = Find<TabControl>(root).Single();
                    var visibleTabs = tabs.Items.OfType<TabItem>()
                        .Where(tab => tab.Visibility == Visibility.Visible)
                        .Select(tab => tab.Header?.ToString())
                        .ToArray();
                    CollectionAssert.AreEquivalent(
                        new[] { "Input Editor", "Movie Text", "Savestates" }, visibleTabs);
                    Assert.IsTrue(tabs.Items.OfType<TabItem>()
                        .Where(tab => !visibleTabs.Contains(tab.Header?.ToString()))
                        .All(tab => tab.Visibility == Visibility.Collapsed),
                        "Advanced and diagnostic tabs should be collapsed by default.");

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
                    var mainMenu = Find<Menu>(root).Single();
                    Assert.AreEqual(Color.FromRgb(24, 32, 43), ((SolidColorBrush)mainMenu.Background).Color);

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
                    foreach (var name in new[] { "MovieTextTab", "SavesTab", "HelpTab" })
                    {
                        var commonTab = (TabItem)window.FindName(name);
                        tabs.SelectedItem = commonTab;
                        root.Measure(new Size(contentWidth, contentHeight));
                        root.Arrange(new Rect(0, 0, contentWidth, contentHeight));
                        root.UpdateLayout();
                        foreach (var button in Find<Button>(commonTab))
                        {
                            var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                            Assert.IsTrue(bounds.Right <= root.ActualWidth + 0.5
                                && bounds.Bottom <= root.ActualHeight + 0.5 && bounds.Top >= 0,
                                $"{name}: '{button.Content}' is clipped at minimum size.");
                        }
                        if (name == "SavesTab")
                            Assert.IsTrue(Find<ListBox>(commonTab).Single().ActualHeight >= 40,
                                "The save catalog must remain usable at minimum size.");
                        else if (name == "MovieTextTab")
                            Assert.IsTrue(Find<TextBox>(commonTab).Single(box => box.AcceptsReturn).ActualHeight >= 50,
                                "The movie editor must remain usable at minimum size.");
                        else
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
                    var menus = Find<MenuItem>(root).ToArray();
                    Assert.AreSame(vm.StepCommand, menus.Single(menu => menu.Header.ToString()!.StartsWith("Frame Advance")).Command);
                    Assert.AreSame(vm.TogglePauseCommand, menus.Single(menu => menu.Header.ToString()!.StartsWith("Play / Pause")).Command);
                    Assert.AreSame(vm.StartVideoExportCommand, menus.Single(menu => menu.Header.ToString()!.StartsWith("Encode to MP4")).Command);
                    foreach (var menu in menus.Where(menu => menu.Tag is string))
                    {
                        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        var target = (TabItem)window.FindName((string)menu.Tag);
                        Assert.AreSame(target, tabs.SelectedItem);
                        Assert.AreEqual(Visibility.Visible, target.Visibility);
                    }
                    menus.Single(menu => menu.Header.ToString()!.StartsWith("Close Tool Panels"))
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.AreSame(window.FindName("InputGridTab"), tabs.SelectedItem);
                    Assert.AreEqual(3, tabs.Items.OfType<TabItem>().Count(item => item.Visibility == Visibility.Visible));
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
                    window.Close();
                    app.Shutdown();
                }
                catch (Exception error) { failure = error; }
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
