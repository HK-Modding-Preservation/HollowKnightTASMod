using System;
using System.IO;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplaySlotOverwriteAuthorizationTests
    {
        [TestMethod]
        public void ConsentBindsExecutionAndExactOriginalFilesAcrossSerialization()
        {
            var data = new byte[] { 1, 2 };
            var consent = new ReplaySlotOverwriteAuthorization(new string('a', 64), new string('b', 64),
                new string('c', 64), 42, new[] {
                    new ReplaySlotFileIdentity(2, Sha256Utility.ComputeHex(data), Sha256Utility.ComputeHex(Array.Empty<byte>())),
                    new ReplaySlotFileIdentity(1, string.Empty, string.Empty) });
            var restored = ReplaySlotOverwriteAuthorization.Deserialize(consent.Serialize());
            Assert.AreEqual(consent.Serialize(), restored.Serialize());
            Assert.AreEqual(1, restored.Slots[0].Slot);
            Assert.IsTrue(restored.MatchesExecution(new string('a', 64), new string('b', 64), new string('c', 64), 42));
            Assert.IsFalse(restored.MatchesExecution(new string('a', 64), new string('b', 64), new string('d', 64), 42));
            Assert.IsFalse(restored.MatchesExecution(new string('a', 64), new string('b', 64), new string('c', 64), 43));
            Assert.IsTrue(restored.Allows(2, data, Array.Empty<byte>()));
            Assert.IsFalse(restored.Allows(2, data, null));
            Assert.IsFalse(restored.Allows(2, new byte[] { 1, 3 }, Array.Empty<byte>()));
            Assert.IsFalse(restored.Allows(3, data, Array.Empty<byte>()));
            Assert.IsTrue(restored.Allows(1, null, null));
            Assert.IsFalse(restored.Allows(1, Array.Empty<byte>(), null));
        }

        [TestMethod]
        public void ConsentRejectsDuplicatesNoncanonicalDataAndInvalidSlots()
        {
            var slot = new ReplaySlotFileIdentity(1, string.Empty, string.Empty);
            Assert.ThrowsExactly<ArgumentException>(() => new ReplaySlotOverwriteAuthorization(new string('a', 64),
                new string('b', 64), new string('c', 64), 0, new[] { slot, slot }));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReplaySlotFileIdentity(5, "", ""));
            var consent = new ReplaySlotOverwriteAuthorization(new string('a', 64), new string('b', 64),
                new string('c', 64), 0, new[] { slot });
            Assert.ThrowsExactly<InvalidDataException>(() => ReplaySlotOverwriteAuthorization.Deserialize(consent.Serialize() + "\n"));
            Assert.ThrowsExactly<InvalidDataException>(() => ReplaySlotOverwriteAuthorization.Deserialize(consent.Serialize().Replace("\n0\n", "\n00\n")));
        }
    }
}
