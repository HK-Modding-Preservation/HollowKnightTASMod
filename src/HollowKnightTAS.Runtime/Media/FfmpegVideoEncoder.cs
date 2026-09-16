using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Media;
using HollowKnightTAS.Runtime.Ipc;

namespace HollowKnightTAS.Runtime.Media
{
    /// <summary>Bounded, lossless transport to an external encoder. Does not own the game clock.</summary>
    public sealed class FfmpegVideoEncoder : IDisposable
    {
        private readonly VideoExportTimeline timeline;
        private readonly BlockingCollection<byte[]> video = new BlockingCollection<byte[]>(2);
        private readonly BlockingCollection<byte[]> audio = new BlockingCollection<byte[]>(2);
        private readonly CancellationTokenSource stopped = new CancellationTokenSource();
        private readonly NativeNamedPipeServer videoPipe;
        private readonly NativeNamedPipeServer audioPipe;
        private readonly Process process;
        private readonly Task videoWriter;
        private readonly Task audioWriter;
        private readonly string temporaryDirectory;
        private readonly string temporaryOutput;
        private readonly StringBuilder diagnostics = new StringBuilder();
        private bool disposed;
        private bool completed;
        private readonly object publicationGate = new object();
        public bool IsCompleted { get { lock (publicationGate) return completed; } }

        public FfmpegVideoEncoder(string ffmpegPath, string outputPath, VideoExportFormat format)
        {
            timeline = new VideoExportTimeline(format);
            if (!Path.IsPathRooted(ffmpegPath) || !File.Exists(ffmpegPath))
                throw new FileNotFoundException("Select an existing FFmpeg executable.", ffmpegPath);
            if (!Path.IsPathRooted(outputPath) || !string.Equals(Path.GetExtension(outputPath), ".mp4", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An absolute .mp4 output path is required.", nameof(outputPath));
            OutputPath = Path.GetFullPath(outputPath);
            if (File.Exists(OutputPath) || Directory.Exists(OutputPath))
                throw new IOException("Output already exists: " + OutputPath);
            var parent = Path.GetDirectoryName(OutputPath)!;
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);

            var id = Guid.NewGuid().ToString("N");
            temporaryDirectory = Path.Combine(parent, ".hktas-export-" + id);
            temporaryOutput = Path.Combine(temporaryDirectory, "partial.mp4");
            var videoName = "hktas-video-" + id;
            var audioName = "hktas-audio-" + id;
            // Unity's bundled Mono does not implement the managed named-pipe server.
            // Use the same local-user-only native transport as the runtime command channel.
            videoPipe = new NativeNamedPipeServer(videoName, PipeDirection.Out);
            audioPipe = new NativeNamedPipeServer(audioName, PipeDirection.Out);
            process = new Process();
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                var fps = format.FpsNumerator.ToString(CultureInfo.InvariantCulture) + "/" + format.FpsDenominator.ToString(CultureInfo.InvariantCulture);
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    // Inputs are generated pipe names; only output needs quoting. No shell is involved.
                    // Both raw formats are fully specified: probing can deadlock bounded dual-input
                    // producers before FFmpeg opens/consumes the other stream.
                    Arguments = "-hide_banner -loglevel warning -nostdin -n -thread_queue_size 4 -nofind_stream_info"
                        + " -f rawvideo -pixel_format rgb24 -video_size " + format.Width + "x" + format.Height
                        + " -framerate " + fps + " -i \\\\.\\pipe\\" + videoName
                        + " -thread_queue_size 4 -nofind_stream_info -f f32le -ar " + format.SampleRate
                        + " -ac " + format.Channels + " -i \\\\.\\pipe\\" + audioName
                        + " -map 0:v:0 -map 1:a:0 -vf vflip -c:v libx264 -preset veryfast -tune zerolatency -crf 18"
                        + " -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -f mp4 " + Quote(temporaryOutput),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    lock (diagnostics)
                    {
                        diagnostics.AppendLine(args.Data);
                        if (diagnostics.Length > 8192) diagnostics.Remove(0, diagnostics.Length - 8192);
                    }
                };
                if (!process.Start()) throw new IOException("FFmpeg did not start.");
                process.BeginErrorReadLine();
                videoWriter = Pump(videoPipe, video);
                audioWriter = Pump(audioPipe, audio);
            }
            catch
            {
                stopped.Cancel();
                videoPipe.Dispose();
                audioPipe.Dispose();
                TryKill();
                process.Dispose();
                CleanupTemporaryOutput();
                throw;
            }
        }

        public string OutputPath { get; }
        public long FrameCount { get; private set; }

        /// <summary>Takes ownership of RGB24 (Unity bottom-up) and interleaved little-endian float PCM arrays.</summary>
        public void WriteFrame(byte[] rgb, byte[] pcm)
        {
            ThrowIfUnavailable();
            if (rgb == null || rgb.Length != timeline.Format.VideoFrameBytes)
                throw new ArgumentException("RGB frame length does not match the export format.", nameof(rgb));
            if (pcm == null || pcm.Length != checked(timeline.AudioValueCountForFrame(FrameCount) * sizeof(float)))
                throw new ArgumentException("PCM length does not match this frame's media time.", nameof(pcm));
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    video.Add(rgb, deadline.Token);
                    audio.Add(pcm, deadline.Token);
                    FrameCount++;
                }
                catch
                {
                    Cancel();
                    throw new IOException("Video export transport failed or stalled. " + ReadDiagnostics());
                }
            }
        }

        public void Complete()
        {
            ThrowIfUnavailable();
            if (FrameCount == 0) throw new InvalidOperationException("Cannot export an empty sequence.");
            try
            {
                video.CompleteAdding();
                audio.CompleteAdding();
                if (!Task.WaitAll(new[] { videoWriter, audioWriter }, TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("FFmpeg input finalization timed out.");
                if (!process.WaitForExit(30000)) throw new TimeoutException("FFmpeg finalization timed out.");
                process.WaitForExit(); // Drain asynchronous stderr after the bounded process wait.
                if (process.ExitCode != 0 || !File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0)
                    throw new IOException("FFmpeg failed: " + ReadDiagnostics());
                lock (publicationGate)
                {
                    stopped.Token.ThrowIfCancellationRequested();
                    File.Move(temporaryOutput, OutputPath); // No overwrite, including files created during export.
                    completed = true;
                }
                Directory.Delete(temporaryDirectory);
            }
            catch
            {
                Cancel();
                throw;
            }
        }

        public void Cancel()
        {
            lock (publicationGate)
            {
                if (disposed || completed) return;
                stopped.Cancel();
                TryKill();
                videoPipe.Dispose();
                audioPipe.Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            disposed = true;
            try { Task.WaitAll(new[] { videoWriter, audioWriter }, TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
            videoPipe.Dispose();
            audioPipe.Dispose();
            process.Dispose();
            CleanupTemporaryOutput();
            // Queues/CTS remain alive if a native pipe write has not unwound yet.
            if (videoWriter.IsCompleted && audioWriter.IsCompleted)
            {
                video.Dispose();
                audio.Dispose();
                stopped.Dispose();
            }
        }

        private Task Pump(NativeNamedPipeServer pipe, BlockingCollection<byte[]> queue)
        {
            return Task.Run(async () =>
            {
                try
                {
                    pipe.WaitForConnection();
                    foreach (var bytes in queue.GetConsumingEnumerable(stopped.Token))
                        await pipe.WriteStream.WriteAsync(bytes, 0, bytes.Length, stopped.Token).ConfigureAwait(false);
                }
                catch
                {
                    stopped.Cancel();
                    throw;
                }
                finally { pipe.Dispose(); }
            });
        }

        private void ThrowIfUnavailable()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FfmpegVideoEncoder));
            if (completed || stopped.IsCancellationRequested || process.HasExited)
                throw new InvalidOperationException("Encoder is not accepting frames. " + ReadDiagnostics());
        }

        private void TryKill()
        {
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
            catch (InvalidOperationException) { }
        }

        private string ReadDiagnostics() { lock (diagnostics) return diagnostics.ToString(); }

        private void CleanupTemporaryOutput()
        {
            if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory);
        }

        private static string Quote(string path)
        {
            if (path.IndexOf('"') >= 0) throw new ArgumentException("Invalid path.", nameof(path));
            return "\"" + path + "\"";
        }
    }
}
