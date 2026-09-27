using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class InfoOverlayWindowTests
    {
        [TestMethod]
        public void OverlayRendersClampsAllAnchorsAndOnlyAcceptsMouseWhileAdjusting()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new InfoOverlayWindow();
                    var handle = new WindowInteropHelper(window).EnsureHandle();
                    Assert.AreNotEqual(0L, GetWindowLongPtr(handle, -20).ToInt64() & 0x20L);
                    window.SetAdjusting(true);
                    Assert.AreEqual(0L, GetWindowLongPtr(handle, -20).ToInt64() & 0x20L);
                    Assert.AreNotEqual(0L, GetWindowLongPtr(handle, -20).ToInt64() & 0x08000000L);
                    window.SetAdjusting(false);
                    Assert.AreNotEqual(0L, GetWindowLongPtr(handle, -20).ToInt64() & 0x20L);
                    var canvas = (Canvas)window.Content;
                    var settings = InfoOverlaySettings.Defaults();
                    var values = InfoOverlayModel.Decode("{\"schemaVersion\":1,\"values\":{\"frame\":1256,\"room\":\"GG_Workshop\",\"x\":24.382,\"y\":8.15,\"vx\":0,\"vy\":-12.4,\"dash\":0,\"shade\":0.82,\"health\":5,\"maxHealth\":9,\"soul\":66}}");
                    foreach (var anchor in InfoOverlaySettings.Anchors)
                    {
                        canvas.Measure(new Size(800, 450)); canvas.Arrange(new Rect(0, 0, 800, 450));
                        settings.Anchor = anchor; settings.MarginX = 10000; settings.MarginY = 10000;
                        window.Update(settings, values); canvas.UpdateLayout();
                        var panel = (Border)canvas.Children[0];
                        Assert.IsTrue(Canvas.GetLeft(panel) >= 0 && Canvas.GetTop(panel) >= 0);
                        Assert.IsTrue(Canvas.GetLeft(panel) + panel.ActualWidth <= 801);
                        Assert.IsTrue(Canvas.GetTop(panel) + panel.ActualHeight <= 451);
                    }
                    settings.Anchor = "左上"; settings.MarginX = 16; settings.MarginY = 140;
                    window.Update(settings, values); canvas.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(800, 450, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
                    var root = new DirectoryInfo(AppContext.BaseDirectory);
                    while (root != null && !Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
                    var path = Path.Combine(root!.FullName, "artifacts", "info-overlay", "overlay-offline.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(path)) encoder.Save(stream);
                    window.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));
            if (failure != null) throw new AssertFailedException("Overlay WPF test failed: " + failure, failure);
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    }
}
