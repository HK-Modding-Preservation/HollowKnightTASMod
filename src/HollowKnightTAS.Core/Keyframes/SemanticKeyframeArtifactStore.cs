using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Core.Keyframes
{
    public enum KeyframeTransactionStage : byte
    {
        TransactionCreated = 1,
        ObjectsPublished = 2,
        DescriptorPrepared = 3,
        DescriptorPublished = 4,
        IndexPublished = 5
    }

    public enum KeyframeArtifactStatus : byte
    {
        Ready = 1,
        NotFound = 2,
        Corrupt = 3,
        Failed = 4
    }

    public sealed class KeyframeSimulatedCrashException : Exception
    {
        public KeyframeSimulatedCrashException(
            KeyframeTransactionStage stage)
            : base("Simulated keyframe crash after " + stage + ".")
        {
            Stage = stage;
        }

        public KeyframeTransactionStage Stage { get; }
    }

    public sealed class SemanticKeyframeCommit
    {
        public const int MaximumObjectBytes = 16 * 1024 * 1024;

        public SemanticKeyframeCommit(
            string replaySaveId,
            long targetMovieTick,
            SemanticKeyframeDescriptor descriptor,
            IReadOnlyDictionary<string, byte[]> objects)
        {
            ReplaySaveId = KeyframeAdapterEnvelope.RequireId(
                replaySaveId,
                nameof(replaySaveId));
            Descriptor = descriptor
                         ?? throw new ArgumentNullException(
                             nameof(descriptor));
            if (targetMovieTick < descriptor.CaptureMovieTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMovieTick));
            }
            if (descriptor.Status != KeyframeStatus.Ready)
            {
                throw new ArgumentException(
                    "Only a Ready keyframe can enter the accelerator index.",
                    nameof(descriptor));
            }
            TargetMovieTick = targetMovieTick;

            var copied = new Dictionary<string, byte[]>(
                StringComparer.Ordinal);
            foreach (var item in objects
                                 ?? throw new ArgumentNullException(
                                     nameof(objects)))
            {
                ReplaySaveDescriptor.RequireSha256(
                    item.Key,
                    nameof(objects));
                if (item.Value == null
                    || item.Value.Length == 0
                    || item.Value.Length > MaximumObjectBytes)
                {
                    throw new ArgumentException(
                        "A keyframe object is empty or too large.",
                        nameof(objects));
                }
                if (!string.Equals(
                        Sha256Utility.ComputeHex(item.Value),
                        item.Key,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "A keyframe object hash does not match its bytes.",
                        nameof(objects));
                }
                copied.Add(item.Key, (byte[])item.Value.Clone());
            }

            var expected = RequiredObjectHashes(descriptor);
            if (!expected.SetEquals(copied.Keys))
            {
                throw new ArgumentException(
                    "The keyframe object set is not exact.",
                    nameof(objects));
            }
            foreach (var envelope in descriptor.Adapters)
            {
                if (copied[envelope.PayloadSha256].Length
                    != envelope.PayloadLength)
                {
                    throw new ArgumentException(
                        "An adapter payload length is inconsistent.",
                        nameof(objects));
                }
            }

            var manifest = KeyframeAdapterManifestCodec.Deserialize(
                copied[descriptor.AdapterManifestSha256]);
            ValidateAdapterContracts(descriptor, manifest);
            Objects =
                new ReadOnlyDictionary<string, byte[]>(copied);
        }

        public string ReplaySaveId { get; }
        public long TargetMovieTick { get; }
        public SemanticKeyframeDescriptor Descriptor { get; }
        public IReadOnlyDictionary<string, byte[]> Objects { get; }

        internal static HashSet<string> RequiredObjectHashes(
            SemanticKeyframeDescriptor descriptor)
        {
            var hashes = new HashSet<string>(
                StringComparer.Ordinal)
            {
                descriptor.AdapterManifestSha256,
                descriptor.SemanticSnapshotSha256,
                descriptor.RngStateSha256
            };
            foreach (var adapter in descriptor.Adapters)
            {
                hashes.Add(adapter.PayloadSha256);
            }
            return hashes;
        }

        internal static void ValidateAdapterContracts(
            SemanticKeyframeDescriptor descriptor,
            KeyframeAdapterManifest manifest)
        {
            var registrations = manifest.Adapters.ToDictionary(
                value => value.AdapterId,
                StringComparer.Ordinal);
            foreach (var envelope in descriptor.Adapters)
            {
                if (!registrations.TryGetValue(
                        envelope.AdapterId,
                        out var registration)
                    || registration.SchemaVersion
                    != envelope.SchemaVersion
                    || registration.IsRequired
                    != envelope.IsRequired
                    || registration.MinimumTier > descriptor.Tier)
                {
                    throw new InvalidDataException(
                        "The keyframe adapter contract is inconsistent.");
                }
            }
            foreach (var required in registrations.Values.Where(
                         value => value.IsRequired
                                  && value.MinimumTier
                                  <= descriptor.Tier))
            {
                if (!descriptor.Adapters.Any(
                        value => string.Equals(
                            value.AdapterId,
                            required.AdapterId,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        "A required adapter payload is absent.");
                }
            }
        }
    }

    public sealed class KeyframeArtifactCommitResult
    {
        public KeyframeArtifactCommitResult(
            bool success,
            KeyframeArtifactStatus status,
            string detail)
        {
            Success = success;
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public bool Success { get; }
        public KeyframeArtifactStatus Status { get; }
        public string Detail { get; }
    }

    public sealed class KeyframeArtifactLoadResult
    {
        public KeyframeArtifactLoadResult(
            KeyframeArtifactStatus status,
            SemanticKeyframeDescriptor? descriptor,
            IReadOnlyDictionary<string, byte[]>? objects,
            string detail)
        {
            Status = status;
            Descriptor = descriptor;
            Objects = objects;
            Detail = detail ?? string.Empty;
        }

        public KeyframeArtifactStatus Status { get; }
        public SemanticKeyframeDescriptor? Descriptor { get; }
        public IReadOnlyDictionary<string, byte[]>? Objects { get; }
        public string Detail { get; }
        public bool Success =>
            Status == KeyframeArtifactStatus.Ready
            && Descriptor != null
            && Objects != null;
    }

    public sealed class KeyframeRecoveryReport
    {
        public KeyframeRecoveryReport(
            int readyDescriptorCount,
            int indexedSaveCount,
            IEnumerable<string> incompleteTransactions,
            IEnumerable<string> invalidArtifacts)
        {
            ReadyDescriptorCount = readyDescriptorCount;
            IndexedSaveCount = indexedSaveCount;
            IncompleteTransactions =
                new ReadOnlyCollection<string>(
                    (incompleteTransactions
                     ?? Array.Empty<string>()).ToArray());
            InvalidArtifacts =
                new ReadOnlyCollection<string>(
                    (invalidArtifacts
                     ?? Array.Empty<string>()).ToArray());
        }

        public int ReadyDescriptorCount { get; }
        public int IndexedSaveCount { get; }
        public IReadOnlyList<string> IncompleteTransactions { get; }
        public IReadOnlyList<string> InvalidArtifacts { get; }
    }

    public sealed class SemanticKeyframeArtifactStore
    {
        private readonly object sync = new object();
        private readonly string root;
        private readonly string keyframeRoot;
        private readonly string entriesDirectory;
        private readonly string transactionsDirectory;
        private readonly string quarantineDirectory;
        private readonly string objectsDirectory;
        private readonly string indexPath;
        private readonly Action<KeyframeTransactionStage>? afterStage;
        private KeyframeArtifactIndex index =
            new KeyframeArtifactIndex(
                KeyframeArtifactIndex.CurrentSchemaVersion,
                Array.Empty<KeyframeIndexEntry>());
        private bool recovered;

        public SemanticKeyframeArtifactStore(
            string replaySaveStoreRoot,
            Action<KeyframeTransactionStage>? afterStage = null)
        {
            if (string.IsNullOrWhiteSpace(replaySaveStoreRoot))
            {
                throw new ArgumentException(
                    "A replay-save store root is required.",
                    nameof(replaySaveStoreRoot));
            }
            root = Path.GetFullPath(replaySaveStoreRoot);
            keyframeRoot = Path.Combine(root, "keyframes");
            entriesDirectory = Path.Combine(
                keyframeRoot,
                "entries");
            transactionsDirectory = Path.Combine(
                keyframeRoot,
                "transactions");
            quarantineDirectory = Path.Combine(
                keyframeRoot,
                "quarantine");
            objectsDirectory = Path.Combine(
                root,
                "objects",
                "sha256");
            indexPath = Path.Combine(keyframeRoot, "index.json");
            this.afterStage = afterStage;
            EnsureDirectories();
        }

        public KeyframeArtifactCommitResult Commit(
            SemanticKeyframeCommit commit)
        {
            if (commit == null)
            {
                throw new ArgumentNullException(nameof(commit));
            }
            lock (sync)
            {
                EnsureRecovered();
                var descriptor = commit.Descriptor;
                var entryPath = EntryPath(descriptor.KeyframeId);
                if (File.Exists(entryPath))
                {
                    return Failed(
                        KeyframeArtifactStatus.Failed,
                        "Keyframe ID already exists.");
                }
                if (!ObjectIsValid(
                        descriptor.BaselineObjectSha256,
                        BaselineBundle.MaximumBundleBytes))
                {
                    return Failed(
                        KeyframeArtifactStatus.Corrupt,
                        "The T09 baseline object is absent or corrupt.");
                }

                var transactionDirectory = Path.Combine(
                    transactionsDirectory,
                    descriptor.KeyframeId
                    + "--"
                    + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(transactionDirectory);
                try
                {
                    WriteTransactionState(
                        transactionDirectory,
                        commit,
                        KeyframeTransactionStage.TransactionCreated);
                    Notify(KeyframeTransactionStage.TransactionCreated);

                    foreach (var item in commit.Objects)
                    {
                        PublishObject(item.Key, item.Value);
                    }
                    WriteTransactionState(
                        transactionDirectory,
                        commit,
                        KeyframeTransactionStage.ObjectsPublished);
                    Notify(KeyframeTransactionStage.ObjectsPublished);

                    var descriptorBytes =
                        SemanticKeyframeDescriptorCodec.Serialize(
                            descriptor);
                    WriteAtomic(
                        Path.Combine(
                            transactionDirectory,
                            "descriptor.json"),
                        descriptorBytes);
                    WriteTransactionState(
                        transactionDirectory,
                        commit,
                        KeyframeTransactionStage.DescriptorPrepared);
                    Notify(KeyframeTransactionStage.DescriptorPrepared);

                    WriteAtomic(entryPath, descriptorBytes);
                    WriteTransactionState(
                        transactionDirectory,
                        commit,
                        KeyframeTransactionStage.DescriptorPublished);
                    Notify(KeyframeTransactionStage.DescriptorPublished);

                    UpsertAssociation(
                        commit.ReplaySaveId,
                        descriptor.KeyframeId,
                        descriptor.CaptureMovieTick,
                        commit.TargetMovieTick);
                    WriteIndex();
                    WriteTransactionState(
                        transactionDirectory,
                        commit,
                        KeyframeTransactionStage.IndexPublished);
                    Notify(KeyframeTransactionStage.IndexPublished);
                    return new KeyframeArtifactCommitResult(
                        true,
                        KeyframeArtifactStatus.Ready,
                        string.Empty);
                }
                catch (KeyframeSimulatedCrashException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException
                    || exception is InvalidDataException
                    || exception is UnauthorizedAccessException)
                {
                    return Failed(
                        KeyframeArtifactStatus.Failed,
                        exception.GetType().Name
                        + ": "
                        + exception.Message);
                }
            }
        }

        public KeyframeArtifactLoadResult Load(
            string keyframeId)
        {
            KeyframeAdapterEnvelope.RequireId(
                keyframeId,
                nameof(keyframeId));
            lock (sync)
            {
                EnsureRecovered();
                return LoadInternal(keyframeId);
            }
        }

        public KeyframeArtifactLoadResult FindForReplaySave(
            string replaySaveId)
        {
            KeyframeAdapterEnvelope.RequireId(
                replaySaveId,
                nameof(replaySaveId));
            lock (sync)
            {
                EnsureRecovered();
                var association = index.Find(replaySaveId);
                if (association == null)
                {
                    return new KeyframeArtifactLoadResult(
                        KeyframeArtifactStatus.NotFound,
                        null,
                        null,
                        "No keyframe is associated with this replay save.");
                }
                var result = LoadInternal(association.KeyframeId);
                if (!result.Success
                    || result.Descriptor == null)
                {
                    return result;
                }
                if (result.Descriptor.CaptureMovieTick
                    != association.KeyframeMovieTick
                    || association.TargetMovieTick
                    < association.KeyframeMovieTick)
                {
                    return new KeyframeArtifactLoadResult(
                        KeyframeArtifactStatus.Corrupt,
                        null,
                        null,
                        "The accelerator index disagrees with the descriptor.");
                }
                return result;
            }
        }

        public IReadOnlyList<KeyframeIndexEntry> ListAssociations()
        {
            lock (sync)
            {
                EnsureRecovered();
                return index.Entries;
            }
        }

        public KeyframeArtifactCommitResult Associate(
            string replaySaveId,
            string keyframeId,
            long targetMovieTick)
        {
            KeyframeAdapterEnvelope.RequireId(
                replaySaveId,
                nameof(replaySaveId));
            KeyframeAdapterEnvelope.RequireId(
                keyframeId,
                nameof(keyframeId));
            lock (sync)
            {
                EnsureRecovered();
                var loaded = LoadInternal(keyframeId);
                if (!loaded.Success || loaded.Descriptor == null)
                {
                    return Failed(loaded.Status, loaded.Detail);
                }
                if (targetMovieTick
                    < loaded.Descriptor.CaptureMovieTick)
                {
                    return Failed(
                        KeyframeArtifactStatus.Failed,
                        "The target tick is before the keyframe tick.");
                }
                try
                {
                    UpsertAssociation(
                        replaySaveId,
                        keyframeId,
                        loaded.Descriptor.CaptureMovieTick,
                        targetMovieTick);
                    WriteIndex();
                    return new KeyframeArtifactCommitResult(
                        true,
                        KeyframeArtifactStatus.Ready,
                        string.Empty);
                }
                catch (Exception exception) when (
                    exception is IOException
                    || exception is InvalidDataException
                    || exception is UnauthorizedAccessException)
                {
                    return Failed(
                        KeyframeArtifactStatus.Failed,
                        exception.GetType().Name
                        + ": "
                        + exception.Message);
                }
            }
        }

        public bool RemoveAssociation(string replaySaveId)
        {
            KeyframeAdapterEnvelope.RequireId(
                replaySaveId,
                nameof(replaySaveId));
            lock (sync)
            {
                EnsureRecovered();
                var remaining = index.Entries
                    .Where(
                        value => !string.Equals(
                            value.ReplaySaveId,
                            replaySaveId,
                            StringComparison.Ordinal))
                    .ToArray();
                if (remaining.Length == index.Entries.Count)
                {
                    return false;
                }
                index = new KeyframeArtifactIndex(
                    KeyframeArtifactIndex.CurrentSchemaVersion,
                    remaining);
                WriteIndex();
                return true;
            }
        }

        public bool RemoveUnreferencedDescriptor(
            string keyframeId)
        {
            KeyframeAdapterEnvelope.RequireId(
                keyframeId,
                nameof(keyframeId));
            lock (sync)
            {
                EnsureRecovered();
                if (index.Entries.Any(
                        value => string.Equals(
                            value.KeyframeId,
                            keyframeId,
                            StringComparison.Ordinal)))
                {
                    return false;
                }
                var path = EntryPath(keyframeId);
                if (!File.Exists(path))
                {
                    return false;
                }
                var destination = Path.Combine(
                    quarantineDirectory,
                    keyframeId
                    + "-"
                    + Timestamp()
                    + ".removed.json");
                File.Move(path, destination);
                return true;
            }
        }

        public KeyframeRecoveryReport Recover()
        {
            lock (sync)
            {
                EnsureDirectories();
                var invalid = new List<string>();
                index = ReadIndexOrEmpty(invalid);

                var readyIds = new HashSet<string>(
                    StringComparer.Ordinal);
                foreach (var path in Directory
                             .EnumerateFiles(
                                 entriesDirectory,
                                 "*.json")
                             .OrderBy(
                                 value => value,
                                 StringComparer.Ordinal))
                {
                    try
                    {
                        var descriptor =
                            SemanticKeyframeDescriptorCodec.Deserialize(
                                ReadBounded(
                                    path,
                                    SemanticKeyframeDescriptorCodec
                                        .MaximumBytes));
                        if (!string.Equals(
                                Path.GetFileName(path),
                                descriptor.KeyframeId + ".json",
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "Descriptor file name does not match ID.");
                        }
                        var loaded = LoadDescriptorObjects(descriptor);
                        if (loaded == null)
                        {
                            throw new InvalidDataException(
                                "A content object is absent or corrupt.");
                        }
                        readyIds.Add(descriptor.KeyframeId);
                    }
                    catch (Exception exception) when (
                        exception is IOException
                        || exception is InvalidDataException
                        || exception is UnauthorizedAccessException)
                    {
                        invalid.Add(
                            Path.GetFileName(path)
                            + ":"
                            + exception.GetType().Name);
                    }
                }

                var filtered = index.Entries.Where(
                        value => readyIds.Contains(value.KeyframeId))
                    .ToArray();
                if (filtered.Length != index.Entries.Count)
                {
                    invalid.Add("index:orphaned-association");
                    index = new KeyframeArtifactIndex(
                        KeyframeArtifactIndex.CurrentSchemaVersion,
                        filtered);
                    WriteIndex();
                }

                var incomplete = Directory
                    .EnumerateDirectories(transactionsDirectory)
                    .Where(
                        directory =>
                        {
                            var statePath = Path.Combine(
                                directory,
                                "state.json");
                            return !File.Exists(statePath)
                                   || File.ReadAllText(statePath)
                                       .IndexOf(
                                           "\"stage\":\"IndexPublished\"",
                                           StringComparison.Ordinal) < 0;
                        })
                    .Select(Path.GetFileName)
                    .Where(value => value != null)
                    .Cast<string>()
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                recovered = true;
                return new KeyframeRecoveryReport(
                    readyIds.Count,
                    index.Entries.Count,
                    incomplete,
                    invalid);
            }
        }

        private KeyframeArtifactLoadResult LoadInternal(
            string keyframeId)
        {
            var path = EntryPath(keyframeId);
            if (!File.Exists(path))
            {
                return new KeyframeArtifactLoadResult(
                    KeyframeArtifactStatus.NotFound,
                    null,
                    null,
                    "The keyframe descriptor was not found.");
            }
            try
            {
                var descriptor =
                    SemanticKeyframeDescriptorCodec.Deserialize(
                        ReadBounded(
                            path,
                            SemanticKeyframeDescriptorCodec.MaximumBytes));
                if (!string.Equals(
                        descriptor.KeyframeId,
                        keyframeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The keyframe ID does not match its file.");
                }
                var objects = LoadDescriptorObjects(descriptor);
                if (objects == null)
                {
                    throw new InvalidDataException(
                        "A content object is absent or corrupt.");
                }
                return new KeyframeArtifactLoadResult(
                    KeyframeArtifactStatus.Ready,
                    descriptor,
                    objects,
                    string.Empty);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is InvalidDataException
                || exception is UnauthorizedAccessException)
            {
                return new KeyframeArtifactLoadResult(
                    KeyframeArtifactStatus.Corrupt,
                    null,
                    null,
                    exception.GetType().Name
                    + ": "
                    + exception.Message);
            }
        }

        private IReadOnlyDictionary<string, byte[]>?
            LoadDescriptorObjects(
                SemanticKeyframeDescriptor descriptor)
        {
            if (!ObjectIsValid(
                    descriptor.BaselineObjectSha256,
                    BaselineBundle.MaximumBundleBytes))
            {
                return null;
            }
            var objects = new Dictionary<string, byte[]>(
                StringComparer.Ordinal);
            foreach (var hash in SemanticKeyframeCommit
                         .RequiredObjectHashes(descriptor))
            {
                var path = ObjectPath(hash);
                if (!ObjectIsValid(
                        hash,
                        SemanticKeyframeCommit.MaximumObjectBytes))
                {
                    return null;
                }
                objects.Add(
                    hash,
                    ReadBounded(
                        path,
                        SemanticKeyframeCommit.MaximumObjectBytes));
            }
            var manifest = KeyframeAdapterManifestCodec.Deserialize(
                objects[descriptor.AdapterManifestSha256]);
            SemanticKeyframeCommit.ValidateAdapterContracts(
                descriptor,
                manifest);
            foreach (var adapter in descriptor.Adapters)
            {
                if (objects[adapter.PayloadSha256].Length
                    != adapter.PayloadLength)
                {
                    throw new InvalidDataException(
                        "An adapter payload length differs.");
                }
            }
            return new ReadOnlyDictionary<string, byte[]>(objects);
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
            Directory.CreateDirectory(entriesDirectory);
            Directory.CreateDirectory(transactionsDirectory);
            Directory.CreateDirectory(quarantineDirectory);
            Directory.CreateDirectory(objectsDirectory);
        }

        private void PublishObject(string hash, byte[] bytes)
        {
            ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
            if (!string.Equals(
                    Sha256Utility.ComputeHex(bytes),
                    hash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Object bytes do not match their hash.");
            }
            var directory = Path.Combine(
                objectsDirectory,
                hash.Substring(0, 2));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, hash);
            if (File.Exists(path))
            {
                if (!ObjectIsValid(
                        hash,
                        SemanticKeyframeCommit.MaximumObjectBytes))
                {
                    throw new InvalidDataException(
                        "An existing shared object is corrupt.");
                }
                return;
            }
            WriteAtomic(path, bytes);
        }

        private bool ObjectIsValid(string hash, int maximumBytes)
        {
            try
            {
                ReplaySaveDescriptor.RequireSha256(hash, nameof(hash));
                var path = ObjectPath(hash);
                var info = new FileInfo(path);
                return info.Exists
                       && info.Length > 0
                       && info.Length <= maximumBytes
                       && string.Equals(
                           Sha256Utility.ComputeFileHex(path),
                           hash,
                           StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private KeyframeArtifactIndex ReadIndexOrEmpty(
            List<string> invalid)
        {
            if (!File.Exists(indexPath))
            {
                return new KeyframeArtifactIndex(
                    KeyframeArtifactIndex.CurrentSchemaVersion,
                    Array.Empty<KeyframeIndexEntry>());
            }
            try
            {
                return KeyframeArtifactIndexCodec.Deserialize(
                    ReadBounded(
                        indexPath,
                        KeyframeArtifactIndexCodec.MaximumBytes));
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is InvalidDataException
                || exception is UnauthorizedAccessException)
            {
                invalid.Add(
                    "index.json:" + exception.GetType().Name);
                var quarantinePath = Path.Combine(
                    quarantineDirectory,
                    "index-"
                    + Timestamp()
                    + "-"
                    + Guid.NewGuid().ToString("N")
                    + ".corrupt.json");
                File.Copy(indexPath, quarantinePath);
                var empty = new KeyframeArtifactIndex(
                    KeyframeArtifactIndex.CurrentSchemaVersion,
                    Array.Empty<KeyframeIndexEntry>());
                WriteAtomic(
                    indexPath,
                    KeyframeArtifactIndexCodec.Serialize(empty));
                return empty;
            }
        }

        private void UpsertAssociation(
            string replaySaveId,
            string keyframeId,
            long keyframeMovieTick,
            long targetMovieTick)
        {
            var entries = index.Entries
                .Where(
                    value => !string.Equals(
                        value.ReplaySaveId,
                        replaySaveId,
                        StringComparison.Ordinal))
                .Concat(
                    new[]
                    {
                        new KeyframeIndexEntry(
                            replaySaveId,
                            keyframeId,
                            keyframeMovieTick,
                            targetMovieTick)
                    });
            index = new KeyframeArtifactIndex(
                KeyframeArtifactIndex.CurrentSchemaVersion,
                entries);
        }

        private void WriteIndex()
        {
            WriteAtomic(
                indexPath,
                KeyframeArtifactIndexCodec.Serialize(index));
        }

        private void WriteTransactionState(
            string transactionDirectory,
            SemanticKeyframeCommit commit,
            KeyframeTransactionStage stage)
        {
            var bytes = ReplaySaveJson.StrictUtf8.GetBytes(
                "{\"keyframeId\":\""
                + commit.Descriptor.KeyframeId
                + "\",\"replaySaveId\":\""
                + commit.ReplaySaveId
                + "\",\"stage\":\""
                + stage
                + "\",\"targetMovieTick\":"
                + commit.TargetMovieTick.ToString(
                    CultureInfo.InvariantCulture)
                + "}");
            WriteAtomic(
                Path.Combine(transactionDirectory, "state.json"),
                bytes);
        }

        private void Notify(KeyframeTransactionStage stage)
        {
            afterStage?.Invoke(stage);
        }

        private string EntryPath(string keyframeId)
        {
            return Path.Combine(
                entriesDirectory,
                keyframeId + ".json");
        }

        private string ObjectPath(string hash)
        {
            return Path.Combine(
                objectsDirectory,
                hash.Substring(0, 2),
                hash);
        }

        private static byte[] ReadBounded(
            string path,
            int maximumBytes)
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length <= 0
                || info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "File size is outside the allowed range.");
            }
            return File.ReadAllBytes(path);
        }

        private static void WriteAtomic(
            string destinationPath,
            byte[] bytes)
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
                    File.Replace(
                        temporaryPath,
                        destinationPath,
                        null);
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

        private static string Timestamp()
        {
            return DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfffffff'Z'",
                CultureInfo.InvariantCulture);
        }

        private static KeyframeArtifactCommitResult Failed(
            KeyframeArtifactStatus status,
            string detail)
        {
            return new KeyframeArtifactCommitResult(
                false,
                status,
                detail);
        }
    }
}
