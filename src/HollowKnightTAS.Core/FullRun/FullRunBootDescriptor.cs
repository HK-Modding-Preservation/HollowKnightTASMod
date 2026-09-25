using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Core.FullRun
{
    public sealed class FullRunBootDescriptor
    {
        public const int MaximumBytes = 4096;
        private const string Format = "hktas-full-run-boot-v1";

        public FullRunBootDescriptor(string gateToken, string runId, string mode,
            bool mouseEnabled, string moviePath, string movieSha256, int framesPerSecond = 50, long pauseAtFrame = -1)
        {
            if (!IsGateToken(gateToken)) throw new ArgumentException("Invalid gate token.", nameof(gateToken));
            if (!ProtectedSaveDescriptor.IsSafeRunId(runId))
                throw new ArgumentException("Invalid run ID.", nameof(runId));
            if (mode != "Record" && mode != "Replay")
                throw new ArgumentException("Mode must be Record or Replay.", nameof(mode));
            if (mode == "Record")
            {
                if (!string.IsNullOrEmpty(moviePath) || !string.IsNullOrEmpty(movieSha256))
                    throw new ArgumentException("Recording cannot carry a replay movie.");
            }
            else if (string.IsNullOrEmpty(moviePath)
                || !Path.IsPathRooted(moviePath)
                || !string.Equals(Path.GetFullPath(moviePath), moviePath,
                    StringComparison.OrdinalIgnoreCase)
                || !MovieProtocolV1.IsLowerSha256(movieSha256))
                throw new ArgumentException("Replay movie path or hash is invalid.");
            if (framesPerSecond < 1 || framesPerSecond > 1000 || pauseAtFrame < -1 || pauseAtFrame > MovieProtocolV2.MaximumExpandedFrames)
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            FramesPerSecond = framesPerSecond;
            PauseAtFrame = pauseAtFrame;
            GateToken = gateToken;
            RunId = runId;
            Mode = mode;
            MouseEnabled = mouseEnabled;
            MoviePath = moviePath;
            MovieSha256 = movieSha256;
        }

        public int FramesPerSecond { get; }
        public long PauseAtFrame { get; }
        public string GateToken { get; }
        public string RunId { get; }
        public string Mode { get; }
        public bool MouseEnabled { get; }
        public string MoviePath { get; }
        public string MovieSha256 { get; }

        public static byte[] Serialize(FullRunBootDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var record = new Record
            {
                FramesPerSecond = descriptor.FramesPerSecond, PauseAtFrame = descriptor.PauseAtFrame,
                Format = Format, GateToken = descriptor.GateToken,
                RunId = descriptor.RunId, Mode = descriptor.Mode,
                MouseEnabled = descriptor.MouseEnabled,
                MoviePath = descriptor.MoviePath, MovieSha256 = descriptor.MovieSha256
            };
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Record)).WriteObject(stream, record);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("Boot descriptor is too large.");
                return stream.ToArray();
            }
        }

        public static FullRunBootDescriptor Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes)
                throw new InvalidDataException("Boot descriptor size is invalid.");
            Record record;
            using (var stream = new MemoryStream(bytes, false))
                record = (Record)(new DataContractJsonSerializer(typeof(Record))
                    .ReadObject(stream) ?? throw new InvalidDataException("Boot descriptor is empty."));
            if (record.Format != Format)
                throw new InvalidDataException("Boot descriptor format is invalid.");
            return new FullRunBootDescriptor(record.GateToken ?? string.Empty,
                record.RunId ?? string.Empty, record.Mode ?? string.Empty,
                record.MouseEnabled, record.MoviePath ?? string.Empty,
                record.MovieSha256 ?? string.Empty, record.FramesPerSecond == 0 ? 50 : record.FramesPerSecond, record.PauseAtFrame ?? -1);
        }

        public static bool IsGateToken(string token)
        {
            if (token == null || token.Length != 32) return false;
            foreach (var character in token)
                if (!(character >= '0' && character <= '9')
                    && !(character >= 'a' && character <= 'f')) return false;
            return true;
        }

        [DataContract]
        private sealed class Record
        {
            [DataMember(Name = "fps", Order = 7)] public int FramesPerSecond { get; set; }
            [DataMember(Name = "pauseAtFrame", Order = 8)] public long? PauseAtFrame { get; set; }
            [DataMember(Name = "format", Order = 0)] public string? Format { get; set; }
            [DataMember(Name = "gateToken", Order = 1)] public string? GateToken { get; set; }
            [DataMember(Name = "runId", Order = 2)] public string? RunId { get; set; }
            [DataMember(Name = "mode", Order = 3)] public string? Mode { get; set; }
            [DataMember(Name = "mouseEnabled", Order = 4)] public bool MouseEnabled { get; set; }
            [DataMember(Name = "moviePath", Order = 5)] public string? MoviePath { get; set; }
            [DataMember(Name = "movieSha256", Order = 6)] public string? MovieSha256 { get; set; }
        }
    }
}
