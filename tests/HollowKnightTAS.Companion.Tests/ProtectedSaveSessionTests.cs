using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class ProtectedSaveSessionTests
    {
        [TestMethod]
        public void Prepare_ReadOnlyCopiesAllSlotSideFilesAndRecordsAbsentSlots()
        {
            using var fixture = new TemporaryRoots();
            Directory.CreateDirectory(fixture.Original);
            var first = Path.Combine(fixture.Original, "user1.dat");
            var side = Path.Combine(fixture.Original, "user1.modded.json");
            var fourth = Path.Combine(fixture.Original, "user4.dat.bak");
            File.WriteAllBytes(first, new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(side, "{\"mod\":true}", Encoding.UTF8);
            File.WriteAllBytes(fourth, new byte[] { 4, 3, 2, 1 });
            File.WriteAllText(Path.Combine(fixture.Original, "notes.txt"), "unrelated");

            var session = ProtectedSaveSession.Prepare("run-1", fixture.Original, fixture.Shadow);
            var decoded = ProtectedSaveDescriptorCodec.Parse(File.ReadAllBytes(session.DescriptorPath));
            Assert.AreEqual(session.Descriptor.GuardToken, decoded.GuardToken);
            Assert.AreEqual(session.Descriptor.ShadowRoot, decoded.ShadowRoot);
            Assert.AreEqual(session.Descriptor.OriginalRoot, decoded.OriginalRoot);
            Assert.AreEqual(3, session.OriginalSha256.Count);
            CollectionAssert.AreEqual(new[] { 2, 3 }, session.Descriptor.AbsentSlots.ToArray());
            Assert.AreEqual(4L, session.OriginalLengths["user1.dat"]);
            foreach (var pair in session.OriginalSha256)
            {
                Assert.AreEqual(Sha256Utility.ComputeFileHex(Path.Combine(fixture.Original, pair.Key)), pair.Value);
                Assert.AreEqual(pair.Value, Sha256Utility.ComputeFileHex(Path.Combine(fixture.Shadow, pair.Key)));
            }
            Assert.IsFalse(File.Exists(Path.Combine(fixture.Shadow, "notes.txt")));
            File.WriteAllBytes(Path.Combine(fixture.Shadow, "user1.dat"), new byte[] { 9 });
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(first));
        }

        [TestMethod]
        public void Prepare_RejectsOverlappingAndInvalidInputsBeforeCopy()
        {
            using var fixture = new TemporaryRoots();
            Directory.CreateDirectory(fixture.Original);
            Assert.Throws<InvalidDataException>(() =>
                ProtectedSaveSession.Prepare("run-1", fixture.Original,
                    Path.Combine(fixture.Original, "shadow")));
            Assert.Throws<ArgumentException>(() =>
                ProtectedSaveSession.Prepare("../unsafe", fixture.Original, fixture.Shadow));
            Assert.IsFalse(Directory.Exists(fixture.Shadow));

            Directory.CreateDirectory(Path.Combine(fixture.Original, "user2.dat"));
            Assert.Throws<InvalidDataException>(() =>
                ProtectedSaveSession.Prepare("run-2", fixture.Original, fixture.Shadow));
            Assert.IsFalse(Directory.Exists(fixture.Shadow));
            Directory.Delete(Path.Combine(fixture.Original, "user2.dat"));

            using (var file = new FileStream(Path.Combine(fixture.Original, "user3.dat"),
                       FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(ProtectedSaveSession.MaximumFileBytes + 1);
            Assert.Throws<InvalidDataException>(() =>
                ProtectedSaveSession.Prepare("run-3", fixture.Original, fixture.Shadow));
            Assert.IsFalse(Directory.Exists(fixture.Shadow));
        }

        [TestMethod]
        public void Descriptor_CopiesHashMapAndRejectsDuplicateNames()
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user1.dat"] = new string('a', 64)
            };
            var descriptor = new ProtectedSaveDescriptor("run-1", "original", "shadow", hashes,
                new string('b', 64));
            hashes["user1.dat"] = new string('c', 64);
            Assert.AreEqual(new string('a', 64), descriptor.OriginalFileSha256["user1.dat"]);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, descriptor.AbsentSlots.ToArray());

            var duplicate = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user1.dat"] = new string('a', 64),
                ["USER1.DAT"] = new string('b', 64)
            };
            Assert.Throws<ArgumentException>(() =>
                new ProtectedSaveDescriptor("run-1", "original", "shadow", duplicate, new string('c', 64)));
        }

        [TestMethod]
        public void DescriptorCodec_RejectsInvalidAndOversizedBytes()
        {
            Assert.Throws<Exception>(() => ProtectedSaveDescriptorCodec.Parse(new byte[] { 1, 2, 3 }));
            Assert.Throws<InvalidDataException>(() => ProtectedSaveDescriptorCodec.Parse(
                new byte[ProtectedSaveDescriptorCodec.MaximumBytes + 1]));
        }

        private sealed class TemporaryRoots : IDisposable
        {
            private readonly string root;
            public TemporaryRoots()
            {
                root = Path.Combine(Path.GetTempPath(), "HollowKnightTAS-ProtectedSaveTests",
                    Guid.NewGuid().ToString("N"));
                Original = Path.Combine(root, "original");
                Shadow = Path.Combine(root, "shadow");
            }
            public string Original { get; }
            public string Shadow { get; }
            public void Dispose()
            {
                var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                    "HollowKnightTAS-ProtectedSaveTests"));
                var full = Path.GetFullPath(root);
                if (!full.StartsWith(expectedParent + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected temporary test path.");
                if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            }
        }
    }
}
