using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HollowKnightTAS.Runtime.Media;

namespace HollowKnightTAS.Runtime.FullRun
{
    public sealed partial class RuntimeFullRunSession
    {
        private volatile RuntimeVideoCapture? videoCapture;
        private FullRunVideoExportRange? videoRange;
        public bool IsVideoExportActive => videoCapture?.IsActive == true;

        public string StartVideoExport(string ffmpeg, string output, int maximumFrames,
            bool replayLoadedMovie, long endMovieFrame = -1)
        {
            // The worker must never touch Unity audio or rendering. The existing native
            // boundary queue services this on the Unity thread even while paused.
            return observationQueue.Invoke(frame =>
            {
                if (!clock.IsPaused || !inputReady || mode != "Replay" || replayMovie == null)
                    throw new InvalidOperationException("Pause an input-ready v2 replay before exporting video.");
                if (!replayLoadedMovie)
                    throw new InvalidOperationException("v2 export requires replayLoadedMovie=true.");
                if (IsVideoExportActive) throw new InvalidOperationException("A video export is already active.");
                // Native loading frames run at 50 fps. Resampling variable-rate Movies
                // requires a separate media timeline; fail explicitly instead of changing speed.
                if (replayMovie.Runs.Any(run => run.FramesPerSecond != 50))
                    throw new InvalidOperationException("v2 video export currently requires a constant 50 fps Movie.");
                var range = new FullRunVideoExportRange(frame, movieFrame, replayLength,
                    endMovieFrame, maximumFrames);
                videoCapture?.Dispose();
                videoCapture = new RuntimeVideoCapture(ffmpeg, output, maximumFrames,
                    message => Modding.Logger.Log("TAS v2 " + message),
                    finishAtFrameLimit: false, onFailure: clock.RequestPause,
                    frameDuration: () => clock.StepSeconds, nativeStartFrame: frame);
                videoRange = range;
                // A previously scheduled seek must not interrupt the selected capture range.
                pauseAtMovieFrame = -1;
                return videoCapture.OperationId;
            });
        }

        public string CancelVideoExport(string operationId)
        {
            return observationQueue.Invoke(_ =>
            {
                var capture = videoCapture;
                if (capture == null || capture.OperationId != operationId)
                    throw new InvalidOperationException("Video export operationId does not match.");
                capture.Cancel();
                if (!clock.IsFinished) clock.RequestPause();
                return capture.OperationId;
            });
        }

        public void AppendVideoExportStatus(IDictionary<string, string> fields)
        {
            var capture = videoCapture;
            if (capture == null) return;
            capture.AppendStatus(fields);
            var range = videoRange;
            if (range != null)
            {
                fields["videoExport.startNativeFrame"] = range.StartNativeFrame.ToString(CultureInfo.InvariantCulture);
                fields["videoExport.startMovieFrame"] = range.StartMovieFrame.ToString(CultureInfo.InvariantCulture);
                fields["videoExport.endMovieFrame"] = range.EndMovieFrame.ToString(CultureInfo.InvariantCulture);
            }
            fields["videoExport.boundary"] = "native-post-player-loop";
        }

        private void FailVideoExport(string detail)
        {
            var capture = videoCapture;
            if (capture?.IsActive == true) capture.Fail("Full-run replay failed: " + detail);
        }

        private void CaptureVideoFrame(long completed)
        {
            var capture = videoCapture;
            if (capture?.State != "Capturing") return;
            try
            {
                if (mode == "Failed") { FailVideoExport(error); return; }
                var finished = (videoRange ?? throw new InvalidOperationException("Video range is unavailable."))
                    .CompleteNativeFrame(completed, movieFrame);
                capture.CaptureCompletedFrame(completed);
                // Capture the final rendered input once, then pause without a release frame.
                // The loaded Movie remains intact when the selected end is before its tail.
                if (capture.State == "Capturing" && finished)
                {
                    capture.Finish();
                    if (!clock.IsFinished) clock.RequestPause();
                }
            }
            catch (Exception exception) { capture.Fail(exception.Message); }
        }
    }
}
