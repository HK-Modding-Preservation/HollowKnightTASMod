using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HollowKnightTAS.Core.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Diagnostics
{
    [TestClass]
    public sealed class DirectoryLogBudgetTests
    {
        [TestMethod]
        public void ConcurrentReservationsShareOneCeilingAndReleaseUnusedSpace()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                var leases = new DirectoryLogBudget[4];
                Parallel.For(0, 4, i => leases[i] = DirectoryLogBudget.Reserve(root,
                    Path.Combine(root, i + ".jsonl"), 80, 100));
                Assert.AreEqual(100L, leases.Sum(value => value.GrantedBytes));
                foreach (var lease in leases) lease.Dispose();
                using var fresh = DirectoryLogBudget.Reserve(root, Path.Combine(root, "next.jsonl"), 80, 100);
                Assert.AreEqual(80L, fresh.GrantedBytes);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ExistingEvidenceCountsWithoutDeletionAndExhaustionMarksIncomplete()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var retained = Path.Combine(root, "old.jsonl");
                File.WriteAllBytes(retained, new byte[100]);
                var output = Path.Combine(root, "new.jsonl");
                using (var sink = new JsonLinesEventSink(output, 4, TimeSpan.FromSeconds(2), 80, root, 100))
                {
                    sink.Emit(new StructuredEvent(1, "test", 1, "event", DateTimeOffset.UtcNow,
                        new Dictionary<string, string>()));
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.IsTrue(sink.SizeLimitReached);
                }
                Assert.AreEqual(100L, new FileInfo(retained).Length);
                Assert.AreEqual(0L, new FileInfo(output).Length);
                Assert.IsTrue(File.Exists(output + ".incomplete"));
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ActiveReservationChargesMaximumRatherThanDoubleCountingWrittenBytes()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                var path = Path.Combine(root, "active.jsonl");
                using var active = DirectoryLogBudget.Reserve(root, path, 80, 100);
                File.WriteAllBytes(path, new byte[30]);
                using var other = DirectoryLogBudget.Reserve(root, Path.Combine(root, "other.jsonl"), 80, 100);
                Assert.AreEqual(20L, other.GrantedBytes);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
