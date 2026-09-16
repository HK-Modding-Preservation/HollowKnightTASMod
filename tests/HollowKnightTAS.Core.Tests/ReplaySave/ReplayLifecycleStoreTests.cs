using System;
using System.Collections.Generic;
using System.IO;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplayLifecycleStoreTests
    {
        [TestMethod]
        public void ReopenedStoreLoadsReferencedBytesAndRejectsMissingDependency()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var store = new ContentAddressedReplaySaveStore(directory);
                var root = ReplaySaveTestFactory.Commit("lifecycle-root");
                Assert.IsTrue(store.Commit(root).Success);
                var bytes = new byte[] { 17, 31, 49 };
                var hash = Sha256Utility.ComputeHex(bytes);
                var moddedHash = Sha256Utility.ComputeHex(Array.Empty<byte>());
                var log = new ReplayLifecycleLog(root.Descriptor.BaselineObjectSha256,
                    new[] { new ReplayLifecycleRecord(0, 0, new string('b', 64),
                        ReplayLifecycleKind.LoadSlot, 4, hash, ReplayLifecycleOutcome.Completed, 100, "", moddedHash) });
                var objects = new Dictionary<string, byte[]> { [hash] = bytes, [moddedHash] = Array.Empty<byte>() };
                var published = store.PublishLifecycleLog(log, objects);
                bytes[0] = 0; // Published content must not alias caller buffers.
                var reopened = new ContentAddressedReplaySaveStore(directory);
                CollectionAssert.AreEqual(log.Serialize(), reopened.LoadLifecycleLog(published).Serialize());
                Assert.AreEqual(published, reopened.PublishLifecycleLog(log, new Dictionary<string, byte[]>()));
                Assert.HasCount(1, reopened.List()); // Publishing a log is not publishing a Ready save.
                File.Delete(Path.Combine(directory, "objects", "sha256", hash.Substring(0, 2), hash));
                Assert.ThrowsExactly<FileNotFoundException>(() => reopened.LoadLifecycleLog(published));
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void InvalidDependencyDoesNotPublishLog()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var store = new ContentAddressedReplaySaveStore(directory);
                var root = ReplaySaveTestFactory.Commit("lifecycle-invalid-root");
                Assert.IsTrue(store.Commit(root).Success);
                var hash = Sha256Utility.ComputeHex(new byte[] { 1 });
                var log = new ReplayLifecycleLog(root.Descriptor.BaselineObjectSha256,
                    new[] { new ReplayLifecycleRecord(0, 0, new string('b', 64),
                        ReplayLifecycleKind.LoadSlot, 4, hash, ReplayLifecycleOutcome.Completed, 100, "") });
                var logHash = Sha256Utility.ComputeHex(log.Serialize());
                Assert.ThrowsExactly<InvalidDataException>(() => store.PublishLifecycleLog(log,
                    new Dictionary<string, byte[]> { [hash] = new byte[] { 2 } }));
                Assert.IsFalse(File.Exists(Path.Combine(directory, "objects", "sha256", logHash.Substring(0, 2), logHash)));
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
