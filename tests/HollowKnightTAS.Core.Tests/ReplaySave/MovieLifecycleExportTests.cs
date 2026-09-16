using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class MovieLifecycleExportTests
    {
        [TestMethod]
        public void ChunkedSnapshotRoundTripPreservesBaselineInputsAndCapturedSlot()
        {
            var commit = ReplaySaveTestFactory.Commit();
            var movie = new MovieParser().Parse(new StringReader(Encoding.UTF8.GetString(
                commit.Objects[commit.Descriptor.MovieObjectSha256])), "export").Document!;
            var slot = new byte[MovieLifecycleExport.ChunkBytes + 17];
            slot[0] = 7;
            var hash = Sha256Utility.ComputeHex(slot);
            var log = new ReplayLifecycleLog(commit.Descriptor.BaselineObjectSha256,
                new[] { new ReplayLifecycleRecord(0, 2, MoviePrefixIdentity.ComputeSha256(movie, 3),
                    ReplayLifecycleKind.LoadSlot, 4, hash, ReplayLifecycleOutcome.Completed, 10, "", "") });
            var export = MovieLifecycleExport.Create(movie, commit.Objects[commit.Descriptor.BaselineObjectSha256],
                log, new Dictionary<string, byte[]> { [hash] = slot });
            slot[0] = 99;
            Assert.IsTrue(export.ChunkCount > 1);
            var bytes = Enumerable.Range(0, export.ChunkCount).SelectMany(export.GetChunk).ToArray();
            var decoded = MovieLifecycleExport.Decode(bytes, export.Sha256);
            Assert.AreEqual((byte)7, decoded.CopySlotObjects()[hash][0]);
            decoded.CopySlotObjects()[hash][0] = 99;
            Assert.AreEqual((byte)7, decoded.CopySlotObjects()[hash][0]);
            CollectionAssert.AreEqual(log.Serialize(), decoded.Lifecycle.Serialize());
            CollectionAssert.AreEqual(commit.Objects[commit.Descriptor.BaselineObjectSha256], decoded.CopyBaselineBytes());
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => export.GetChunk(export.ChunkCount));
            bytes[20] ^= 1;
            Assert.ThrowsExactly<InvalidDataException>(() => MovieLifecycleExport.Decode(bytes, export.Sha256));
        }
    }
}
