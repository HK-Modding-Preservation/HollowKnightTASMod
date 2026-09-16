using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplayLifecycleLogTests
    {
        private static readonly string Root = new string('a', 64);
        private static readonly string Prefix = new string('b', 64);
        private static ReplayLifecycleRecord Menu(int sequence, long tick = 10,
            ReplayLifecycleOutcome outcome = ReplayLifecycleOutcome.Completed) =>
            new ReplayLifecycleRecord(sequence, tick, Prefix, ReplayLifecycleKind.ReturnToMenu,
                0, "", outcome, 12, outcome == ReplayLifecycleOutcome.Failed ? "Native exit failed." : "");

        [TestMethod]
        public void RoundTrip_PreservesSameBoundaryOrderingAndSlotIdentity()
        {
            var slot = new byte[] { 1, 2, 3 };
            var hash = Sha256Utility.ComputeHex(slot);
            var source = new ReplayLifecycleLog(Root, new[] { Menu(0),
                new ReplayLifecycleRecord(1, 10, Prefix, ReplayLifecycleKind.LoadSlot,
                    4, hash, ReplayLifecycleOutcome.Completed, 100, "") });
            var bytes = source.Serialize();
            var restored = ReplayLifecycleLog.Deserialize(bytes);
            CollectionAssert.AreEqual(bytes, restored.Serialize());
            Assert.IsTrue(restored.IsCompleted);
            Assert.AreEqual(4, restored.Records[1].Slot);
            Assert.AreEqual(100L, restored.Records[1].NativeFrameCount);
            restored.VerifySlotObjects(id => id == hash ? slot : null);
            Assert.ThrowsExactly<InvalidDataException>(() => restored.VerifySlotObjects(_ => null));
            Assert.ThrowsExactly<InvalidDataException>(() => restored.VerifySlotObjects(_ => new byte[] { 4 }));
        }

        [TestMethod]
        public void InvalidOrderingAndUnfinishedContinuationAreRejected()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new ReplayLifecycleLog(Root, new[] { Menu(0), Menu(0) }));
            Assert.ThrowsExactly<ArgumentException>(() => new ReplayLifecycleLog(Root, new[] { Menu(0), Menu(1, 9) }));
            foreach (var outcome in new[] { ReplayLifecycleOutcome.Waiting, ReplayLifecycleOutcome.Failed })
            {
                var pending = new ReplayLifecycleLog(Root, new[] { Menu(0, outcome: outcome) });
                Assert.IsFalse(ReplayLifecycleLog.Deserialize(pending.Serialize()).IsCompleted);
                Assert.ThrowsExactly<ArgumentException>(() => new ReplayLifecycleLog(Root,
                    new[] { Menu(0, outcome: outcome), Menu(1) }));
            }
        }

        [TestMethod]
        public void ModdedSettingsDistinguishUnknownAbsentAndEmptyFile()
        {
            var slot = new byte[] { 1 };
            var slotHash = Sha256Utility.ComputeHex(slot);
            var emptyHash = Sha256Utility.ComputeHex(Array.Empty<byte>());
            foreach (var modded in new string?[] { null, string.Empty, emptyHash })
            {
                var log = new ReplayLifecycleLog(Root, new[] { new ReplayLifecycleRecord(0, 0, Prefix,
                    ReplayLifecycleKind.LoadSlot, 4, slotHash, ReplayLifecycleOutcome.Completed, 1, "", modded) });
                var restored = ReplayLifecycleLog.Deserialize(log.Serialize());
                Assert.AreEqual(modded, restored.Records[0].ModdedSlotObjectSha256);
                restored.VerifySlotObjects(hash => hash == slotHash ? slot : Array.Empty<byte>());
                if (modded == emptyHash)
                    Assert.ThrowsExactly<InvalidDataException>(() => restored.VerifySlotObjects(hash => hash == slotHash ? slot : null));
                if (modded == null)
                {
                    var legacy = log.Serialize().Take(log.Serialize().Length - 1).ToArray();
                    BitConverter.GetBytes(1).CopyTo(legacy, 4);
                    Assert.IsNull(ReplayLifecycleLog.Deserialize(legacy).Records[0].ModdedSlotObjectSha256);
                }
            }
        }

        [TestMethod]
        public void CorruptOrUnknownBinaryIsRejected()
        {
            var bytes = new ReplayLifecycleLog(Root, new[] { Menu(0) }).Serialize();
            Assert.ThrowsExactly<InvalidDataException>(() => ReplayLifecycleLog.Deserialize(bytes.Concat(new byte[] { 0 }).ToArray()));
            Assert.ThrowsExactly<InvalidDataException>(() => ReplayLifecycleLog.Deserialize(bytes.Take(7).ToArray()));
            bytes[4] = 99;
            Assert.ThrowsExactly<InvalidDataException>(() => ReplayLifecycleLog.Deserialize(bytes));
        }
    }
}
