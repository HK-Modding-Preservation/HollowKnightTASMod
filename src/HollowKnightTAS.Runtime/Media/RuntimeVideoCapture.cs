using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Control;
using HollowKnightTAS.Core.Media;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Media
{
    public sealed class RuntimeVideoCapture : IDisposable
    {
        private readonly FfmpegVideoEncoder encoder;
        private readonly UnityFrameCapture capture;
        private readonly int maximumFrames;
        private readonly Action<string> log;
        private readonly int framesPerSecond;
        private Task? finalization;
        private volatile string state = "Capturing";
        private volatile string detail = string.Empty;
        private volatile bool cancelled;
        private int lastUnityFrame;
        private bool detached;

        public RuntimeVideoCapture(string ffmpeg, string output, int maximumFrames, Action<string> log)
        {
            if (maximumFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
            this.maximumFrames = maximumFrames;
            this.log = log;
            OperationId = "video-" + Guid.NewGuid().ToString("N");
            var delta = Time.captureDeltaTime;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0 || delta < 1f / 240f)
                throw new InvalidOperationException("No supported stable TAS frame duration: " + delta.ToString("R", CultureInfo.InvariantCulture));
            framesPerSecond = checked((int)Math.Round(1d / delta));
            var format = new VideoExportFormat(Screen.width, Screen.height, framesPerSecond, 1, AudioSettings.outputSampleRate,
                AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2);
            encoder = new FfmpegVideoEncoder(ffmpeg, output, format);
            try { capture = new UnityFrameCapture(format); }
            catch { encoder.Dispose(); throw; }
            // Start is accepted while paused at an already-rendered boundary. Do not recapture it.
            lastUnityFrame = Time.frameCount;
            CompletedFrameBoundarySignal.Reached += OnCompletedFrame;
            log("video capture started: " + OperationId);
        }

        public string OperationId { get; }
        public bool IsActive => state == "Capturing" || state == "Finalizing";

        public void AppendStatus(IDictionary<string, string> fields)
        {
            fields["videoExport.operationId"] = OperationId;
            fields["videoExport.state"] = state;
            fields["videoExport.detail"] = detail;
            fields["videoExport.outputPath"] = encoder.OutputPath;
            fields["videoExport.frames"] = encoder.FrameCount.ToString(CultureInfo.InvariantCulture);
            fields["videoExport.fps"] = framesPerSecond.ToString(CultureInfo.InvariantCulture);
            fields["videoExport.lastAudioSampleFrames"] = capture.LastAudioSampleFrames.ToString(CultureInfo.InvariantCulture);
            fields["videoExport.maximumAudioPeak"] = capture.MaximumAudioPeak.ToString("R", CultureInfo.InvariantCulture);
            fields["videoExport.dspBlockSampleFrames"] = capture.DspBlockSampleFrames.ToString(CultureInfo.InvariantCulture);
        }

        public void Finish()
        {
            if (state != "Capturing") throw new InvalidOperationException("Capture is not accepting frames.");
            Detach();
            state = "Finalizing";
            finalization = Task.Run(() =>
            {
                try
                {
                    encoder.Complete();
                    state = "Completed";
                }
                catch (Exception exception) { detail = exception.Message; state = cancelled ? "Cancelled" : "Failed"; }
                finally
                {
                    try { encoder.Dispose(); }
                    catch (Exception exception) { detail = exception.Message; state = "Failed"; }
                }
            });
        }

        public void Cancel()
        {
            if (!IsActive) return;
            cancelled = true;
            try { Detach(); }
            finally
            {
                encoder.Cancel();
                if (finalization == null) encoder.Dispose();
                state = "Cancelled";
            }
        }

        public void Dispose()
        {
            Cancel();
            if (finalization == null) encoder.Dispose();
        }

        private void OnCompletedFrame()
        {
            if (state != "Capturing" || Time.frameCount == lastUnityFrame) return;
            lastUnityFrame = Time.frameCount;
            try
            {
                capture.Capture(encoder.FrameCount, out var rgb, out var pcm);
                encoder.WriteFrame(rgb, pcm);
                if (encoder.FrameCount >= maximumFrames) Finish();
            }
            catch (Exception exception)
            {
                detail = exception.Message;
                try { Cancel(); }
                catch (Exception cleanup) { detail += " Cleanup: " + cleanup.Message; }
                state = "Failed";
                log("video capture failed: " + detail);
            }
        }

        private void Detach()
        {
            if (detached) return;
            detached = true;
            CompletedFrameBoundarySignal.Reached -= OnCompletedFrame;
            capture.Dispose();
        }
    }
}
