using System;
using System.Threading;
using System.Threading.Tasks;

namespace HollowKnightTAS.Companion.Services
{
    public enum RecordingRestartPhase { Preparing, ExitingSource, Launching, WaitingForOrigin, Ready, Cancelled, Failed }

    public sealed record RecordingRestartSnapshot(string OperationId, int Slot,
        RecordingRestartPhase Phase, string Detail);

    // The prepared operation must bind one verified source process and validate
    // the selected slot before ExitSourceAsync can have any effect.
    public interface IPreparedRecordingRestart : IDisposable
    {
        Task ExitSourceAsync(CancellationToken cancellationToken);
        Task<IRecordingRestartTarget> LaunchAsync(string operationId, CancellationToken cancellationToken);
    }

    public interface IRecordingRestartTarget : IDisposable
    {
        Task WaitForRecordingOriginAsync(int slot, CancellationToken cancellationToken);
        void ReleaseSupervision();
    }

    public interface IRecordingRestartHost
    {
        Task<IPreparedRecordingRestart> PrepareAsync(int slot, CancellationToken cancellationToken);
    }

    public sealed class RecordingSessionRestartCoordinator
    {
        private readonly object sync = new object();
        private readonly IRecordingRestartHost host;
        private readonly CancellationToken shutdown;
        private Operation? active;
        private RecordingRestartSnapshot? latest;

        public RecordingSessionRestartCoordinator(IRecordingRestartHost host, CancellationToken shutdown = default)
        { this.host = host ?? throw new ArgumentNullException(nameof(host)); this.shutdown = shutdown; }

        public RecordingRestartSnapshot? Latest { get { lock (sync) return latest; } }
        public bool IsActive { get { lock (sync) return active != null; } }

        public string Begin(int slot)
        {
            if (slot < 1 || slot > 4) throw new ArgumentOutOfRangeException(nameof(slot));
            lock (sync)
            {
                shutdown.ThrowIfCancellationRequested();
                if (active != null) throw new InvalidOperationException("A recording restart is already active.");
                var operation = new Operation("restart-" + Guid.NewGuid().ToString("N"), slot, shutdown);
                active = operation;
                SetPhase(operation, RecordingRestartPhase.Preparing, "Validating source and slot.");
                operation.Task = RunAsync(operation);
                return operation.Id;
            }
        }

        public async Task<RecordingRestartSnapshot> CancelAsync(string operationId)
        {
            Task task;
            Operation operation;
            lock (sync)
            {
                operation = active ?? throw new InvalidOperationException("No recording restart is active.");
                if (operation.Id != operationId) throw new InvalidOperationException("Recording restart binding is stale.");
                if (operation.Snapshot?.Phase == RecordingRestartPhase.Ready)
                    throw new InvalidOperationException("The ready recording session must not be cancelled.");
                operation.Cancellation.Cancel();
                task = operation.Task;
            }
            await task;
            lock (sync) return operation.Snapshot!;
        }

        public Task WaitAsync(string operationId)
        {
            lock (sync)
            {
                if (active?.Id == operationId) return active.Task;
                if (latest?.OperationId == operationId) return Task.CompletedTask;
                throw new InvalidOperationException("Recording restart binding is stale.");
            }
        }

        private async Task RunAsync(Operation operation)
        {
            IRecordingRestartTarget? target = null;
            try
            {
                using var prepared = await host.PrepareAsync(operation.Slot, operation.Cancellation.Token);
                lock (sync)
                {
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    SetPhase(operation, RecordingRestartPhase.ExitingSource, "Waiting for the exact source process to exit.");
                }
                // Once exit is submitted, observe its outcome even if the user
                // cancels. Never launch while source exit is ambiguous.
                await prepared.ExitSourceAsync(shutdown);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                SetPhase(operation, RecordingRestartPhase.Launching, "Starting one verified target process.");
                target = await prepared.LaunchAsync(operation.Id, operation.Cancellation.Token);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                SetPhase(operation, RecordingRestartPhase.WaitingForOrigin, "Loading slot and verifying recording origin.");
                await target.WaitForRecordingOriginAsync(operation.Slot, operation.Cancellation.Token);
                lock (sync)
                {
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    target.ReleaseSupervision();
                    SetPhase(operation, RecordingRestartPhase.Ready, "Recording origin is ready.");
                }
            }
            catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
            {
                SetPhase(operation, RecordingRestartPhase.Cancelled, "Recording restart cancelled.");
            }
            catch (Exception exception)
            {
                SetPhase(operation, RecordingRestartPhase.Failed, exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                try { target?.Dispose(); }
                catch (Exception exception)
                { SetPhase(operation, RecordingRestartPhase.Failed, "Target cleanup failed: " + exception.Message); }
                lock (sync)
                {
                    if (ReferenceEquals(active, operation)) active = null;
                    operation.Cancellation.Dispose();
                }
            }
        }

        private void SetPhase(Operation operation, RecordingRestartPhase phase, string detail)
        {
            lock (sync)
            {
                operation.Snapshot = new RecordingRestartSnapshot(operation.Id, operation.Slot, phase, detail);
                latest = operation.Snapshot;
            }
        }

        private sealed class Operation
        {
            public Operation(string id, int slot, CancellationToken shutdown)
            { Id = id; Slot = slot; Cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown); }
            public string Id { get; }
            public int Slot { get; }
            public CancellationTokenSource Cancellation { get; }
            public RecordingRestartSnapshot? Snapshot { get; set; }
            public Task Task { get; set; } = Task.CompletedTask;
        }
    }
}
