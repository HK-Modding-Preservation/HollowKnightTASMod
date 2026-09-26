using System;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;

namespace HollowKnightTAS.Companion.Automation
{
    /// <summary>Compensates an accepted capture when its subsequent native replay start fails.</summary>
    public static class VideoExportStartRecovery
    {
        public static async Task<NativeFrameBoundary> RunAsync(
            Func<Task<NativeFrameBoundary>> startPlayback,
            Func<bool> nativeFaulted,
            Func<CancellationToken, Task> cancelCapture)
        {
            NativeFrameBoundary failure;
            try
            {
                var boundary = await startPlayback();
                if (boundary.Mode != "Fault") return boundary;
                failure = boundary;
            }
            catch (Exception exception)
            {
                failure = new NativeFrameBoundary(-1, -1, "Fault", exception.Message);
            }

            string cleanup;
            if (nativeFaulted())
            {
                // Runtime.Fail closes its capture before setting the native fault.
                // The faulted gate cannot service another Unity-thread request.
                cleanup = "Native gate faulted; main-thread cancellation is unavailable. Restart the session.";
            }
            else
            {
                try
                {
                    // A cancelled client/start token must not cancel compensation too.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await cancelCapture(timeout.Token);
                    cleanup = "Accepted video capture was cancelled.";
                }
                catch (Exception exception)
                {
                    cleanup = "Video cancellation could not be confirmed: " + exception.Message
                        + ". Restart the session before another export.";
                }
            }
            return new NativeFrameBoundary(failure.CompletedFrame, failure.AckSequence,
                "Fault", "Video replay start failed: " + failure.Error + " " + cleanup);
        }
    }
}
