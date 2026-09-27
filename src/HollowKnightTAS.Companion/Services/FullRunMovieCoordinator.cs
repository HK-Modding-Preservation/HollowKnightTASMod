using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services
{
    /// <summary>Owns the frame-zero bootstrap and the player's original-save audit.</summary>
    public sealed class FullRunMovieCoordinator
    {
        private readonly StartupBootController boot;
        private readonly string localRoot;
        private StartupBootGate? gate;
        private ProtectedSaveSession? saves;
        private FullRunBootstrapStore? bootstrap;
        private string mode = "Unarmed";
        public InitialSaveSnapshot? SequenceInitialSaves { get; set; }
        public InitialSaveSnapshot? SessionInitialSaves => saves?.InitialSaves;

        public FullRunMovieCoordinator(StartupBootController boot)
        {
            this.boot = boot ?? throw new ArgumentNullException(nameof(boot));
            localRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS");
        }

        public bool IsPending => gate != null && boot.IsPending;
        public bool IsArmed => IsPending && gate?.IsFullRunFinished != true
            && (mode == "Recording" || mode == "Replay");
        public string Mode => IsPending
            ? gate?.IsFullRunFinished == true && mode == "Replay" ? "Completed"
                : gate?.IsFullRunFinished == true ? "Stopped" : mode
            : "Idle";
        public string RunId => saves?.Descriptor.RunId ?? string.Empty;
        public string ShadowRoot => saves?.Descriptor.ShadowRoot ?? string.Empty;
        public StartupBootGate? Gate => gate;
        public bool IsTerminal => IsPending && (gate!.FullRunFaultCode != 0 || gate.IsFullRunFinished);

        // Terminal gates cannot accept Pause. A later Play rebuilds the new draft.
        public async Task PauseForDocumentChangeAsync(CancellationToken cancellationToken)
        {
            if (!IsPending || IsTerminal || gate!.IsWaiting) return;
            try
            {
                var boundary = await PauseAsync(cancellationToken);
                if (boundary.Mode == "Fault" && !IsTerminal)
                    throw new InvalidOperationException(boundary.Error);
            }
            catch (InvalidOperationException) when (IsTerminal) { }
        }
        public System.Collections.Generic.IReadOnlyDictionary<string, string> OriginalHashes => SequenceInitialSaves?.Hashes ?? saves?.InitialSaves.Hashes
            ?? throw new InvalidOperationException("Protected baseline is unavailable.");

        public StartupBootGate PrepareLaunch()
        {
            if (IsPending) throw new InvalidOperationException("A full-run session is already active.");
            var runId = "full-run-" + Guid.NewGuid().ToString("N");
            var originalRoot = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), "AppData", "LocalLow",
                "Team Cherry", "Hollow Knight");
            var shadowRoot = Path.Combine(localRoot, "save-shadows", runId);
            var session = ProtectedSaveSession.Prepare(runId, originalRoot, shadowRoot, SequenceInitialSaves);
            var nextGate = boot.BeginV2();
            nextGate.SetProtectedSaveSession(session);
            saves = session;
            gate = nextGate;
            bootstrap = new FullRunBootstrapStore(Path.Combine(localRoot,
                "full-run-bootstrap"));
            mode = "Unarmed";
            return nextGate;
        }

        public void ArmRecording(bool gameMouseEnabled, int framesPerSecond = 50)
        {
            var readyGate = RequireFrameZero();
            var store = bootstrap ?? throw new InvalidOperationException("Bootstrap store is missing.");
            var descriptor = store.StageRecording(readyGate.Token,
                saves!.Descriptor.RunId, gameMouseEnabled, framesPerSecond);
            readyGate.ArmV2(readyGate.Token, store.CommitAndHash(descriptor));
            mode = "Recording";
        }

        public void ArmReplay(MovieV2Document movie, long pauseAtFrame = -1)
        {
            if (movie == null) throw new ArgumentNullException(nameof(movie));
            if (SequenceInitialSaves != null && saves?.InitialSaves.Id != SequenceInitialSaves.Id)
                throw new InvalidOperationException("Restart the protected game with the sequence's initial saves before replay.");
            var readyGate = RequireFrameZero();
            var validation = new MovieV2Validator().Validate(movie,
                MovieV2ValidationContext.CreateDefault());
            if (!validation.Success)
                throw new InvalidDataException("The v2 movie is invalid: "
                    + validation.Diagnostics[0].Message);
            var store = bootstrap ?? throw new InvalidOperationException("Bootstrap store is missing.");
            var descriptor = store.StageReplay(readyGate.Token,
                saves!.Descriptor.RunId, movie, pauseAtFrame);
            readyGate.ArmV2(readyGate.Token, store.CommitAndHash(descriptor));
            mode = "Replay";
        }

        public Task<NativeFrameBoundary> StepAsync(long expectedFrame,
            CancellationToken cancellationToken)
        {
            RequireArmed();
            return boot.StepV2Async(expectedFrame, cancellationToken);
        }

        public Task<NativeFrameBoundary> RunAsync(long expectedFrame,
            CancellationToken cancellationToken)
        {
            RequireArmed();
            return boot.RunV2Async(expectedFrame, cancellationToken);
        }

        public Task<NativeFrameBoundary> PauseAsync(CancellationToken cancellationToken)
        {
            RequireArmed();
            return boot.PauseV2Async(cancellationToken);
        }

        public void MarkLiveReplay() => mode = "Replay";

        public void MarkStopped()
        {
            if (mode == "Stopped") return;
            if ((mode != "Recording" && mode != "Replay") || gate == null || !gate.IsWaiting)
                throw new InvalidOperationException("Full-run movie can only stop at a paused native frame.");
            mode = "Stopped";
        }

        public void VerifyOriginalSavesUnchanged()
        {
            var session = saves ?? throw new InvalidOperationException("Protected save session is missing.");
            var original = session.Descriptor.OriginalRoot;
            var current = Directory.Exists(original)
                ? Directory.EnumerateFiles(original, "user*", SearchOption.TopDirectoryOnly)
                    .Where(path => HollowKnightTAS.Core.ReplaySave.ProtectedSaveDescriptor
                        .IsSlotFileName(Path.GetFileName(path)))
                    .Select(Path.GetFileName).ToArray()
                : Array.Empty<string>();
            var expected = session.OriginalSha256.Keys.OrderBy(name => name,
                StringComparer.OrdinalIgnoreCase).ToArray();
            if (!current.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Player save file set changed during the full-run session.");
            foreach (var pair in session.OriginalSha256)
            {
                var path = Path.Combine(original, pair.Key);
                if (new FileInfo(path).Length != session.OriginalLengths[pair.Key]
                    || HollowKnightTAS.Core.Cryptography.Sha256Utility.ComputeFileHex(path)
                        != pair.Value)
                    throw new InvalidDataException("Player save changed during the full-run session: "
                        + pair.Key);
            }
        }

        public void ClearAfterExit()
        {
            gate = null;
            bootstrap = null;
            mode = "Unarmed";
        }

        private StartupBootGate RequireFrameZero()
        {
            if (mode != "Unarmed" || gate == null || !boot.IsWaiting
                || gate.NativeCompletedFrames != 0 || gate.SaveGuardArmed != 1)
                throw new InvalidOperationException("Choose a full-run movie at protected native frame 0.");
            return gate;
        }

        private void RequireArmed()
        {
            if (!IsArmed) throw new InvalidOperationException(
                "New or Open a v2 movie at frame 0 before advancing the game.");
        }
    }
}
