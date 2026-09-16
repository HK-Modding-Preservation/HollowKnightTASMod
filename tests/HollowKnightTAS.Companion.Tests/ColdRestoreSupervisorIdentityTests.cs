using System;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class ColdRestoreSupervisorIdentityTests
    {
        [TestMethod]
        public void LoadOrCreate_ReusesStableIdentityAndProtectsSecretCopy()
        {
            var root = TemporaryDirectory();
            try
            {
                var first = ColdRestoreSupervisorIdentity.LoadOrCreate(root);
                var expectedSecret = first.ClaimSecret;
                first.ClaimSecret[0] ^= 0xff;

                var second = ColdRestoreSupervisorIdentity.LoadOrCreate(root);
                Assert.AreEqual(
                    first.CompanionInstanceId,
                    second.CompanionInstanceId);
                CollectionAssert.AreEqual(
                    expectedSecret,
                    second.ClaimSecret);
                Assert.AreEqual(32, second.ClaimSecret.Length);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void LoadOrCreate_RejectsChecksumTampering()
        {
            var root = TemporaryDirectory();
            try
            {
                ColdRestoreSupervisorIdentity.LoadOrCreate(root);
                var path = Path.Combine(root, "identity-v1.txt");
                var bytes = File.ReadAllBytes(path);
                var marker = Encoding.UTF8.GetBytes("companion-");
                var index = Find(bytes, marker);
                Assert.IsGreaterThanOrEqualTo(0, index);
                bytes[index + marker.Length] =
                    bytes[index + marker.Length] == (byte)'a'
                        ? (byte)'b'
                        : (byte)'a';
                File.WriteAllBytes(path, bytes);

                Assert.ThrowsExactly<InvalidDataException>(
                    () => ColdRestoreSupervisorIdentity.LoadOrCreate(root));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void LoadOrCreate_RejectsNonCanonicalTrailingBytes()
        {
            var root = TemporaryDirectory();
            try
            {
                ColdRestoreSupervisorIdentity.LoadOrCreate(root);
                var path = Path.Combine(root, "identity-v1.txt");
                File.AppendAllText(
                    path,
                    "trailing\n",
                    new UTF8Encoding(false));

                Assert.ThrowsExactly<InvalidDataException>(
                    () => ColdRestoreSupervisorIdentity.LoadOrCreate(root));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static int Find(byte[] value, byte[] marker)
        {
            for (var index = 0;
                 index <= value.Length - marker.Length;
                 index++)
            {
                if (marker.SequenceEqual(
                        value.Skip(index).Take(marker.Length)))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string TemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "hktas-cold-identity-tests-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
