using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HollowKnightTAS.Core.Media;
using HollowKnightTAS.Runtime.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Media
{
    [TestClass]
    public sealed class FfmpegVideoEncoderTests
    {
        [TestMethod]
        public void EncodesSixtyFramesWithStereoAudioAndPublishesOnlyOnCompletion()
        {
            WithEncoderTools((ffmpeg, ffprobe, directory) =>
            {
                var output = Path.Combine(directory, "test video.mp4");
                var format = new VideoExportFormat(1280, 720);
                var timeline = new VideoExportTimeline(format);
                using (var encoder = new FfmpegVideoEncoder(ffmpeg, output, format))
                {
                    for (var frame = 0; frame < 60; frame++)
                    {
                        var rgb = new byte[format.VideoFrameBytes];
                        for (var p = 0; p < rgb.Length; p += 3) rgb[p] = (byte)(frame * 4);
                        var samples = new float[timeline.AudioValueCountForFrame(frame)];
                        for (var i = 0; i < samples.Length; i += 2)
                            samples[i] = samples[i + 1] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * (frame * 800 + i / 2) / 48000));
                        var pcm = new byte[samples.Length * sizeof(float)];
                        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
                        encoder.WriteFrame(rgb, pcm);
                    }
                    Assert.IsFalse(File.Exists(output));
                    encoder.Complete();
                    Assert.AreEqual(60L, encoder.FrameCount);
                }
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo(ffprobe)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    Arguments = "-v error -show_streams -of json \"" + output + "\""
                };
                process.Start();
                var json = process.StandardOutput.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(10000));
                Assert.AreEqual(0, process.ExitCode);
                using var document = JsonDocument.Parse(json);
                var streams = document.RootElement.GetProperty("streams");
                Assert.AreEqual(2, streams.GetArrayLength());
                Assert.AreEqual("h264", streams[0].GetProperty("codec_name").GetString());
                Assert.AreEqual("60", streams[0].GetProperty("nb_frames").GetString());
                Assert.AreEqual("60/1", streams[0].GetProperty("avg_frame_rate").GetString());
                Assert.AreEqual("1.000000", streams[0].GetProperty("duration").GetString());
                Assert.AreEqual("aac", streams[1].GetProperty("codec_name").GetString());
                Assert.AreEqual("48000", streams[1].GetProperty("sample_rate").GetString());
                Assert.AreEqual(2, streams[1].GetProperty("channels").GetInt32());
                Assert.AreEqual("1.000000", streams[1].GetProperty("duration").GetString());
                Assert.AreEqual(1, Directory.GetFileSystemEntries(directory).Length);
            });
        }

        [TestMethod]
        public void CancellationBeforePipeConnectionDoesNotPublishOrLeakTemporaryFiles()
        {
            WithEncoderTools((ffmpeg, _, directory) =>
            {
                var output = Path.Combine(directory, "cancel.mp4");
                using (var encoder = new FfmpegVideoEncoder(ffmpeg, output, new VideoExportFormat(64, 64)))
                    encoder.Cancel();
                Assert.IsFalse(File.Exists(output));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(directory).Length);
            });
        }

        [TestMethod]
        public void OutputCreatedDuringEncodingIsProtectedAndPartialIsRemoved()
        {
            WithEncoderTools((ffmpeg, _, directory) =>
            {
                var output = Path.Combine(directory, "race.mp4");
                var format = new VideoExportFormat(64, 64);
                using (var encoder = new FfmpegVideoEncoder(ffmpeg, output, format))
                {
                    encoder.WriteFrame(new byte[format.VideoFrameBytes], new byte[1600 * sizeof(float)]);
                    File.WriteAllText(output, "created while encoding");
                    Assert.ThrowsExactly<IOException>(() => encoder.Complete());
                }
                Assert.AreEqual("created while encoding", File.ReadAllText(output));
                Assert.AreEqual(1, Directory.GetFileSystemEntries(directory).Length);
            });
        }

        [TestMethod]
        public void ExistingOutputIsNotOverwritten()
        {
            WithEncoderTools((ffmpeg, _, directory) =>
            {
                var output = Path.Combine(directory, "existing.mp4");
                File.WriteAllText(output, "protected");
                Assert.ThrowsExactly<IOException>(() => new FfmpegVideoEncoder(ffmpeg, output, new VideoExportFormat(64, 64)));
                Assert.AreEqual("protected", File.ReadAllText(output));
            });
        }

        [TestMethod]
        public void KilledFfmpegFailsEncodingAndDisposeRemovesTemporaryArtifacts()
        {
            WithEncoderTools((ffmpeg, _, directory) =>
            {
                var output = Path.Combine(directory, "killed.mp4");
                var format = new VideoExportFormat(64, 64);
                var encoder = new FfmpegVideoEncoder(ffmpeg, output, format);
                string temporaryDirectory;
                try
                {
                    var temporaryDirectories = Directory.GetDirectories(directory, ".hktas-export-*");
                    Assert.AreEqual(1, temporaryDirectories.Length);
                    temporaryDirectory = temporaryDirectories[0];

                    var processField = typeof(FfmpegVideoEncoder).GetField(
                        "process", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(processField);
                    var process = (Process)processField!.GetValue(encoder)!;
                    process.Kill();
                    Assert.IsTrue(process.WaitForExit(10000));

                    Assert.ThrowsExactly<InvalidOperationException>(() =>
                        encoder.WriteFrame(new byte[format.VideoFrameBytes], new byte[1600 * sizeof(float)]));
                    Assert.IsFalse(encoder.IsCompleted);
                }
                finally
                {
                    encoder.Dispose();
                }

                Assert.IsFalse(File.Exists(output));
                Assert.IsFalse(Directory.Exists(temporaryDirectory));
            });
        }

        private static void WithEncoderTools(Action<string, string, string> test)
        {
            var ffmpeg = Environment.GetEnvironmentVariable("HKTAS_TEST_FFMPEG");
            var ffprobe = Environment.GetEnvironmentVariable("HKTAS_TEST_FFPROBE");
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(ffmpeg) || string.IsNullOrEmpty(ffprobe))
            {
                Assert.Inconclusive("Set HKTAS_TEST_FFMPEG and HKTAS_TEST_FFPROBE to run the Windows encoder integration checks.");
                return;
            }
            var directory = Path.Combine(Path.GetTempPath(), "hktas-encoder-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { test(ffmpeg, ffprobe, directory); }
            finally
            {
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory); // Refuse to hide unexpected leftover subdirectories.
            }
        }
    }
}
