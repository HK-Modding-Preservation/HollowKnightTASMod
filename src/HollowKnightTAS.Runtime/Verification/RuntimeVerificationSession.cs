using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Playback;
using HollowKnightTAS.Core.Serialization;
using HollowKnightTAS.Core.State;
using HollowKnightTAS.Core.Verification;
using HollowKnightTAS.Runtime.Playback;
using HollowKnightTAS.Runtime.State;

namespace HollowKnightTAS.Runtime.Verification
{
    public sealed class RuntimeVerificationSession
    {
        private readonly string outputDirectory;
        private readonly string sessionId;
        private readonly string manifestSha256;
        private readonly MilestoneCaptureController captureController;
        private MovieDocument? movie;
        private string? movieId;
        private string? baselineSha256;
        private string? runSignature;
        private bool completed;

        public RuntimeVerificationSession(
            string outputDirectory,
            string sessionId,
            string manifestSha256,
            Func<string>? rngStateSha256 = null)
        {
            this.outputDirectory = Path.GetFullPath(
                outputDirectory
                ?? throw new ArgumentNullException(nameof(outputDirectory)));
            this.sessionId = RequireText(sessionId, nameof(sessionId));
            this.manifestSha256 = RequireText(
                manifestSha256,
                nameof(manifestSha256));
            captureController = new MilestoneCaptureController(
                rngStateSha256);
            Directory.CreateDirectory(this.outputDirectory);
        }

        public string? RunSignature => runSignature;
        public string RunJsonPath =>
            Path.Combine(outputDirectory, "run.json");

        public void Begin(
            MovieDocument value,
            SnapshotCaptureResult baselineCapture)
        {
            RuntimeVerificationEligibility.ThrowIfIneligible();
            if (movie != null)
            {
                throw new InvalidOperationException(
                    "Verification session already began.");
            }

            movie = value ?? throw new ArgumentNullException(nameof(value));
            if (baselineCapture == null || !baselineCapture.Success)
            {
                throw new ArgumentException(
                    "A successful baseline capture is required.",
                    nameof(baselineCapture));
            }

            movieId = new MovieCanonicalWriter().ComputeMovieId(movie);
            baselineSha256 = baselineCapture.Sha256;
            captureController.AddBaseline(
                "baseline",
                0,
                baselineCapture);
        }

        public void ObserveInput(
            ReplayInputObservation observation,
            TickStamp stamp)
        {
            captureController.ObserveInput(observation, stamp);
        }

        public void ObserveEvent(PlaybackEvent playbackEvent)
        {
            captureController.QueueEvent(playbackEvent);
        }

        public void CapturePending(TickStamp stamp)
        {
            captureController.CapturePending(stamp);
        }

        public void Complete(
            long endpointMovieTick,
            SnapshotCaptureResult endpointCapture)
        {
            RuntimeVerificationEligibility.ThrowIfIneligible();
            if (completed)
            {
                return;
            }

            if (movie == null || movieId == null || baselineSha256 == null)
            {
                throw new InvalidOperationException(
                    "Verification session was not started.");
            }

            captureController.AddEndpoint(
                endpointMovieTick,
                endpointCapture);
            var process = Process.GetCurrentProcess();
            var processInstanceId = process.Id.ToString(
                                        CultureInfo.InvariantCulture)
                                    + "-"
                                    + process.StartTime
                                        .ToUniversalTime()
                                        .Ticks
                                        .ToString(
                                            CultureInfo.InvariantCulture);
            var evidence = new RunEvidence(
                sessionId,
                processInstanceId,
                manifestSha256,
                baselineSha256,
                movieId,
                SemanticSnapshotSchemaV1.Version,
                TickLedgerRecordJson.SchemaVersion,
                captureController.Milestones);
            WriteEvidence(evidence);
            runSignature = evidence.RunSignature;
            completed = true;
        }

        private void WriteEvidence(RunEvidence evidence)
        {
            var milestoneDirectory = Path.Combine(
                outputDirectory,
                "milestones");
            Directory.CreateDirectory(milestoneDirectory);
            var snapshotFiles = new List<string>();
            for (var index = 0; index < evidence.Milestones.Count; index++)
            {
                var name = index.ToString("D3", CultureInfo.InvariantCulture)
                           + ".snapshot";
                var relative = "milestones/" + name;
                WriteAtomic(
                    Path.Combine(milestoneDirectory, name),
                    evidence.Milestones[index].SemanticSnapshotBytes);
                snapshotFiles.Add(relative);
            }

            var builder = new StringBuilder(65536);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", 1);
            AppendString(builder, "sessionId", evidence.SessionId);
            AppendString(
                builder,
                "processInstanceId",
                evidence.ProcessInstanceId);
            AppendString(
                builder,
                "manifestSha256",
                evidence.ManifestSha256);
            AppendString(
                builder,
                "baselineSha256",
                evidence.BaselineSha256);
            AppendString(builder, "movieId", evidence.MovieId);
            AppendNumber(
                builder,
                "snapshotSchemaVersion",
                evidence.SnapshotSchemaVersion);
            AppendNumber(
                builder,
                "ledgerSchemaVersion",
                evidence.LedgerSchemaVersion);
            AppendString(
                builder,
                "semanticProjection",
                VerificationSnapshotNormalizer.ProjectionId);
            AppendString(
                builder,
                "runSignature",
                evidence.RunSignature);
            AppendPropertyPrefix(builder, "milestones");
            builder.Append('[');
            for (var index = 0; index < evidence.Milestones.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendMilestone(
                    builder,
                    evidence.Milestones[index],
                    snapshotFiles[index]);
            }

            builder.Append(']');
            builder.Append('}');
            WriteAtomic(
                RunJsonPath,
                new UTF8Encoding(false, true).GetBytes(builder.ToString()));
        }

        private static void AppendMilestone(
            StringBuilder builder,
            MilestoneRecord milestone,
            string snapshotFile)
        {
            builder.Append('{');
            AppendString(builder, "milestoneId", milestone.MilestoneId);
            AppendNumber(builder, "movieTick", milestone.MovieTick);
            AppendPropertyPrefix(builder, "tickStamp");
            AppendStamp(builder, milestone.TickStamp);
            AppendString(builder, "sceneName", milestone.SceneName);
            AppendString(
                builder,
                "semanticSha256",
                milestone.SemanticSha256);
            AppendString(builder, "snapshotFile", snapshotFile);
            AppendPropertyPrefix(builder, "input");
            AppendInput(builder, milestone.Input);
            AppendString(
                builder,
                "ledgerWindowSha256",
                milestone.LedgerWindowSha256);
            AppendString(
                builder,
                "rngStateSha256",
                milestone.RngStateSha256);
            AppendPropertyPrefix(builder, "ledgerWindow");
            builder.Append('[');
            for (var index = 0;
                 index < milestone.LedgerWindow.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var entry = milestone.LedgerWindow[index];
                builder.Append('{');
                AppendNumber(builder, "movieTick", entry.MovieTick);
                AppendPropertyPrefix(builder, "tickStamp");
                AppendStamp(builder, entry.Stamp);
                AppendPropertyPrefix(builder, "input");
                AppendInput(builder, entry.Input);
                AppendNumber(
                    builder,
                    "fixedStepsSincePreviousVisual",
                    entry.FixedStepsSincePreviousVisual);
                AppendNumber(
                    builder,
                    "timeMinusFixedTimeBits",
                    entry.TimeMinusFixedTimeBits);
                AppendString(builder, "sceneName", entry.SceneName);
                AppendString(builder, "eventName", entry.EventName);
                AppendString(
                    builder,
                    "rngStateSha256",
                    entry.RngStateSha256);
                builder.Append('}');
            }

            builder.Append(']');
            builder.Append('}');
        }

        private static void AppendStamp(
            StringBuilder builder,
            TickStamp stamp)
        {
            builder.Append('{');
            AppendUnsigned(builder, "inputTick", stamp.InputTick);
            AppendNumber(builder, "visualTick", stamp.VisualTick);
            AppendNumber(builder, "fixedTick", stamp.FixedTick);
            AppendNumber(builder, "sceneEpoch", stamp.SceneEpoch);
            AppendString(builder, "phase", stamp.Phase.ToString());
            builder.Append('}');
        }

        private static void AppendInput(
            StringBuilder builder,
            InputSample input)
        {
            builder.Append('{');
            AppendUnsigned(builder, "inputTick", input.InputTick);
            AppendNumber(builder, "held", (int)input.Held);
            AppendNumber(builder, "pressed", (int)input.Pressed);
            AppendNumber(builder, "released", (int)input.Released);
            AppendNumber(builder, "axisX", input.AxisX);
            AppendNumber(builder, "axisY", input.AxisY);
            builder.Append('}');
        }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    name);
            }

            return value;
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendUnsigned(
            StringBuilder builder,
            string name,
            ulong value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendPropertyPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            // Hollow Knight runs on Mono with legacy Windows path limits.
            // Use a short sibling rather than appending to the already long
            // destination filename.
            var directory = Path.GetDirectoryName(path)
                            ?? throw new InvalidOperationException(
                                "Evidence path has no containing directory.");
            var temporary = Path.Combine(
                directory,
                ".t-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
