using System;
using HollowKnightTAS.Core.Media;
using Unity.Collections;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Media
{
    /// <summary>Main-thread-only offline mixer and completed backbuffer reader.</summary>
    public sealed class UnityFrameCapture : IDisposable
    {
        private readonly VideoExportFormat format;
        private readonly VideoExportTimeline timeline;
        private bool recordingAudio;
        public int LastAudioSampleFrames { get; private set; }
        public float MaximumAudioPeak { get; private set; }

        public UnityFrameCapture(VideoExportFormat format)
        {
            this.format = format;
            timeline = new VideoExportTimeline(format);
            ValidateConfiguration();
            if (!AudioRenderer.Start()) throw new InvalidOperationException("Unity offline audio capture could not start.");
            recordingAudio = true;
        }

        public void Capture(long frameIndex, out byte[] rgb, out byte[] pcm)
        {
            if (!recordingAudio) throw new ObjectDisposedException(nameof(UnityFrameCapture));
            ValidateConfiguration();
            LastAudioSampleFrames = AudioRenderer.GetSampleCountForCaptureFrame();
            var expectedValues = timeline.AudioValueCountForFrame(frameIndex);
            // The installed player currently reports zero and rendering an explicit buffer
            // produced only zeros in the short probe. Fail closed until an audio source is verified.
            if (LastAudioSampleFrames != expectedValues / format.Channels)
                throw new InvalidOperationException("Offline audio sample mismatch: Unity=" + LastAudioSampleFrames
                    + ", expected=" + expectedValues / format.Channels + ", frame=" + frameIndex + ".");
            using (var buffer = new NativeArray<float>(expectedValues, Allocator.Temp))
            {
                if (!AudioRenderer.Render(buffer)) throw new InvalidOperationException("Unity offline audio render failed.");
                var floats = buffer.ToArray();
                foreach (var sample in floats) MaximumAudioPeak = Math.Max(MaximumAudioPeak, Math.Abs(sample));
                pcm = new byte[checked(floats.Length * sizeof(float))];
                Buffer.BlockCopy(floats, 0, pcm, 0, pcm.Length);
            }

            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) throw new InvalidOperationException("Completed game backbuffer is unavailable.");
            try
            {
                if (texture.width != format.Width || texture.height != format.Height)
                    throw new InvalidOperationException("Game backbuffer dimensions changed during export.");
                var pixels = texture.GetPixels32();
                rgb = new byte[format.VideoFrameBytes];
                for (var i = 0; i < pixels.Length; i++)
                {
                    rgb[i * 3] = pixels[i].r;
                    rgb[i * 3 + 1] = pixels[i].g;
                    rgb[i * 3 + 2] = pixels[i].b;
                }
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        public void Dispose()
        {
            if (!recordingAudio) return;
            recordingAudio = false;
            if (!AudioRenderer.Stop()) throw new InvalidOperationException("Unity offline audio capture could not stop.");
        }

        private void ValidateConfiguration()
        {
            if (Screen.width != format.Width || Screen.height != format.Height)
                throw new InvalidOperationException("Export resolution must match the current game backbuffer.");
            if (AudioSettings.outputSampleRate != format.SampleRate
                || (format.Channels == 2 ? AudioSettings.speakerMode != AudioSpeakerMode.Stereo
                    : AudioSettings.speakerMode != AudioSpeakerMode.Mono))
                throw new InvalidOperationException("Export audio format must match Unity's output configuration.");
            var duration = (double)format.FpsDenominator / format.FpsNumerator;
            if (Math.Abs(Time.captureDeltaTime - duration) > 0.000001)
                throw new InvalidOperationException("Export frame duration mismatch: Unity=" + Time.captureDeltaTime
                    + ", requested=" + duration + ". Capture does not change the clock.");
        }
    }
}
