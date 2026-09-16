using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                    var app = new App();
                    app.InitializeComponent();
                    var window = new MainWindow();
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

                    var toolbar = LogicalTreeHelper.GetChildren(root).OfType<WrapPanel>().Single();
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

        private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T match) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var value in Find<T>(child)) yield return value;
        }
    }
}
