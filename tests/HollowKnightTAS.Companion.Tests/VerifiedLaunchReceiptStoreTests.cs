using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class VerifiedLaunchReceiptStoreTests
    {
        [TestMethod]
        public void RecordAndRequireValidBindRunProcessProfileAndMac()
        {
            var root = TemporaryDirectory();
            try
            {
                var profile = Profile();
                var secret = Enumerable.Range(0, 32)
                    .Select(value => (byte)value)
                    .ToArray();
                var store = new VerifiedLaunchReceiptStore(
                    root,
                    "companion-receipt-test",
                    secret);
                var startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
                using var handle = new FakeLaunchHandle(
                    43210,
                    startedAtUtc,
                    "{\"status\":\"startup-bridge-loaded\"}");

                var recorded = store.Record(
                    profile,
                    "interactive-receipt-test",
                    handle);
                var loaded = store.RequireValid(
                    profile,
                    "interactive-receipt-test",
                    43210,
                    startedAtUtc.UtcTicks);

                Assert.AreEqual(recorded.RunId, loaded.RunId);
                Assert.AreEqual(recorded.ProcessId, loaded.ProcessId);
                Assert.AreEqual(
                    recorded.LauncherEvidenceSha256,
                    loaded.LauncherEvidenceSha256);
                Assert.ThrowsExactly<InvalidDataException>(
                    () => store.RequireValid(
                        profile,
                        "interactive-receipt-test",
                        43211,
                        startedAtUtc.UtcTicks));

                var receiptPath = Directory.GetFiles(
                        root,
                        "*.launch.json",
                        SearchOption.TopDirectoryOnly)
                    .Single();
                var bytes = File.ReadAllBytes(receiptPath);
                var marker = System.Text.Encoding.UTF8.GetBytes(
                    "startup-bridge-loaded");
                Assert.IsFalse(Contains(bytes, marker));
                bytes[bytes.Length / 2] ^= 1;
                File.WriteAllBytes(receiptPath, bytes);
                Assert.ThrowsExactly<InvalidDataException>(
                    () => store.RequireValid(
                        profile,
                        "interactive-receipt-test",
                        43210,
                        startedAtUtc.UtcTicks));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static VerifiedStartupProfile Profile()
        {
            var constructor = typeof(VerifiedStartupProfile)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Single();
            return (VerifiedStartupProfile)constructor.Invoke(
                new object[]
                {
                    "C:\\bundle",
                    "C:\\game\\hollow_knight.exe",
                    "C:\\bundle\\HollowKnightTAS.ClockInjector.exe",
                    "C:\\bundle\\clock-build-manifest-v1.json",
                    Hash('a'),
                    Hash('b'),
                    Hash('c'),
                    Hash('d')
                });
        }

        private static bool Contains(byte[] source, byte[] pattern)
        {
            for (var index = 0;
                 index <= source.Length - pattern.Length;
                 index++)
            {
                var matched = true;
                for (var offset = 0; offset < pattern.Length; offset++)
                {
                    if (source[index + offset] == pattern[offset])
                    {
                        continue;
                    }

                    matched = false;
                    break;
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Hash(char value)
        {
            return new string(value, 64);
        }

        private static string TemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "hktas-launch-receipt-tests-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class FakeLaunchHandle : IColdRestoreGameLaunchHandle
        {
            public FakeLaunchHandle(
                int processId,
                DateTimeOffset processStartedAtUtc,
                string launcherEvidence)
            {
                ProcessId = processId;
                ProcessStartedAtUtc = processStartedAtUtc;
                LauncherEvidence = launcherEvidence;
            }

            public int ProcessId { get; }
            public DateTimeOffset ProcessStartedAtUtc { get; }
            public string LauncherEvidence { get; }
            public bool HasExited => false;

            public void ReleaseSupervision()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
