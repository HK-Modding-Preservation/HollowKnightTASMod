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
        private long videoStartNativeFrame;
        private long videoStartMovieFrame;
        public bool IsVideoExportActive => videoCapture?.IsActive == true;

        public string StartVideoExport(string ffmpeg, string output, int maximumFrames, bool replayLoadedMovie)
        {
            // The worker must never touch Unity audio or rendering. The existing native
            // boundary queue services this on the Unity thread even while paused.
            return observationQueue.Invoke(frame =>
            {
                if (!clock.IsPaused || !inputReady || mode != "Replay" || replayMovie == null)
                    throw new InvalidOperationException("Pause an input-ready v2 replay before exporting video.");
                if (!replayLoadedMovie)
                    throw new InvalidOperationException("v2 export requires replayLoadedMovie=true and finishes at the Movie end.");
                if (IsVideoExportActive) throw new InvalidOperationException("A video export is already active.");
                // Native loading frames run at 50 fps. Resampling variable-rate Movies
                // requires a separate media timeline; fail explicitly instead of changing speed.
                if (replayMovie.Runs.Any(run => run.FramesPerSecond != 50))
                    throw new InvalidOperationException("v2 video export currently requires a constant 50 fps Movie.");
                if (movieFrame >= replayLength || maximumFrames <= replayLength - movieFrame)
                    throw new InvalidOperationException("Video safety limit must exceed the remaining input count to allow loading frames.");
                videoCapture?.Dispose();
                videoStartNativeFrame = frame;
                videoStartMovieFrame = movieFrame;
                videoCapture = new RuntimeVideoCapture(ffmpeg, output, maximumFrames,
                    message => Modding.Logger.Log("TAS v2 " + message),
                    finishAtFrameLimit: false, onFailure: clock.RequestPause,
                    frameDuration: () => clock.StepSeconds, nativeStartFrame: frame);
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
            fields["videoExport.startNativeFrame"] = videoStartNativeFrame.ToString(CultureInfo.InvariantCulture);
            fields["videoExport.startMovieFrame"] = videoStartMovieFrame.ToString(CultureInfo.InvariantCulture);
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
                capture.CaptureCompletedFrame(completed);
                // Editable replays pause at the end while fixed replays become Completed.
                // Both stop after encoding the last rendered input, without a release frame.
                if (capture.State == "Capturing" && movieFrame == replayLength)
                    capture.Finish();
            }
            catch (Exception exception) { capture.Fail(exception.Message); }
        }
    }
}
