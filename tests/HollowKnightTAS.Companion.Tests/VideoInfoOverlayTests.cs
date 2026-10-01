using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Inspector;
using HollowKnightTAS.Core.Media;
using HollowKnightTAS.Runtime.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class VideoInfoOverlayTests
    {
        private static InfoOverlayVideoSettings Settings()
        {
            var settings = InfoOverlaySettings.Defaults(); settings.IncludeInVideo = true;
            settings.Items.Clear(); settings.FontSize = 20; settings.BackgroundOpacity = .8;
            foreach (var id in new[] { "rt", "gt", "healthPair" }) settings.Items.Add(InfoOverlayModel.NewItem(id));
            settings.Items[0].Expression = "rt - 12.5";
            settings.Items[1].Color = "#66FFAA";
            settings.Items[2].Label = "生命 / Health";
            return JsonSerializer.Deserialize<InfoOverlayVideoSettings>(InfoOverlayModel.VideoSettingsJson(settings))!;
        }
        [TestMethod]
        public void CompositorRendersAndClampsEachAnchorWithoutChangingOtherPixels()
        {
            var options = Settings();
            var values = new Dictionary<string, object?> { ["watch:rt - 12.5"] = 1.25, ["gt"] = .82, ["health"] = 5, ["maxHealth"] = 9 };
            foreach (var right in new[] { false, true }) foreach (var bottom in new[] { false, true })
            {
                options.Right = right; options.Bottom = bottom;
                using var renderer = new VideoInfoOverlay(options);
                var pixels = Enumerable.Repeat((byte)40, 640 * 360 * 3).ToArray();
                renderer.Composite(pixels, 640, 360, values);
                Assert.IsTrue(pixels.Any(x => x != 40));
                int opposite = ((bottom ? 359 : 0) * 640 + (right ? 0 : 639)) * 3;
                Assert.AreEqual((byte)40, pixels[opposite]);
                var path = Path.Combine(AppContext.BaseDirectory, $"video-info-{right}-{bottom}.png");
                SaveRgb(path, pixels, 640, 360);
            }
            options.MarginX = options.MarginY = 10000;
            using (var renderer = new VideoInfoOverlay(options)) renderer.Composite(new byte[32 * 24 * 3], 32, 24, values);
            options.Rows.Clear();
            using (var renderer = new VideoInfoOverlay(options))
            {
                var untouched = new byte[12]; renderer.Composite(untouched, 2, 2, values);
                CollectionAssert.AreEqual(new byte[12], untouched);
            }
        }
        [TestMethod]
        public void EncodedVideoContainsOverlayAndRetainsFractionalTimelineAndAudio()
        {
            var ffmpeg = Environment.GetEnvironmentVariable("HKTAS_TEST_FFMPEG");
            var ffprobe = Environment.GetEnvironmentVariable("HKTAS_TEST_FFPROBE");
            if (!File.Exists(ffmpeg) || !File.Exists(ffprobe))
            { Assert.Inconclusive("Set HKTAS_TEST_FFMPEG and HKTAS_TEST_FFPROBE."); return; }
            var root = Path.Combine(Path.GetTempPath(), "hktas-info-video-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var settings = Settings(); var format = new VideoExportFormat(640, 360, 50);
                var timer = new InfoSequenceTimer(); var audio = new VariableVideoTimeline();
                var rt = InfoWatchExpression.Parse("rt - 12.5");
                var durations = Enumerable.Repeat(.02, 10).Concat(Enumerable.Repeat(1d / 99.999, 20)).ToArray();
                var output = Path.Combine(root, "overlay.mp4");
                using (var renderer = new VideoInfoOverlay(settings))
                using (var encoder = new FfmpegVideoEncoder(ffmpeg!, output, format))
                {
                    for (int i = 0; i < durations.Length; i++)
                    {
                        timer.CompleteFrame(durations[i], i < 5);
                        var values = new Dictionary<string, object?> { ["gt"] = timer.GameSeconds, ["health"] = 5, ["maxHealth"] = 9,
                            ["watch:rt - 12.5"] = rt.Evaluate(_ => null, _ => timer.RealSeconds + 12.5) };
                        var pixels = Enumerable.Repeat((byte)40, format.VideoFrameBytes).ToArray();
                        renderer.Composite(pixels, 640, 360, values);
                        encoder.WriteFrame(pixels, new byte[audio.Advance(durations[i], 48000, 2) * 4], durations[i]);
                    }
                    encoder.Complete(); Assert.AreEqual(30L, encoder.FrameCount);
                }
                using var metadata = JsonDocument.Parse(Run(ffprobe!, "-v", "error", "-show_streams", "-show_packets", "-of", "json", output));
                var packets = metadata.RootElement.GetProperty("packets").EnumerateArray().Where(p => p.GetProperty("codec_type").GetString() == "video").ToArray();
                Assert.AreEqual(durations.Length, packets.Length);
                double elapsed = 0;
                for (int i = 0; i < packets.Length; i++)
                {
                    Assert.AreEqual(elapsed, double.Parse(packets[i].GetProperty("pts_time").GetString()!, CultureInfo.InvariantCulture), .000002);
                    elapsed += durations[i];
                }
                foreach (var stream in metadata.RootElement.GetProperty("streams").EnumerateArray())
                    Assert.AreEqual(timer.RealSeconds, double.Parse(stream.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), .001);
                string decoded = Path.Combine(root, "decoded.rgb");
                Run(ffmpeg!, "-v", "error", "-i", output, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", decoded);
                var pixelsDecoded = File.ReadAllBytes(decoded); Assert.AreEqual(format.VideoFrameBytes, pixelsDecoded.Length);
                Assert.IsTrue(pixelsDecoded.Max() > 180, "The encoded and decoded frame must contain bright information text.");
                Assert.AreEqual(40, (int)pixelsDecoded[(359 * 640) * 3], 5, "Opposite corner remains unchanged.");
                Run(ffmpeg!, "-v", "error", "-i", output, "-f", "null", "-");
                File.Copy(output, Path.Combine(AppContext.BaseDirectory, "video-info-synthetic.mp4"), true);
                // Disabled export has no compositor; prove its decoded image contains no text.
                var plain = Path.Combine(root, "plain.mp4");
                using (var encoder = new FfmpegVideoEncoder(ffmpeg!, plain, format))
                {
                    encoder.WriteFrame(Enumerable.Repeat((byte)40, format.VideoFrameBytes).ToArray(), new byte[1920 * 4], .02);
                    encoder.Complete();
                }
                var plainRgb = Path.Combine(root, "plain.rgb");
                Run(ffmpeg!, "-v", "error", "-i", plain, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", plainRgb);
                Assert.IsTrue(File.ReadAllBytes(plainRgb).All(v => Math.Abs(v - 40) <= 5));
            }
            finally
            {
                foreach (var file in Directory.GetFiles(root)) File.Delete(file);
                Directory.Delete(root);
            }
        }
        private static string Run(string executable, params string[] args)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20000)) { process.Kill(); Assert.Fail("Encoder tool timed out."); }
            Assert.AreEqual(0, process.ExitCode, error.GetAwaiter().GetResult()); return output.GetAwaiter().GetResult();
        }
        private static void SaveRgb(string path, byte[] rgb, int width, int height)
        {
            using var bitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int offset = ((height - 1 - y) * width + x) * 3;
                bitmap.SetPixel(x, y, Color.FromArgb(rgb[offset], rgb[offset + 1], rgb[offset + 2]));
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
