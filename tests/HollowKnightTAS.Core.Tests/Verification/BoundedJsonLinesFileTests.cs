using System;
using System.Collections.Generic;
using System.IO;
using HollowKnightTAS.Core.Diagnostics;
using HollowKnightTAS.Core.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class BoundedJsonLinesFileTests
    {
        [TestMethod]
        public void ExactUtf8LimitCompletesAndExistingEvidenceIsProtected()
        {
            WithPath(path =>
            {
                Assert.IsTrue(BoundedJsonLinesFile.Write(path, new[] { "中" }, 4));
                Assert.AreEqual(4L, new FileInfo(path).Length);
                Assert.AreEqual("中\n", File.ReadAllText(path));
                Assert.IsFalse(File.Exists(path + ".incomplete"));
                Assert.ThrowsExactly<IOException>(() => BoundedJsonLinesFile.Write(path, new[] { "x" }));
                Assert.AreEqual("中\n", File.ReadAllText(path));
            });
        }

        [TestMethod]
        public void LimitPreservesWholeLinesAndRejectsEvidence()
        {
            WithPath(path =>
            {
                Assert.IsFalse(BoundedJsonLinesFile.Write(path, new[] { "{}", "中" }, 6));
                Assert.AreEqual("{}\n", File.ReadAllText(path));
                Assert.ThrowsExactly<InvalidDataException>(() => EvidenceCompleteness.RequireComplete(path));
            });
        }

        [TestMethod]
        public void SerializationFailureRemainsMarkedIncomplete()
        {
            WithPath(path =>
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => BoundedJsonLinesFile.Write(path, FailingLines()));
                Assert.IsTrue(File.Exists(path + ".incomplete"));
            });
        }

        private static IEnumerable<string> FailingLines()
        {
            yield return "{}";
            throw new InvalidOperationException("synthetic serialization failure");
        }

        private static void WithPath(Action<string> check)
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-trace-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { check(Path.Combine(root, "trace.jsonl")); }
            finally { Directory.Delete(root, true); }
        }
    }
}
