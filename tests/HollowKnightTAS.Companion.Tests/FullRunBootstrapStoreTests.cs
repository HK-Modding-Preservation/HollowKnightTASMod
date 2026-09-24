using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.FullRun;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class FullRunBootstrapStoreTests
    {
        private const string Token = "0123456789abcdef0123456789abcdef";

        [TestMethod]
        public void Record_CommitIsOnceAndDescriptorHashMatchesBytes()
        {
            using var roots = new TemporaryRoot();
            var store = new FullRunBootstrapStore(roots.Path);
            var staged = store.StageRecording(Token, "run-1", false);
            var hash = store.CommitAndHash(staged);
            Assert.AreEqual(hash, Sha256Utility.ComputeFileHex(store.GetDescriptorPath(Token)));
            Assert.AreEqual("Record", FullRunBootDescriptor.Parse(
                File.ReadAllBytes(store.GetDescriptorPath(Token))).Mode);
            Assert.Throws<InvalidOperationException>(() => store.CommitAndHash(staged));
        }

        [TestMethod]
        public void Replay_StashesCanonicalMovieWithMatchingHashAndMouseMode()
        {
            using var roots = new TemporaryRoot();
            var header = new MovieV2Header("game", "api", "mod", MovieProtocolV2.NativeProfileId,
                MovieProtocolV2.ActionSchemaId, true, "none", 800, 450);
            var movie = new MovieV2Document("test.hktas", header, new[]
            {
                new NativeFrameRun(1, Array.Empty<GameInputSample>(),
                    new MovieSourceSpan("test.hktas", 2, 1, 1))
            });
            var store = new FullRunBootstrapStore(roots.Path);
            var descriptor = store.StageReplay(Token, "run-1", movie);
            store.CommitAndHash(descriptor);
            Assert.IsTrue(descriptor.MouseEnabled);
            Assert.AreEqual(descriptor.MovieSha256,
                Sha256Utility.ComputeFileHex(descriptor.MoviePath));
            Assert.AreEqual(new MovieV2Codec().WriteCanonical(movie),
                File.ReadAllText(descriptor.MoviePath));
        }

        private sealed class TemporaryRoot : IDisposable
        {
            public TemporaryRoot()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "HKTAS-FullRunBootstrapTests", Guid.NewGuid().ToString("N"));
            }
            public string Path { get; }
            public void Dispose()
            {
                var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "HKTAS-FullRunBootstrapTests"));
                var full = System.IO.Path.GetFullPath(Path);
                if (!full.StartsWith(parent + System.IO.Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected temporary test path.");
                if (!Directory.Exists(full)) return;
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(full, recursive: true);
            }
        }
    }
}
