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
        private volatile bool failed;
        private int lastUnityFrame;
        private bool detached;
        private readonly bool finishAtFrameLimit;
        private readonly Action? onFailure;
        private readonly Action? afterFrame;
        public static bool HideTasOverlays { get; private set; }

        public RuntimeVideoCapture(string ffmpeg, string output, int maximumFrames, Action<string> log,
            bool finishAtFrameLimit = true, Action? onFailure = null, Action? afterFrame = null)
        {
            if (maximumFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
            this.maximumFrames = maximumFrames;
            this.log = log;
            this.finishAtFrameLimit = finishAtFrameLimit;
            this.onFailure = onFailure;
            this.afterFrame = afterFrame;
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
            HideTasOverlays = true;
            log("video capture started: " + OperationId);
        }

        public string OperationId { get; }
        public string State => state;
        public bool IsActive => state == "Capturing" || state == "Finalizing" || state == "Cancelling";

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
                }
                catch (Exception exception) { detail = exception.Message; if (!cancelled) failed = true; }
                finally
                {
                    try { encoder.Dispose(); }
                    catch (Exception exception) { detail = exception.Message; failed = true; }
                    state = failed ? "Failed" : encoder.IsCompleted ? "Completed" : cancelled ? "Cancelled" : "Failed";
                }
            });
        }

        public void Cancel()
        {
            if (!IsActive) return;
            cancelled = true;
            state = "Cancelling";
            try { Detach(); }
            finally
            {
                encoder.Cancel();
                if (finalization == null)
                {
                    encoder.Dispose();
                    state = "Cancelled";
                }
            }
        }

        public void Dispose()
        {
            Cancel();
            if (finalization == null) encoder.Dispose();
        }

        public void Fail(string reason)
        {
            detail = reason;
            failed = true;
            try { Cancel(); }
            catch (Exception cleanup) { detail += " Cleanup: " + cleanup.Message; }
            state = "Failed";
            // Never let a subscriber failure escape the completed-frame gate.
            try { onFailure?.Invoke(); }
            catch (Exception cleanup) { detail += " Playback cleanup: " + cleanup.Message; }
            try { log("video capture failed: " + detail); }
            catch { /* Diagnostics must not break the frame gate. */ }
        }

        private void OnCompletedFrame()
        {
            if (state != "Capturing" || Time.frameCount == lastUnityFrame) return;
            lastUnityFrame = Time.frameCount;
            try
            {
                capture.Capture(encoder.FrameCount, out var rgb, out var pcm);
                encoder.WriteFrame(rgb, pcm);
                afterFrame?.Invoke();
                if (encoder.FrameCount >= maximumFrames)
                {
                    if (finishAtFrameLimit) Finish();
                    else throw new InvalidOperationException("Export frame safety limit reached before replay completion.");
                }
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private void Detach()
        {
            if (detached) return;
            detached = true;
            HideTasOverlays = false;
            CompletedFrameBoundarySignal.Reached -= OnCompletedFrame;
            capture.Dispose();
        }
    }
}
