using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class SlotRecoveryRecordTests
    {
        [TestMethod]
        public void DurableRecordPreservesAbsenceAndBindsExactProcess()
        {
            var original = new byte[] { 1 };
            var started = DateTimeOffset.FromUnixTimeSeconds(1000);
            var record = new SlotRecoveryRecord("cold-example", 42, started, 2, 4,
                original, null, new byte[] { 2 }, Array.Empty<byte>());
            original[0] = 9;
            var bytes = record.Serialize();
            var copy = SlotRecoveryRecord.Deserialize(bytes, Sha256Utility.ComputeHex(bytes));
            CollectionAssert.AreEqual(new byte[] { 1 }, copy.OriginalSave);
            Assert.IsNull(copy.OriginalModded);
            Assert.AreEqual(0, copy.InstalledModded!.Length);
            copy.OriginalSave![0] = 8;
            Assert.AreEqual(1, copy.OriginalSave![0]);
            Assert.IsTrue(copy.MatchesOwner("cold-example", 42, started));
            Assert.IsFalse(copy.MatchesOwner("cold-example", 42, started.AddSeconds(1)));
            Assert.IsFalse(copy.MatchesOwner("another-operation", 42, started));
            Assert.IsTrue(copy.CanRestore(new byte[] { 1 }, Array.Empty<byte>()));
            Assert.IsTrue(copy.CanRestore(new byte[] { 2 }, null));
            Assert.IsFalse(copy.CanRestore(new byte[] { 3 }, null));
        }

        [TestMethod]
        public void PublishedRecordCanBeReadByAFreshStore()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-recovery-" + Guid.NewGuid().ToString("N"));
            try
            {
                var record = new SlotRecoveryRecord("cold-example", 42,
                    DateTimeOffset.FromUnixTimeSeconds(1000), 0, 1, new byte[] { 1 }, null, new byte[] { 2 }, null);
                var store = new SlotRecoveryStore(root);
                var hash = store.Publish(record);
                Assert.AreEqual(hash, store.Publish(record));
                var loaded = new SlotRecoveryStore(root).Load("cold-example", hash);
                CollectionAssert.AreEqual(new byte[] { 1 }, loaded.OriginalSave);
                Assert.IsTrue(loaded.CanRestore(new byte[] { 2 }, null));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ExitRecoveryOrdersNestedInstallsAndRefusesUnsafeWrites()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new SlotRecoveryStore(Path.Combine(root, "records"));
                var started = DateTimeOffset.FromUnixTimeSeconds(1000);
                store.Publish(new SlotRecoveryRecord("cold-example", 42, started, 0, 1,
                    new byte[] { 1 }, null, new byte[] { 2 }, null));
                store.Publish(new SlotRecoveryRecord("cold-example", 42, started, 1, 1,
                    new byte[] { 2 }, null, new byte[] { 3 }, null));
                var save = Path.Combine(root, "user1.dat");
                var modded = Path.Combine(root, "user1.modded.json");
                File.WriteAllBytes(save, new byte[] { 3 });
                Assert.ThrowsExactly<InvalidOperationException>(() => store.RecoverAfterExit("cold-example", 42, started, root, () => false));
                Assert.ThrowsExactly<InvalidDataException>(() => store.RecoverAfterExit("cold-example", 42, started.AddSeconds(1), root, () => true));
                File.WriteAllBytes(modded, new byte[] { 9 });
                Assert.ThrowsExactly<InvalidOperationException>(() => store.RecoverAfterExit("cold-example", 42, started, root, () => true));
                CollectionAssert.AreEqual(new byte[] { 3 }, File.ReadAllBytes(save));
                CollectionAssert.AreEqual(new byte[] { 9 }, File.ReadAllBytes(modded));
                Assert.AreEqual(2, store.ReadPending("cold-example").Count);
                File.Delete(modded);
                Assert.AreEqual(2, store.RecoverAfterExit("cold-example", 42, started, root, () => true));
                CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(save));
                Assert.AreEqual(0, new SlotRecoveryStore(Path.Combine(root, "records")).ReadPending("cold-example").Count);
                Assert.AreEqual(0, store.RecoverAfterExit("cold-example", 42, started, root, () => true));
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void FreshStoreDiscoversPendingOwnersButNotCompletedRecords()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-recovery-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SlotRecoveryStore(root);
                var old = new SlotRecoveryRecord("cold-old", 41, DateTimeOffset.FromUnixTimeSeconds(1000),
                    0, 1, null, null, new byte[] { 1 }, null);
                var latest = new SlotRecoveryRecord("cold-new", 42, DateTimeOffset.FromUnixTimeSeconds(2000),
                    0, 2, null, null, new byte[] { 2 }, null);
                var oldHash = store.Publish(old);
                store.Publish(latest);
                var owners = new SlotRecoveryStore(root).FindPendingOwners();
                Assert.AreEqual(2, owners.Count);
                Assert.AreEqual("cold-new", owners[0].OperationId);
                Assert.AreEqual(42, owners[0].ProcessId);
                store.MarkComplete("cold-old", oldHash, true);
                var fresh = new SlotRecoveryStore(root).FindPendingOwners();
                Assert.AreEqual(1, fresh.Count);
                Assert.AreEqual("cold-new", fresh[0].OperationId);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void TamperAndTrailingBytesAreRejected()
        {
            var record = new SlotRecoveryRecord("cold-example", 42,
                DateTimeOffset.FromUnixTimeSeconds(1000), 0, 1, null, null, new byte[] { 2 }, null);
            var bytes = record.Serialize();
            var hash = Sha256Utility.ComputeHex(bytes);
            bytes[bytes.Length - 1] ^= 1;
            Assert.ThrowsExactly<InvalidDataException>(() => SlotRecoveryRecord.Deserialize(bytes, hash));
            var trailing = record.Serialize().Concat(new byte[] { 0 }).ToArray();
            Assert.ThrowsExactly<InvalidDataException>(() => SlotRecoveryRecord.Deserialize(trailing, Sha256Utility.ComputeHex(trailing)));
        }
    }
}
