using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class LifecycleMovieBranchStoreTests
    {
        [TestMethod]
        public void BranchCatalogPaginatesDeduplicatesAndDoesNotClaimValidation()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-catalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var artifacts = Path.Combine(root, "artifacts");
                var library = Path.Combine(root, "movie-library");
                var workspace = new MoviePatchWorkspace(artifacts, "current", library);
                var old = Path.Combine(artifacts, "old", "movie-branches");
                Directory.CreateDirectory(old);
                for (var n = 0; n < 52; n++)
                {
                    var name = n.ToString("x64") + ".hktas";
                    File.WriteAllText(Path.Combine(library, name), "invalid content deliberately not read by listing");
                    File.SetLastWriteTimeUtc(Path.Combine(library, name), DateTime.UnixEpoch.AddSeconds(n));
                    File.WriteAllText(Path.Combine(old, name), "duplicate legacy entry");
                }
                File.WriteAllText(Path.Combine(library, "not-a-branch.hktas"), "ignored");
                var first = workspace.ListBranches(0);
                var last = workspace.ListBranches(50);
                Assert.AreEqual(50, first.Count);
                Assert.AreEqual(2, last.Count);
                Assert.AreEqual(51.ToString("x64"), first[0].BranchId);
                Assert.AreEqual(0.ToString("x64"), last[1].BranchId);
                Assert.IsTrue(first.All(x => x.Verification == "NotChecked"));
                Assert.AreEqual(0, workspace.ListBranches(52).Count);
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => workspace.ListBranches(-1));
                Assert.ThrowsExactly<InvalidDataException>(() => workspace.ReadBranch(first[0].BranchId));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void PersistentLibraryReadsLegacyBranchAndKeepsContinuedEditsAcrossSessions()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-branch-library-" + Guid.NewGuid().ToString("N"));
            try
            {
                var artifacts = Path.Combine(root, "artifacts");
                var library = Path.Combine(root, "movie-library");
                var source = Movie(5, TasAction.None);
                var writer = new MovieCanonicalWriter();
                var bytes = writer.WriteUtf8(source);
                var slot = new byte[] { 1, 2 };
                var slotHash = Sha256Utility.ComputeHex(slot);
                var log = Log(source, slotHash);
                var replacement = writer.WriteUtf8(Movie(1, TasAction.Right));
                var legacy = new MoviePatchWorkspace(artifacts, "old-session");
                var first = legacy.EditLifecycleTimeline(legacy.GetLifecycleSourceId(bytes, log), bytes,
                    log, new Dictionary<string, byte[]> { [slotHash] = slot }, TimelineEditKind.Insert, 0, 0, replacement);
                var originalPath = Path.Combine(artifacts, "old-session", "movie-branches", "lifecycle", first.BranchId + ".hklbranch");
                var originalBytes = File.ReadAllBytes(originalPath);
                var current = new MoviePatchWorkspace(artifacts, "new-session", library);
                Assert.IsTrue(current.HasLifecycleBranch(first.BranchId));
                Assert.AreEqual(first.BranchId, current.ReadLifecycleBranch(first.BranchId).BranchId);
                var continued = current.EditLifecycleBranch(first.BranchId, TimelineEditKind.Insert, 1, 0, replacement);
                CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(originalPath));
                Assert.IsTrue(File.Exists(Path.Combine(library, "lifecycle", continued.BranchId + ".hklbranch")));
                var reopened = new MoviePatchWorkspace(artifacts, "third-session", library);
                var result = reopened.ReadLifecycleBranch(continued.BranchId);
                Assert.AreEqual(7L, result.Plan.InputEdit.ExpandedTicks);
                CollectionAssert.AreEqual(log.Serialize(), result.Plan.Source.Serialize());
                CollectionAssert.AreEqual(slot, result.CopySlotObjects()[slotHash]);
                Assert.IsFalse(reopened.HasLifecycleBranch("../outside"));
                Assert.ThrowsExactly<InvalidDataException>(() => reopened.ReadLifecycleBranch("../outside"));
                var plain = legacy.Propose("none", "none", bytes);
                Assert.IsTrue(plain.Success);
                CollectionAssert.AreEqual(bytes, reopened.ReadBranch(plain.BranchMovieId));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void CorruptLegacyBranchCannotBeImportedIntoPersistentLibrary()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-branch-library-" + Guid.NewGuid().ToString("N"));
            try
            {
                var artifacts = Path.Combine(root, "artifacts");
                var library = Path.Combine(root, "movie-library");
                var legacyPath = Path.Combine(artifacts, "old-session", "movie-branches", "lifecycle");
                Directory.CreateDirectory(legacyPath);
                var id = new string('a', 64);
                File.WriteAllBytes(Path.Combine(legacyPath, id + ".hklbranch"), new byte[] { 1, 2, 3 });
                var workspace = new MoviePatchWorkspace(artifacts, "new-session", library);
                Assert.ThrowsExactly<InvalidDataException>(() => workspace.EditLifecycleBranch(id,
                    TimelineEditKind.Insert, 0, 0, new MovieCanonicalWriter().WriteUtf8(Movie(1, TasAction.None))));
                Assert.IsFalse(File.Exists(Path.Combine(library, "lifecycle", id + ".hklbranch")));
                Assert.IsTrue(File.Exists(Path.Combine(legacyPath, id + ".hklbranch")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static MovieDocument Movie(long count, TasAction action) => new MovieDocument("branch",
            new MovieHeader(MovieProtocolV1.Version, "1.5.78.11833", "1.5.78.11833-77",
                new string('0', 64), "none", "none", MovieProtocolV1.TickUnit),
            new[] { new FrameRunCommand(count, action, 0, 0, false, new MovieSourceSpan("branch", 1, 1, 1)) });
        private static ReplayLifecycleLog Log(MovieDocument movie, string hash) => new ReplayLifecycleLog(
            new string('b', 64), new[] {
                new ReplayLifecycleRecord(0, 2, MoviePrefixIdentity.ComputeSha256(movie, 3),
                    ReplayLifecycleKind.LoadSlot, 4, hash, ReplayLifecycleOutcome.Completed, 10, "", "")
            });

        [TestMethod]
        public void UneditedSourceBranchPreservesInputsAndNativeEvidence()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-lifecycle-branch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var movie = Movie(5, TasAction.Right);
                var writer = new MovieCanonicalWriter();
                var source = writer.WriteUtf8(movie);
                var slot = new byte[] { 1 };
                var hash = Sha256Utility.ComputeHex(slot);
                var log = Log(movie, hash);
                var store = new LifecycleMovieBranchStore(root);
                var saved = store.Store(source, log, new Dictionary<string, byte[]> { [hash] = slot },
                    TimelineEditKind.Replace, 0, 0,
                    writer.WriteUtf8(new MovieDocument("empty", movie.Header, Array.Empty<MovieCommand>())));
                var reopened = store.Read(saved.BranchId);
                CollectionAssert.AreEqual(source, writer.WriteUtf8(reopened.Plan.InputEdit.Movie));
                CollectionAssert.AreEqual(log.Serialize(), reopened.Plan.Source.Serialize());
                Assert.IsFalse(reopened.Plan.Operations[0].RequiresReplay);
                Assert.AreEqual(2L, reopened.Plan.Operations[0].AfterMovieTick);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void RepeatedEditsReopenWithoutParentFileAndPreserveSourceEvidence()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-lifecycle-branch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LifecycleMovieBranchStore(root);
                var movie = Movie(5, TasAction.None);
                var writer = new MovieCanonicalWriter();
                var bytes = new byte[] { 1 };
                var hash = Sha256Utility.ComputeHex(bytes);
                var log = Log(movie, hash);
                var first = store.Store(writer.WriteUtf8(movie), log,
                    new Dictionary<string, byte[]> { [hash] = bytes }, TimelineEditKind.Insert,
                    0, 0, writer.WriteUtf8(Movie(2, TasAction.Right)));
                var second = store.Edit(first.BranchId, TimelineEditKind.Delete, 0, 2,
                    writer.WriteUtf8(new MovieDocument("empty", movie.Header, Array.Empty<MovieCommand>())));
                var third = store.Edit(second.BranchId, TimelineEditKind.Replace, 4, 1,
                    writer.WriteUtf8(Movie(1, TasAction.Attack)));
                Assert.AreEqual(4L, store.Read(first.BranchId).Plan.Operations[0].AfterMovieTick);
                Assert.AreEqual(2L, store.Read(second.BranchId).Plan.Operations[0].AfterMovieTick);
                File.Delete(Path.Combine(root, first.BranchId + ".hklbranch"));
                File.Delete(Path.Combine(root, second.BranchId + ".hklbranch"));
                var reopened = new LifecycleMovieBranchStore(root).Read(third.BranchId);
                Assert.AreEqual(5L, reopened.Plan.InputEdit.ExpandedTicks);
                Assert.AreEqual(2L, reopened.Plan.Operations[0].AfterMovieTick);
                Assert.IsTrue(reopened.Plan.Operations[0].RequiresReplay);
                CollectionAssert.AreEqual(log.Serialize(), reopened.Plan.Source.Serialize());
                CollectionAssert.AreEqual(writer.WriteUtf8(third.Plan.InputEdit.Movie), writer.WriteUtf8(reopened.Plan.InputEdit.Movie));
                CollectionAssert.AreEqual(bytes, reopened.CopySlotObjects()[hash]);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void VersionTwoPreservesRootBaselineAndRejectsUnrelatedRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-lifecycle-branch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var builder = new SemanticSnapshotBuilder();
                builder.AddString("scene.name", "GG_Workshop");
                builder.AddString("game.state", "PLAYING");
                builder.AddString("hero.actorState", "IDLE");
                foreach (var name in new[] { "attacking", "dashing", "falling", "jumping", "onGround", "wallSliding" })
                    builder.AddBoolean("hero.cState." + name, false);
                foreach (var name in new[] { "position.x", "position.y", "velocity.x", "velocity.y" })
                    builder.AddFloat32Bits("hero." + name, 0);
                foreach (var name in new[] { "health", "maxHealth", "mp" }) builder.AddInt32("player." + name, 0);
                var snapshot = builder.Build();
                var semanticHash = SemanticSnapshotHasher.ComputeSha256(snapshot);
                var baseline = BaselineBundleCodec.Serialize(new BaselineBundle(1, "branch-root", semanticHash,
                    4, DateTimeOffset.UnixEpoch, new byte[] { 1, 2 }, null,
                    SemanticSnapshotCanonicalizer.Serialize(snapshot)));
                var original = Movie(5, TasAction.None);
                var movie = new MovieDocument("branch", new MovieHeader(MovieProtocolV1.Version,
                    original.Header.GameVersion, original.Header.ApiVersion, original.Header.ManifestSha256,
                    "branch-root", semanticHash, MovieProtocolV1.TickUnit), original.Commands);
                var source = new MovieCanonicalWriter().WriteUtf8(movie);
                var log = new ReplayLifecycleLog(Sha256Utility.ComputeHex(baseline), Array.Empty<ReplayLifecycleRecord>());
                var replacement = new MovieCanonicalWriter().WriteUtf8(Movie(1, TasAction.Attack));
                var store = new LifecycleMovieBranchStore(root);
                var branch = store.Store(source, log, new Dictionary<string, byte[]>(),
                    TimelineEditKind.Replace, 0, 1, replacement, baseline);
                var reopened = new LifecycleMovieBranchStore(root).Read(branch.BranchId);
                Assert.IsTrue(reopened.HasRootBaseline);
                CollectionAssert.AreEqual(baseline, reopened.CopyRootBaseline());
                reopened.CopyRootBaseline()[0] = 99;
                CollectionAssert.AreEqual(baseline, reopened.CopyRootBaseline());
                var continued = store.Edit(branch.BranchId, TimelineEditKind.Insert, 1, 0, replacement);
                var portable = new LifecycleMovieBranchStore(root).Read(continued.BranchId).CreateExecutionPlan();
                CollectionAssert.AreEqual(baseline, portable.CopyBaseline());
                Assert.AreEqual(continued.BranchId, portable.SourceBranchId);
                baseline[0] ^= 1;
                Assert.ThrowsExactly<InvalidDataException>(() => store.Store(source, log, new Dictionary<string, byte[]>(),
                    TimelineEditKind.Replace, 0, 1, replacement, baseline));
                var legacy = store.Store(source, log, new Dictionary<string, byte[]>(),
                    TimelineEditKind.Replace, 0, 1, replacement);
                Assert.IsFalse(store.Read(legacy.BranchId).HasRootBaseline);
                Assert.ThrowsExactly<InvalidOperationException>(() => legacy.CopyRootBaseline());
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void ReopenPreservesPlanAndDifferentSlotBytesHaveDistinctBranchIdentity()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-lifecycle-branch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LifecycleMovieBranchStore(root);
                var movie = Movie(5, TasAction.None);
                var source = new MovieCanonicalWriter().WriteUtf8(movie);
                var replacement = new MovieCanonicalWriter().WriteUtf8(Movie(1, TasAction.Attack));
                var a = new byte[] { 1, 2, 3 };
                var b = new byte[] { 4, 5, 6 };
                var hashA = Sha256Utility.ComputeHex(a);
                var hashB = Sha256Utility.ComputeHex(b);
                var objects = new Dictionary<string, byte[]> { [hashA] = a, [hashB] = b };
                var first = store.Store(source, Log(movie, hashA), objects, TimelineEditKind.Replace, 0, 1, replacement);
                var second = store.Store(source, Log(movie, hashB), objects, TimelineEditKind.Replace, 0, 1, replacement);
                Assert.AreNotEqual(first.BranchId, second.BranchId);
                Assert.AreEqual(first.BranchId, store.Store(source, Log(movie, hashA), objects,
                    TimelineEditKind.Replace, 0, 1, replacement).BranchId);
                a[0] = 99;
                source[0] = 99;
                var restored = new LifecycleMovieBranchStore(root).Read(first.BranchId);
                Assert.AreEqual(hashA, restored.Plan.Operations[0].Source.SlotObjectSha256);
                Assert.IsTrue(restored.Plan.Operations[0].RequiresReplay);
                Assert.AreEqual(2L, restored.Plan.Operations[0].AfterMovieTick);
                CollectionAssert.AreEqual(new MovieCanonicalWriter().WriteUtf8(first.Plan.InputEdit.Movie),
                    new MovieCanonicalWriter().WriteUtf8(restored.Plan.InputEdit.Movie));
                Assert.AreEqual(2, Directory.GetFiles(root).Length);
                var path = Path.Combine(root, first.BranchId + ".hklbranch");
                var corrupt = File.ReadAllBytes(path);
                corrupt[20] ^= 1;
                File.WriteAllBytes(path, corrupt);
                Assert.ThrowsExactly<InvalidDataException>(() => store.Read(first.BranchId));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void WorkspaceChecksLifecycleSourceIdentityAndReturnsClonedDependencies()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-lifecycle-branch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var workspace = new MoviePatchWorkspace(root, "source");
                var movie = Movie(5, TasAction.None);
                var source = new MovieCanonicalWriter().WriteUtf8(movie);
                var replacement = new MovieCanonicalWriter().WriteUtf8(Movie(1, TasAction.Attack));
                var hash = Sha256Utility.ComputeHex(new byte[] { 1 });
                var log = Log(movie, hash);
                var objects = new Dictionary<string, byte[]> { [hash] = new byte[] { 1 } };
                var id = workspace.GetLifecycleSourceId(source, log);
                Assert.AreNotEqual(id, workspace.GetLifecycleSourceId(source, Log(movie, new string('a', 64))));
                Assert.ThrowsExactly<InvalidOperationException>(() => workspace.EditLifecycleTimeline(
                    new string('0', 64), source, log, objects, TimelineEditKind.Replace, 0, 1, replacement));
                var saved = workspace.EditLifecycleTimeline(id, source, log, objects,
                    TimelineEditKind.Replace, 0, 1, replacement);
                var reopened = new MoviePatchWorkspace(root, "source").ReadLifecycleBranch(saved.BranchId);
                reopened.CopySlotObjects()[hash][0] = 99;
                Assert.AreEqual((byte)1, reopened.CopySlotObjects()[hash][0]);
                Assert.AreEqual(saved.BranchId, reopened.BranchId);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestMethod]
        public void MissingOrWrongDependencyPublishesNothing()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-lifecycle-branch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LifecycleMovieBranchStore(root);
                var movie = Movie(5, TasAction.None);
                var source = new MovieCanonicalWriter().WriteUtf8(movie);
                var replacement = new MovieCanonicalWriter().WriteUtf8(Movie(1, TasAction.Attack));
                var hash = Sha256Utility.ComputeHex(new byte[] { 1 });
                var objects = new Dictionary<string, byte[]>();
                Assert.ThrowsExactly<InvalidDataException>(() => store.Store(source, Log(movie, hash), objects,
                    TimelineEditKind.Replace, 0, 1, replacement));
                objects[hash] = new byte[] { 2 };
                Assert.ThrowsExactly<InvalidDataException>(() => store.Store(source, Log(movie, hash), objects,
                    TimelineEditKind.Replace, 0, 1, replacement));
                Assert.AreEqual(0, Directory.GetFiles(root).Length);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
