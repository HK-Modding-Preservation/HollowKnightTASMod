using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Services
{
    public enum ColdRestoreStoreStage : byte
    {
        TransactionCreated = 1,
        IntentWritten = 2,
        ClaimWritten = 3,
        GenesisWritten = 4,
        OperationPublished = 5,
        EventWritten = 6
    }

    public sealed class ColdRestoreStoreSimulatedCrashException : Exception
    {
        public ColdRestoreStoreSimulatedCrashException(
            ColdRestoreStoreStage stage)
            : base("Simulated cold-restore store crash at " + stage + ".")
        {
            Stage = stage;
        }

        public ColdRestoreStoreStage Stage { get; }
    }

    public sealed class ColdRestoreOperationSnapshot
    {
        private readonly ReadOnlyCollection<ColdRestoreOperationRecord> records;

        public ColdRestoreOperationSnapshot(
            ColdRestoreIntent intent,
            ColdRestoreClaim claim,
            IEnumerable<ColdRestoreOperationRecord> records)
        {
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            Claim = claim ?? throw new ArgumentNullException(nameof(claim));
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var values = records.ToList();
            if (values.Count == 0)
            {
                throw new ArgumentException(
                    "At least one operation record is required.",
                    nameof(records));
            }

            this.records = new ReadOnlyCollection<ColdRestoreOperationRecord>(
                values);
        }

        public ColdRestoreIntent Intent { get; }
        public ColdRestoreClaim Claim { get; }
        public IReadOnlyList<ColdRestoreOperationRecord> Records => records;
        public ColdRestoreOperationRecord Latest => records[records.Count - 1];
    }

    public sealed class ColdRestoreStoreRecoveryReport
    {
        public ColdRestoreStoreRecoveryReport(
            int readyOperationCount,
            IEnumerable<string> incompleteTransactionIds,
            IEnumerable<string> corruptOperationIds)
        {
            ReadyOperationCount = readyOperationCount;
            IncompleteTransactionIds = new ReadOnlyCollection<string>(
                (incompleteTransactionIds
                 ?? throw new ArgumentNullException(
                     nameof(incompleteTransactionIds)))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList());
            CorruptOperationIds = new ReadOnlyCollection<string>(
                (corruptOperationIds
                 ?? throw new ArgumentNullException(nameof(corruptOperationIds)))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList());
        }

        public int ReadyOperationCount { get; }
        public IReadOnlyList<string> IncompleteTransactionIds { get; }
        public IReadOnlyList<string> CorruptOperationIds { get; }
    }

    public sealed class ColdRestoreIntentStore
    {
        private const int MaximumRecords = 64;

        private readonly object sync = new object();
        private readonly string root;
        private readonly string operationsRoot;
        private readonly string transactionsRoot;
        private readonly string companionInstanceId;
        private readonly byte[] claimSecret;
        private readonly Action<ColdRestoreStoreStage>? afterStage;

        public ColdRestoreIntentStore(
            string root,
            string companionInstanceId,
            byte[] claimSecret,
            Action<ColdRestoreStoreStage>? afterStage = null)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException(
                    "A cold-restore store root is required.",
                    nameof(root));
            }

            this.companionInstanceId =
                ColdRestoreIntent.RequireIdentifier(
                    companionInstanceId,
                    nameof(companionInstanceId));
            if (claimSecret == null)
            {
                throw new ArgumentNullException(nameof(claimSecret));
            }

            if (claimSecret.Length
                < ColdRestoreClaimAuthenticator.MinimumSecretBytes)
            {
                throw new ArgumentException(
                    "Cold-restore claim secret is too short.",
                    nameof(claimSecret));
            }

            this.claimSecret = (byte[])claimSecret.Clone();
            this.afterStage = afterStage;
            this.root = Path.GetFullPath(root);
            operationsRoot = Path.Combine(this.root, "operations");
            transactionsRoot = Path.Combine(this.root, "transactions");
            Directory.CreateDirectory(this.root);
            RestrictDirectoryToCurrentUser(this.root);
            Directory.CreateDirectory(operationsRoot);
            Directory.CreateDirectory(transactionsRoot);
        }

        public ColdRestoreOperationSnapshot Prepare(
            ColdRestoreIntent intent,
            string claimId,
            DateTimeOffset occurredAtUtc,
            DateTimeOffset claimExpiresAtUtc)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            lock (sync)
            {
                var destination = OperationDirectory(intent.OperationId);
                if (Directory.Exists(destination))
                {
                    throw new InvalidOperationException(
                        "Cold-restore operation already exists.");
                }

                var transactionId = intent.OperationId
                                    + "--"
                                    + Guid.NewGuid().ToString("N");
                var transaction = Path.Combine(
                    transactionsRoot,
                    transactionId);
                Directory.CreateDirectory(transaction);
                Notify(ColdRestoreStoreStage.TransactionCreated);

                var intentBytes = ColdRestoreIntentCodec.Serialize(intent);
                WriteDurable(
                    Path.Combine(transaction, "intent.json"),
                    intentBytes);
                Notify(ColdRestoreStoreStage.IntentWritten);

                var claim = ColdRestoreClaimAuthenticator.Issue(
                    intent,
                    claimId,
                    companionInstanceId,
                    occurredAtUtc,
                    claimExpiresAtUtc,
                    claimSecret);
                WriteDurable(
                    Path.Combine(transaction, "claim.json"),
                    ColdRestoreClaimCodec.Serialize(claim));
                Notify(ColdRestoreStoreStage.ClaimWritten);

                var events = Path.Combine(transaction, "events");
                Directory.CreateDirectory(events);
                var genesis = new ColdRestoreOperationRecord(
                    ColdRestoreOperationRecord.CurrentSchemaVersion,
                    intent.OperationId,
                    ColdRestoreIntentCodec.ComputeSha256(intent),
                    claim.ClaimId,
                    0,
                    ColdRestoreOperationState.Prepared,
                    ColdRestoreOperationRecord.GenesisPreviousSha256,
                    occurredAtUtc,
                    ColdRestoreActorRole.Companion,
                    companionInstanceId,
                    string.Empty,
                    intent.SourceProcessId,
                    intent.SourceProcessStartedAtUtc,
                    "prepared");
                WriteDurable(
                    EventPath(events, 0),
                    ColdRestoreOperationRecordCodec.Serialize(genesis));
                Notify(ColdRestoreStoreStage.GenesisWritten);

                Directory.Move(transaction, destination);
                Notify(ColdRestoreStoreStage.OperationPublished);
                return LoadInternal(intent.OperationId);
            }
        }

        public ColdRestoreOperationSnapshot Load(string operationId)
        {
            lock (sync)
            {
                return LoadInternal(operationId);
            }
        }

        public IReadOnlyList<string> ListOperationIds()
        {
            lock (sync)
            {
                return new ReadOnlyCollection<string>(
                    Directory.GetDirectories(operationsRoot)
                        .Select(Path.GetFileName)
                        .Where(value => !string.IsNullOrEmpty(value))
                        .Cast<string>()
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToList());
            }
        }

        public ColdRestoreOperationSnapshot Transition(
            string operationId,
            long expectedSequence,
            ColdRestoreOperationState next,
            DateTimeOffset occurredAtUtc,
            ColdRestoreActorRole actorRole,
            string actorInstanceId,
            string actorSessionId,
            int processId,
            DateTimeOffset processStartedAtUtc,
            string detailCode)
        {
            if (next == ColdRestoreOperationState.Prepared
                || next == ColdRestoreOperationState.IntentClaimed)
            {
                throw new InvalidOperationException(
                    "Prepared and IntentClaimed use dedicated store operations.");
            }

            lock (sync)
            {
                var snapshot = LoadInternal(operationId);
                RequireExpectedSequence(snapshot, expectedSequence);
                RequireActorIdentity(actorRole, actorInstanceId);
                ValidateProcessBinding(
                    snapshot.Intent,
                    next,
                    actorRole,
                    actorSessionId,
                    processId,
                    processStartedAtUtc);
                var record = snapshot.Latest.CreateNext(
                    next,
                    occurredAtUtc,
                    actorRole,
                    actorInstanceId,
                    actorSessionId,
                    processId,
                    processStartedAtUtc,
                    detailCode);
                AppendRecord(record);
                return LoadInternal(operationId);
            }
        }

        public ColdRestoreOperationSnapshot Claim(
            string operationId,
            long expectedSequence,
            string suppliedClaimId,
            string suppliedMacSha256,
            ColdRestoreBuildFingerprint actualBuild,
            DateTimeOffset occurredAtUtc,
            string runtimeInstanceId,
            string newRuntimeSessionId,
            int newProcessId,
            DateTimeOffset newProcessStartedAtUtc)
        {
            lock (sync)
            {
                var snapshot = LoadInternal(operationId);
                RequireExpectedSequence(snapshot, expectedSequence);
                var build = ColdRestoreIntentValidator.ValidateForClaim(
                    snapshot.Intent,
                    actualBuild,
                    occurredAtUtc);
                if (!build.Success)
                {
                    throw new InvalidOperationException(build.Detail);
                }

                var supplied = new ColdRestoreClaim(
                    snapshot.Claim.SchemaVersion,
                    suppliedClaimId,
                    snapshot.Claim.IntentSha256,
                    snapshot.Claim.CompanionInstanceId,
                    snapshot.Claim.SourceSessionId,
                    snapshot.Claim.IssuedAtUtc,
                    snapshot.Claim.ExpiresAtUtc,
                    suppliedMacSha256);
                var authentication = ColdRestoreClaimAuthenticator.Validate(
                    supplied,
                    snapshot.Intent,
                    companionInstanceId,
                    occurredAtUtc,
                    claimSecret);
                if (!authentication.Success
                    || !string.Equals(
                        supplied.ClaimId,
                        snapshot.Claim.ClaimId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        authentication.Success
                            ? "Claim ID does not match the stored claim."
                            : authentication.Detail);
                }

                ValidateProcessBinding(
                    snapshot.Intent,
                    ColdRestoreOperationState.IntentClaimed,
                    ColdRestoreActorRole.Runtime,
                    newRuntimeSessionId,
                    newProcessId,
                    newProcessStartedAtUtc);
                var record = snapshot.Latest.CreateNext(
                    ColdRestoreOperationState.IntentClaimed,
                    occurredAtUtc,
                    ColdRestoreActorRole.Runtime,
                    runtimeInstanceId,
                    newRuntimeSessionId,
                    newProcessId,
                    newProcessStartedAtUtc,
                    "intent-claimed");
                AppendRecord(record);
                return LoadInternal(operationId);
            }
        }

        public ColdRestoreStoreRecoveryReport Recover()
        {
            lock (sync)
            {
                var incomplete = Directory.GetDirectories(transactionsRoot)
                    .Select(Path.GetFileName)
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Cast<string>()
                    .ToList();
                var corrupt = new List<string>();
                var ready = 0;
                foreach (var directory in Directory.GetDirectories(
                             operationsRoot))
                {
                    var operationId = Path.GetFileName(directory);
                    try
                    {
                        LoadInternal(operationId);
                        ready++;
                    }
                    catch (Exception exception) when (
                        exception is IOException
                        || exception is InvalidDataException
                        || exception is InvalidOperationException
                        || exception is UnauthorizedAccessException)
                    {
                        corrupt.Add(operationId);
                    }
                }

                return new ColdRestoreStoreRecoveryReport(
                    ready,
                    incomplete,
                    corrupt);
            }
        }

        private ColdRestoreOperationSnapshot LoadInternal(string operationId)
        {
            operationId = ColdRestoreIntent.RequireIdentifier(
                operationId,
                nameof(operationId));
            var directory = OperationDirectory(operationId);
            if (!Directory.Exists(directory))
            {
                throw new FileNotFoundException(
                    "Cold-restore operation was not found.",
                    operationId);
            }

            var intent = ColdRestoreIntentCodec.Deserialize(
                ReadBounded(
                    Path.Combine(directory, "intent.json"),
                    ColdRestoreIntentCodec.MaximumBytes));
            if (!string.Equals(
                    intent.OperationId,
                    operationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Cold-restore operation path does not match its intent.");
            }

            var claim = ColdRestoreClaimCodec.Deserialize(
                ReadBounded(
                    Path.Combine(directory, "claim.json"),
                    ColdRestoreClaimCodec.MaximumBytes));
            var authentication = ColdRestoreClaimAuthenticator.Validate(
                claim,
                intent,
                companionInstanceId,
                claim.IssuedAtUtc,
                claimSecret);
            if (!authentication.Success)
            {
                throw new InvalidDataException(authentication.Detail);
            }

            var eventDirectory = Path.Combine(directory, "events");
            if (!Directory.Exists(eventDirectory))
            {
                throw new InvalidDataException(
                    "Cold-restore event directory is missing.");
            }

            var files = Directory.GetFiles(eventDirectory, "*.json")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (files.Length == 0 || files.Length > MaximumRecords)
            {
                throw new InvalidDataException(
                    "Cold-restore event count is outside the allowed range.");
            }

            var records = new List<ColdRestoreOperationRecord>(files.Length);
            var intentSha256 = ColdRestoreIntentCodec.ComputeSha256(intent);
            for (var index = 0; index < files.Length; index++)
            {
                var expectedName = index.ToString(
                                       "D20",
                                       CultureInfo.InvariantCulture)
                                   + ".json";
                if (!string.Equals(
                        Path.GetFileName(files[index]),
                        expectedName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Cold-restore event sequence has a gap or alias.");
                }

                var record = ColdRestoreOperationRecordCodec.Deserialize(
                    ReadBounded(
                        files[index],
                        ColdRestoreOperationRecordCodec.MaximumBytes));
                if (record.Sequence != index
                    || !string.Equals(
                        record.OperationId,
                        operationId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        record.IntentSha256,
                        intentSha256,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        record.ClaimId,
                        claim.ClaimId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Cold-restore event binding is invalid.");
                }

                if (index > 0)
                {
                    var previous = records[index - 1];
                    if (!string.Equals(
                            record.PreviousRecordSha256,
                            ColdRestoreOperationRecordCodec.ComputeSha256(
                                previous),
                            StringComparison.Ordinal)
                        || record.OccurredAtUtc < previous.OccurredAtUtc
                        || !ColdRestoreOperationStateMachine.CanTransition(
                            previous.State,
                            record.State))
                    {
                        throw new InvalidDataException(
                            "Cold-restore event chain is invalid.");
                    }
                }

                ValidateProcessBinding(
                    intent,
                    record.State,
                    record.ActorRole,
                    record.ActorSessionId,
                    record.ProcessId,
                    record.ProcessStartedAtUtc);
                records.Add(record);
            }

            return new ColdRestoreOperationSnapshot(intent, claim, records);
        }

        private void AppendRecord(ColdRestoreOperationRecord record)
        {
            var eventDirectory = Path.Combine(
                OperationDirectory(record.OperationId),
                "events");
            var destination = EventPath(eventDirectory, record.Sequence);
            if (File.Exists(destination))
            {
                throw new InvalidOperationException(
                    "Cold-restore event sequence already exists.");
            }

            var temporary = Path.Combine(
                eventDirectory,
                ".event-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteDurable(
                    temporary,
                    ColdRestoreOperationRecordCodec.Serialize(record));
                File.Move(temporary, destination);
                Notify(ColdRestoreStoreStage.EventWritten);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private void RequireActorIdentity(
            ColdRestoreActorRole actorRole,
            string actorInstanceId)
        {
            ColdRestoreIntent.RequireIdentifier(
                actorInstanceId,
                nameof(actorInstanceId));
            if (actorRole == ColdRestoreActorRole.Companion
                && !string.Equals(
                    actorInstanceId,
                    companionInstanceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Companion event came from a different instance.");
            }
        }

        private static void ValidateProcessBinding(
            ColdRestoreIntent intent,
            ColdRestoreOperationState state,
            ColdRestoreActorRole actorRole,
            string actorSessionId,
            int processId,
            DateTimeOffset processStartedAtUtc)
        {
            ColdRestoreOperationStateMachine.RequireActor(state, actorRole);
            var started = processStartedAtUtc.ToUniversalTime();
            var sourceIdentity = processId == intent.SourceProcessId
                                 && started == intent.SourceProcessStartedAtUtc;
            if (state == ColdRestoreOperationState.Prepared
                || state == ColdRestoreOperationState.SourceQuiesced
                || state == ColdRestoreOperationState.SourceExited)
            {
                if (!sourceIdentity)
                {
                    throw new InvalidDataException(
                        "Source phase is bound to the wrong game process.");
                }

                if (actorRole == ColdRestoreActorRole.Runtime
                    && !string.Equals(
                        actorSessionId,
                        intent.SourceSessionId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Source Runtime session does not match the intent.");
                }

                return;
            }

            if (state == ColdRestoreOperationState.Cancelled
                || state == ColdRestoreOperationState.Failed)
            {
                return;
            }

            if (sourceIdentity)
            {
                throw new InvalidDataException(
                    "Cold phase reused the source game process identity.");
            }

            if (actorRole == ColdRestoreActorRole.Runtime
                && (string.IsNullOrEmpty(actorSessionId)
                    || string.Equals(
                        actorSessionId,
                        intent.SourceSessionId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Cold Runtime must use a new non-empty session ID.");
            }
        }

        private static void RequireExpectedSequence(
            ColdRestoreOperationSnapshot snapshot,
            long expectedSequence)
        {
            if (snapshot.Latest.Sequence != expectedSequence)
            {
                throw new InvalidOperationException(
                    "Cold-restore operation sequence is stale.");
            }
        }

        public void PersistFailureDiagnostic(string operationId, Exception exception)
            => PersistDiagnostic(operationId, exception, "failure.txt");

        public void PersistSlotRecoveryDiagnostic(string operationId, Exception exception)
            => PersistDiagnostic(operationId, exception, "slot-recovery-failure.txt");

        private void PersistDiagnostic(string operationId, Exception exception, string filename)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            // Validate the authenticated operation and path before writing.
            // This sidecar is diagnostic only, never an input to recovery or
            // proof of an operation transition. Preserve the first failure.
            var snapshot = Load(operationId);
            var destination = Path.Combine(OperationDirectory(snapshot.Intent.OperationId), filename);
            var message = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                + "\nstate=" + snapshot.Latest.State + "\n"
                + exception.GetType().FullName + ": " + exception.Message
                + "\n" + exception.StackTrace;
            if (message.Length > 16384) message = message.Substring(0, 16384) + "\n[truncated]";
            try { WriteDurable(destination, new System.Text.UTF8Encoding(false).GetBytes(message)); }
            catch (IOException) when (File.Exists(destination)) { }
        }

        private string OperationDirectory(string operationId)
        {
            return Path.Combine(operationsRoot, operationId);
        }

        private static string EventPath(string directory, long sequence)
        {
            return Path.Combine(
                directory,
                sequence.ToString("D20", CultureInfo.InvariantCulture)
                + ".json");
        }

        private static byte[] ReadBounded(string path, int maximumBytes)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > maximumBytes)
                {
                    throw new InvalidDataException(
                        "Cold-restore file size is outside the allowed range.");
                }

                var bytes = new byte[checked((int)stream.Length)];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    offset += read;
                }

                return bytes;
            }
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void RestrictDirectoryToCurrentUser(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Cold-restore store requires Windows ACLs.");
            }

            using (var identity = WindowsIdentity.GetCurrent())
            {
                var user = identity.User
                           ?? throw new InvalidOperationException(
                               "Current Windows SID is unavailable.");
                var security = new DirectorySecurity();
                security.SetOwner(user);
                security.SetAccessRuleProtection(
                    isProtected: true,
                    preserveInheritance: false);
                security.AddAccessRule(
                    new FileSystemAccessRule(
                        user,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit
                        | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl(security);
            }
        }

        private void Notify(ColdRestoreStoreStage stage)
        {
            afterStage?.Invoke(stage);
        }
    }
}
