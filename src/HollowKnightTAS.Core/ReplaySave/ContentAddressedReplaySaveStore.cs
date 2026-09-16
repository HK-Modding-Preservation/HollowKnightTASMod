using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ContentAddressedReplaySaveStore : IReplaySaveStore, IReplayLifecyclePlanStore
    {
        private readonly object sync = new object();
        private readonly string root;
        private readonly string entriesDirectory;
        private readonly string objectsDirectory;
        private readonly string transactionsDirectory;
        private readonly string trashDirectory;
        private readonly string catalogPath;
        private readonly string? expectedManifestSha256;
        private readonly Action<ReplaySaveTransactionStage>? afterStage;
        private ReplaySaveCatalog catalog = new ReplaySaveCatalog();
        private bool recovered;

        public ContentAddressedReplaySaveStore(
            string root,
            string? expectedManifestSha256 = null,
            Action<ReplaySaveTransactionStage>? afterStage = null)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException(
                    "A replay-save store root is required.",
                    nameof(root));
            }

            if (expectedManifestSha256 != null)
            {
                ReplaySaveDescriptor.RequireSha256(
                    expectedManifestSha256,
                    nameof(expectedManifestSha256));
            }

            this.root = Path.GetFullPath(root);
            this.expectedManifestSha256 = expectedManifestSha256;
            this.afterStage = afterStage;
            entriesDirectory = Path.Combine(this.root, "entries");
            objectsDirectory = Path.Combine(this.root, "objects", "sha256");
            transactionsDirectory = Path.Combine(this.root, "transactions");
            trashDirectory = Path.Combine(this.root, "trash");
            catalogPath = Path.Combine(this.root, "catalog.json");
            EnsureDirectories();
        }

        public ReplaySaveCommitResult Commit(ReplaySaveCommit commit)
        {
            if (commit == null)
            {
                throw new ArgumentNullException(nameof(commit));
            }

            lock (sync)
            {
                EnsureRecovered();
                var descriptor = commit.Descriptor;
                var entryPath = EntryPath(descriptor.ReplaySaveId);
                if (File.Exists(entryPath))
                {
                    return new ReplaySaveCommitResult(
                        false,
                        ReplaySaveStatus.Failed,
                        descriptor,
                        "Replay-save ID already exists.");
                }

                var transactionId =
                    descriptor.ReplaySaveId
                    + "--"
                    + Guid.NewGuid().ToString("N");
                var transactionDirectory = Path.Combine(
                    transactionsDirectory,
                    transactionId);
                Directory.CreateDirectory(transactionDirectory);
                try
                {
                    WriteTransactionState(
                        transactionDirectory,
                        descriptor,
                        ReplaySaveTransactionStage.TransactionCreated);
                    Notify(ReplaySaveTransactionStage.TransactionCreated);

                    foreach (var item in commit.Objects)
                    {
                        PublishObject(item.Key, item.Value);
                    }

                    WriteTransactionState(
                        transactionDirectory,
                        descriptor,
                        ReplaySaveTransactionStage.ObjectsPublished);
                    Notify(ReplaySaveTransactionStage.ObjectsPublished);

                    var entryBytes = ReplaySaveEntryCodec.Serialize(descriptor);
                    WriteAtomic(
                        Path.Combine(transactionDirectory, "entry.json"),
                        entryBytes);
                    WriteTransactionState(
                        transactionDirectory,
                        descriptor,
                        ReplaySaveTransactionStage.DescriptorPrepared);
                    Notify(ReplaySaveTransactionStage.DescriptorPrepared);

                    WriteAtomic(entryPath, entryBytes);
                    WriteTransactionState(
                        transactionDirectory,
                        descriptor,
                        ReplaySaveTransactionStage.EntryPublished);
                    Notify(ReplaySaveTransactionStage.EntryPublished);

                    ApplyAutomaticRetention();
                    WriteTransactionState(
                        transactionDirectory,
                        descriptor,
                        ReplaySaveTransactionStage.RetentionApplied);
                    Notify(ReplaySaveTransactionStage.RetentionApplied);

                    RebuildCatalog(out _, out _);
                    WriteCatalog();
                    WriteTransactionState(
                        transactionDirectory,
                        descriptor,
                        ReplaySaveTransactionStage.CatalogPublished);
                    Notify(ReplaySaveTransactionStage.CatalogPublished);

                    return new ReplaySaveCommitResult(
                        true,
                        ReplaySaveStatus.Ready,
                        descriptor,
                        string.Empty);
                }
                catch (ReplaySaveSimulatedCrashException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return new ReplaySaveCommitResult(
                        false,
                        ReplaySaveStatus.Failed,
                        descriptor,
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        public ReplaySaveLoadResult Load(string replaySaveId)
        {
            ReplaySaveDescriptor.RequireIdentifier(
                replaySaveId,
                nameof(replaySaveId));
            lock (sync)
            {
                EnsureRecovered();
                var path = EntryPath(replaySaveId);
                if (!File.Exists(path))
                {
                    return new ReplaySaveLoadResult(
                        ReplaySaveStatus.Failed,
                        null,
                        null,
                        "Replay-save entry was not found.");
                }

                ReplaySaveDescriptor descriptor;
                try
                {
                    descriptor = ReplaySaveEntryCodec.Deserialize(
                        ReadBounded(path, ReplaySaveEntryCodec.MaximumBytes));
                }
                catch (Exception exception) when (
                    exception is IOException
                    || exception is InvalidDataException
                    || exception is UnauthorizedAccessException)
                {
                    return new ReplaySaveLoadResult(
                        ReplaySaveStatus.Corrupt,
                        null,
                        null,
                        exception.GetType().Name + ": " + exception.Message);
                }

                var validation = ReplaySaveValidator.Validate(
                    descriptor,
                    TryReadObject,
                    expectedManifestSha256);
                return new ReplaySaveLoadResult(
                    validation.Status,
                    descriptor,
                    validation.Package,
                    validation.Detail);
            }
        }

        // Publishes dependencies first and the immutable log last. This does
        // not publish a save entry or claim that Runtime can replay the log.
        public string PublishLifecycleLog(ReplayLifecycleLog log,
            IReadOnlyDictionary<string, byte[]> slotObjects)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (slotObjects == null) throw new ArgumentNullException(nameof(slotObjects));
            if (!log.IsCompleted) throw new InvalidDataException("An unfinished lifecycle cannot be published for replay.");
            lock (sync)
            {
                EnsureRecovered();
                ReadVerifiedLifecycleDependency(log.RootBaselineSha256, BaselineBundle.MaximumBundleBytes);
                // Copy only referenced objects and validate all of them before
                // publishing anything; caller mutation cannot change the bytes.
                var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var hash in log.Records.Where(record => record.Kind == ReplayLifecycleKind.LoadSlot)
                    .SelectMany(record => new[] { record.SlotObjectSha256, record.ModdedSlotObjectSha256 })
                    .Where(hash => !string.IsNullOrEmpty(hash)).Select(hash => hash!).Distinct(StringComparer.Ordinal))
                {
                    byte[] bytes;
                    if (slotObjects.TryGetValue(hash, out var supplied))
                    {
                        if (supplied == null || supplied.Length > ReplayLifecycleLog.MaximumSlotBytes)
                            throw new InvalidDataException("Lifecycle slot object size is invalid.");
                        bytes = (byte[])supplied.Clone();
                    }
                    else bytes = ReadVerifiedLifecycleDependency(hash, ReplayLifecycleLog.MaximumSlotBytes, allowEmpty: true);
                    captured.Add(hash, bytes);
                }
                log.VerifySlotObjects(hash => captured.TryGetValue(hash, out var bytes) ? bytes : null);
                var encoded = log.Serialize();
                foreach (var item in captured) PublishObject(item.Key, item.Value);
                var logHash = Sha256Utility.ComputeHex(encoded);
                PublishObject(logHash, encoded);
                return logHash;
            }
        }

        public string PublishExecutionPlan(ReplayLifecycleExecutionPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            lock (sync)
            {
                EnsureRecovered();
                var bytes = plan.Serialize();
                var hash = Sha256Utility.ComputeHex(bytes);
                PublishObject(hash, bytes);
                return hash;
            }
        }

        public ReplayLifecycleExecutionPlan LoadExecutionPlan(string sha256)
        {
            lock (sync)
            {
                EnsureRecovered();
                return ReplayLifecycleExecutionPlan.Deserialize(
                    ReadVerifiedLifecycleDependency(sha256, ReplayLifecycleExecutionPlan.MaximumBytes), sha256);
            }
        }

        public ReplayLifecycleLog LoadLifecycleLog(string sha256)
        {
            lock (sync)
            {
                EnsureRecovered();
                var log = ReplayLifecycleLog.Deserialize(
                    ReadVerifiedLifecycleDependency(sha256, ReplayLifecycleLog.MaximumBytes));
                if (!log.IsCompleted) throw new InvalidDataException("Lifecycle log is unfinished.");
                ReadVerifiedLifecycleDependency(log.RootBaselineSha256, BaselineBundle.MaximumBundleBytes);
                log.VerifySlotObjects(hash => ReadVerifiedLifecycleDependency(hash, ReplayLifecycleLog.MaximumSlotBytes, allowEmpty: true));
                return log;
            }
        }

        private byte[] ReadVerifiedLifecycleDependency(string hash, int maximumBytes, bool allowEmpty = false)
        {
            ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
            var bytes = ReadBounded(ObjectPath(hash), maximumBytes, allowEmpty);
            if (Sha256Utility.ComputeHex(bytes) != hash)
                throw new InvalidDataException("Lifecycle dependency hash mismatch: " + hash);
            return bytes;
        }

        public ReplaySaveMovieObjectPublishResult PublishMovieObject(
            byte[] canonicalMovieBytes)
        {
            if (canonicalMovieBytes == null
                || canonicalMovieBytes.Length == 0
                || canonicalMovieBytes.Length
                   > ReplaySaveCommit.MaximumMovieBytes)
            {
                return new ReplaySaveMovieObjectPublishResult(
                    false,
                    string.Empty,
                    "Movie object size is outside the allowed range.");
            }

            lock (sync)
            {
                try
                {
                    EnsureRecovered();
                    var sha256 = Sha256Utility.ComputeHex(
                        canonicalMovieBytes);
                    PublishObject(sha256, canonicalMovieBytes);
                    return new ReplaySaveMovieObjectPublishResult(
                        true,
                        sha256,
                        string.Empty);
                }
                catch (Exception exception) when (
                    exception is IOException
                    || exception is InvalidDataException
                    || exception is UnauthorizedAccessException)
                {
                    return new ReplaySaveMovieObjectPublishResult(
                        false,
                        string.Empty,
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        public ReplaySaveMovieObjectLoadResult LoadMovieObject(string sha256)
        {
            try
            {
                ReplaySaveDescriptor.RequireSha256(
                    sha256,
                    nameof(sha256));
            }
            catch (ArgumentException exception)
            {
                return new ReplaySaveMovieObjectLoadResult(
                    false,
                    sha256,
                    null,
                    exception.Message);
            }

            lock (sync)
            {
                try
                {
                    EnsureRecovered();
                    var path = ObjectPath(sha256);
                    if (!File.Exists(path))
                    {
                        return new ReplaySaveMovieObjectLoadResult(
                            false,
                            sha256,
                            null,
                            "Movie object was not found.");
                    }

                    var bytes = ReadBounded(
                        path,
                        ReplaySaveCommit.MaximumMovieBytes);
                    var actualSha256 = Sha256Utility.ComputeHex(bytes);
                    if (!string.Equals(
                            actualSha256,
                            sha256,
                            StringComparison.Ordinal))
                    {
                        return new ReplaySaveMovieObjectLoadResult(
                            false,
                            sha256,
                            null,
                            "Content-addressed movie object is corrupt.");
                    }

                    return new ReplaySaveMovieObjectLoadResult(
                        true,
                        sha256,
                        bytes,
                        string.Empty);
                }
                catch (Exception exception) when (
                    exception is IOException
                    || exception is InvalidDataException
                    || exception is UnauthorizedAccessException)
                {
                    return new ReplaySaveMovieObjectLoadResult(
                        false,
                        sha256,
                        null,
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        public IReadOnlyList<ReplaySaveDescriptor> List()
        {
            lock (sync)
            {
                EnsureRecovered();
                return new ReadOnlyCollection<ReplaySaveDescriptor>(
                    catalog.Entries
                        .Select(value => value.Descriptor)
                        .ToList());
            }
        }

        public IReadOnlyList<ReplaySaveCatalogEntry> Inspect()
        {
            lock (sync)
            {
                EnsureRecovered();
                return catalog.Entries;
            }
        }

        public ReplaySaveRecoveryReport Recover()
        {
            lock (sync)
            {
                EnsureDirectories();
                var incomplete = FindIncompleteTransactions();
                RebuildCatalog(out var invalid, out var ready);
                ApplyAutomaticRetention();
                RebuildCatalog(out var invalidAfterRetention, out ready);
                foreach (var value in invalidAfterRetention)
                {
                    if (!invalid.Contains(value, StringComparer.Ordinal))
                    {
                        invalid.Add(value);
                    }
                }

                WriteCatalog();
                recovered = true;
                return new ReplaySaveRecoveryReport(
                    catalog.Entries.Count,
                    ready,
                    incomplete,
                    invalid,
                    true);
            }
        }

        public ReplaySaveCommitResult PinAsManual(string replaySaveId)
        {
            ReplaySaveDescriptor.RequireIdentifier(
                replaySaveId,
                nameof(replaySaveId));
            lock (sync)
            {
                EnsureRecovered();
                var load = Load(replaySaveId);
                if (!load.Success || load.Descriptor == null)
                {
                    return new ReplaySaveCommitResult(
                        false,
                        load.Status,
                        load.Descriptor,
                        load.Error);
                }

                if (load.Descriptor.Reason
                    != ReplaySaveReason.AutomaticInterval)
                {
                    return new ReplaySaveCommitResult(
                        true,
                        ReplaySaveStatus.Ready,
                        load.Descriptor,
                        string.Empty);
                }

                var pinned = load.Descriptor.PinAsManual();
                try
                {
                    WriteAtomic(
                        EntryPath(replaySaveId),
                        ReplaySaveEntryCodec.Serialize(pinned));
                    RebuildCatalog(out _, out _);
                    WriteCatalog();
                    return new ReplaySaveCommitResult(
                        true,
                        ReplaySaveStatus.Ready,
                        pinned,
                        string.Empty);
                }
                catch (Exception exception)
                {
                    return new ReplaySaveCommitResult(
                        false,
                        ReplaySaveStatus.Failed,
                        pinned,
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        private void EnsureRecovered()
        {
            if (!recovered)
            {
                Recover();
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(entriesDirectory);
            Directory.CreateDirectory(objectsDirectory);
            Directory.CreateDirectory(transactionsDirectory);
            Directory.CreateDirectory(trashDirectory);
        }

        private void PublishObject(string expectedHash, byte[] bytes)
        {
            ReplaySaveDescriptor.RequireSha256(expectedHash, nameof(expectedHash));
            var actualHash = Sha256Utility.ComputeHex(bytes);
            if (!string.Equals(
                    actualHash,
                    expectedHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Commit object hash does not match its bytes.");
            }

            var directory = Path.Combine(
                objectsDirectory,
                expectedHash.Substring(0, 2));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, expectedHash);
            if (File.Exists(path))
            {
                var existingHash = Sha256Utility.ComputeFileHex(path);
                if (!string.Equals(
                        existingHash,
                        expectedHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Existing content-addressed object is corrupt.");
                }

                return;
            }

            WriteAtomic(path, bytes);
        }

        private bool TryReadObject(string hash, out byte[]? bytes)
        {
            try
            {
                ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
                var path = ObjectPath(hash);
                if (!File.Exists(path))
                {
                    bytes = null;
                    return false;
                }

                bytes = ReadBounded(
                    path,
                    BaselineBundle.MaximumBundleBytes
                    + ReplaySaveCommit.MaximumMovieBytes, allowEmpty: true);
                return true;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        private void RebuildCatalog(
            out List<string> invalidFiles,
            out int readyCount)
        {
            invalidFiles = new List<string>();
            readyCount = 0;
            var rebuilt = new ReplaySaveCatalog(catalog.Revision);
            foreach (var path in Directory
                         .EnumerateFiles(entriesDirectory, "*.json")
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                ReplaySaveDescriptor descriptor;
                try
                {
                    descriptor = ReplaySaveEntryCodec.Deserialize(
                        ReadBounded(path, ReplaySaveEntryCodec.MaximumBytes));
                    var expectedName = descriptor.ReplaySaveId + ".json";
                    if (!string.Equals(
                            Path.GetFileName(path),
                            expectedName,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Entry file name does not match its replay-save ID.");
                    }
                }
                catch (Exception exception) when (
                    exception is IOException
                    || exception is InvalidDataException
                    || exception is UnauthorizedAccessException)
                {
                    invalidFiles.Add(
                        Path.GetFileName(path)
                        + ":"
                        + exception.GetType().Name);
                    continue;
                }

                var validation = ReplaySaveValidator.Validate(
                    descriptor,
                    TryReadObject,
                    expectedManifestSha256);
                rebuilt.Upsert(
                    new ReplaySaveCatalogEntry(
                        descriptor,
                        validation.Status,
                        validation.Detail));
                if (validation.Success)
                {
                    readyCount++;
                }
            }

            catalog = rebuilt;
        }

        private void ApplyAutomaticRetention()
        {
            var automatic = Directory
                .EnumerateFiles(entriesDirectory, "*.json")
                .Select(
                    path =>
                    {
                        try
                        {
                            var descriptor = ReplaySaveEntryCodec.Deserialize(
                                ReadBounded(
                                    path,
                                    ReplaySaveEntryCodec.MaximumBytes));
                            return new EntryFile(path, descriptor);
                        }
                        catch
                        {
                            return null;
                        }
                    })
                .Where(value => value != null)
                .Cast<EntryFile>()
                .Where(
                    value => value.Descriptor.Reason
                             == ReplaySaveReason.AutomaticInterval)
                .OrderByDescending(
                    value => value.Descriptor.EffectiveMovieTick)
                .ThenByDescending(value => value.Descriptor.CreatedAtUtc)
                .ThenByDescending(
                    value => value.Descriptor.ReplaySaveId,
                    StringComparer.Ordinal)
                .ToList();
            if (automatic.Count == 0)
            {
                return;
            }

            var retention = automatic[0].Descriptor.AutoRetentionCount;
            var eligible = new List<EntryFile>();
            foreach (var entry in automatic)
            {
                var validation = ReplaySaveValidator.Validate(
                    entry.Descriptor,
                    TryReadObject,
                    expectedManifestSha256);
                if (validation.Success)
                {
                    eligible.Add(entry);
                }
            }

            foreach (var victim in eligible.Skip(retention))
            {
                var trashName =
                    victim.Descriptor.ReplaySaveId
                    + "-"
                    + DateTimeOffset.UtcNow.ToString(
                        "yyyyMMdd'T'HHmmssfffffff'Z'",
                        CultureInfo.InvariantCulture)
                    + "-"
                    + Guid.NewGuid().ToString("N")
                    + ".json";
                File.Move(
                    victim.Path,
                    Path.Combine(trashDirectory, trashName));
            }
        }

        private List<string> FindIncompleteTransactions()
        {
            var result = new List<string>();
            foreach (var directory in Directory
                         .EnumerateDirectories(transactionsDirectory)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                var statePath = Path.Combine(directory, "state.json");
                if (!File.Exists(statePath)
                    || File.ReadAllText(
                            statePath,
                            new UTF8Encoding(false, true))
                        .IndexOf(
                            "\"stage\":\"CatalogPublished\"",
                            StringComparison.Ordinal) < 0)
                {
                    result.Add(Path.GetFileName(directory));
                }
            }

            return result;
        }

        private void WriteTransactionState(
            string transactionDirectory,
            ReplaySaveDescriptor descriptor,
            ReplaySaveTransactionStage stage)
        {
            var builder = new StringBuilder(512);
            builder.Append("{\"descriptorSha256\":");
            CanonicalJsonWriter.AppendString(
                builder,
                Sha256Utility.ComputeHex(
                    ReplaySaveDescriptorCodec.Serialize(descriptor)));
            builder.Append(",\"replaySaveId\":");
            CanonicalJsonWriter.AppendString(
                builder,
                descriptor.ReplaySaveId);
            builder.Append(",\"stage\":");
            CanonicalJsonWriter.AppendString(builder, stage.ToString());
            builder.Append('}');
            WriteAtomic(
                Path.Combine(transactionDirectory, "state.json"),
                ReplaySaveJson.StrictUtf8.GetBytes(builder.ToString()));
        }

        private void WriteCatalog()
        {
            WriteAtomic(
                catalogPath,
                ReplaySaveCatalogCodec.Serialize(catalog));
        }

        private void Notify(ReplaySaveTransactionStage stage)
        {
            afterStage?.Invoke(stage);
        }

        private string EntryPath(string replaySaveId)
        {
            return Path.Combine(entriesDirectory, replaySaveId + ".json");
        }

        private string ObjectPath(string hash)
        {
            return Path.Combine(
                objectsDirectory,
                hash.Substring(0, 2),
                hash);
        }

        private static byte[] ReadBounded(string path, int maximumBytes, bool allowEmpty = false)
        {
            var info = new FileInfo(path);
            if ((!allowEmpty && info.Length <= 0) || info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "File size is outside the allowed range.");
            }

            return File.ReadAllBytes(path);
        }

        private static void WriteAtomic(string destinationPath, byte[] bytes)
        {
            var temporaryPath =
                destinationPath
                + ".tmp-"
                + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, null);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private sealed class EntryFile
        {
            public EntryFile(
                string path,
                ReplaySaveDescriptor descriptor)
            {
                Path = path;
                Descriptor = descriptor;
            }

            public string Path { get; }
            public ReplaySaveDescriptor Descriptor { get; }
        }
    }
}
