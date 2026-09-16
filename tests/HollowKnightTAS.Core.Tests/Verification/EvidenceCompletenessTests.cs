using System;
using System.IO;
using HollowKnightTAS.Core.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Verification
{
    [TestClass]
    public sealed class EvidenceCompletenessTests
    {
        [TestMethod]
        public void SidecarRejectsEvenOtherwiseValidEvidenceWithoutDeletingIt()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "run.json");
            try
            {
                File.WriteAllText(path, "{}");
                EvidenceCompleteness.RequireComplete(path);
                File.WriteAllText(path + ".incomplete", "quota reached");
                Assert.ThrowsExactly<InvalidDataException>(() => EvidenceCompleteness.RequireComplete(path));
                Assert.AreEqual("{}", File.ReadAllText(path));
                Assert.IsTrue(File.Exists(path + ".incomplete"));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
