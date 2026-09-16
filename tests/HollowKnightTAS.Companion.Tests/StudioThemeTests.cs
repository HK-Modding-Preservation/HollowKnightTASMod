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
                    var app = new App();
                    app.InitializeComponent();
                    var window = new MainWindow();
                    var root = (FrameworkElement)window.Content;
                    var tabs = Find<TabControl>(root).Single();
                    tabs.SelectedIndex = 2;
                    root.Measure(new Size(1150, 820));
                    root.Arrange(new Rect(0, 0, 1150, 820));
                    root.UpdateLayout();
                    Assert.AreEqual(Color.FromRgb(17, 24, 33), ((SolidColorBrush)window.Background).Color);
                    Assert.AreEqual(Color.FromRgb(238, 244, 250), ((SolidColorBrush)window.Foreground).Color);
                    var tab = (TabItem)tabs.SelectedItem;
                    Assert.AreEqual(Color.FromRgb(238, 244, 250), ((SolidColorBrush)tab.Foreground).Color);
                    foreach (var check in Find<CheckBox>(tab))
                        Assert.AreEqual(Color.FromRgb(238, 244, 250), ((SolidColorBrush)check.Foreground).Color);
                    var bitmap = new RenderTargetBitmap(1150, 820, 96, 96, PixelFormats.Pbgra32);
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
            if (failure != null) throw new AssertFailedException("Theme rendering failed.", failure);
        }

        private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T match) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var value in Find<T>(child)) yield return value;
        }
    }
}
