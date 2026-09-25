using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Movie
{
    public sealed class MovieV2Header
    {
        public MovieV2Header(string gameVersion, string apiVersion, string modVersion, string nativeProfileId,
            string actionSchemaId, bool mouseEnabled, string environmentSha256,
            int viewportWidth, int viewportHeight)
        {
            GameVersion = gameVersion ?? throw new ArgumentNullException(nameof(gameVersion));
            ApiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
            ModVersion = modVersion ?? throw new ArgumentNullException(nameof(modVersion));
            NativeProfileId = nativeProfileId ?? throw new ArgumentNullException(nameof(nativeProfileId));
            ActionSchemaId = actionSchemaId ?? throw new ArgumentNullException(nameof(actionSchemaId));
            MouseEnabled = mouseEnabled;
            EnvironmentSha256 = environmentSha256 ?? throw new ArgumentNullException(nameof(environmentSha256));
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
        }

        public int Version => MovieProtocolV2.Version;
        public string Format => MovieProtocolV2.Format;
        public string TickUnit => MovieProtocolV2.TickUnit;
        public string GameVersion { get; }
        public string ApiVersion { get; }
        public string ModVersion { get; }
        public string NativeProfileId { get; }
        public string ActionSchemaId { get; }
        public bool MouseEnabled { get; }
        public string EnvironmentSha256 { get; }
        public int ViewportWidth { get; }
        public int ViewportHeight { get; }
    }

    public sealed class MouseFrameState
    {
        public MouseFrameState(int xQ16, int yQ16, short deltaXQ15, short deltaYQ15,
            uint buttons, short wheelQ15)
        {
            XQ16 = xQ16;
            YQ16 = yQ16;
            DeltaXQ15 = deltaXQ15;
            DeltaYQ15 = deltaYQ15;
            Buttons = buttons;
            WheelQ15 = wheelQ15;
        }

        public int XQ16 { get; }
        public int YQ16 { get; }
        public short DeltaXQ15 { get; }
        public short DeltaYQ15 { get; }
        public uint Buttons { get; }
        public short WheelQ15 { get; }
    }

    public sealed class GameInputSample
    {
        private readonly IReadOnlyList<short> values;

        public GameInputSample(GameInputChannel channel, IReadOnlyList<short> values, MouseFrameState? mouse,
            ulong pressedMask = 0, ulong releasedMask = 0)
        {
            Channel = channel;
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new short[values.Count];
            for (var index = 0; index < copy.Length; index++) copy[index] = values[index];
            this.values = Array.AsReadOnly(copy);
            Mouse = mouse;
            PressedMask = pressedMask;
            ReleasedMask = releasedMask;
        }

        public GameInputChannel Channel { get; }
        public IReadOnlyList<short> Values => values;
        public MouseFrameState? Mouse { get; }
        public ulong PressedMask { get; }
        public ulong ReleasedMask { get; }
    }

    public sealed class NativeFrameRun
    {
        private readonly IReadOnlyList<GameInputSample> samples;

        public NativeFrameRun(long repeatCount, IReadOnlyList<GameInputSample> samples, MovieSourceSpan span, int framesPerSecond = 50, bool authored = false)
        {
            FramesPerSecond = framesPerSecond;
            Authored = authored;
            RepeatCount = repeatCount;
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var copy = new GameInputSample[samples.Count];
            for (var index = 0; index < copy.Length; index++)
                copy[index] = samples[index] ?? throw new ArgumentException("A sample is null.", nameof(samples));
            this.samples = Array.AsReadOnly(copy);
            Span = span;
        }

        public int FramesPerSecond { get; }
        public bool Authored { get; }
        public long RepeatCount { get; }
        public IReadOnlyList<GameInputSample> Samples => samples;
        public MovieSourceSpan Span { get; }
    }

    public sealed class MovieV2Document
    {
        private readonly IReadOnlyList<NativeFrameRun> runs;

        public MovieV2Document(string sourceName, MovieV2Header header, IEnumerable<NativeFrameRun> runs)
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "<movie>" : sourceName;
            Header = header ?? throw new ArgumentNullException(nameof(header));
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            var copy = new List<NativeFrameRun>();
            foreach (var run in runs)
                copy.Add(run ?? throw new ArgumentException("A frame run is null.", nameof(runs)));
            this.runs = Array.AsReadOnly(copy.ToArray());
        }

        public string SourceName { get; }
        public MovieV2Header Header { get; }
        public IReadOnlyList<NativeFrameRun> Runs => runs;
    }

    public sealed class MovieV2ParseResult
    {
        private readonly IReadOnlyList<MovieDiagnostic> diagnostics;

        internal MovieV2ParseResult(MovieV2Document? document, IEnumerable<MovieDiagnostic> diagnostics)
        {
            Document = document;
            this.diagnostics = Array.AsReadOnly(new List<MovieDiagnostic>(diagnostics).ToArray());
        }

        public MovieV2Document? Document { get; }
        public IReadOnlyList<MovieDiagnostic> Diagnostics => diagnostics;
        public bool Success => Document != null && diagnostics.Count == 0;
    }
}
