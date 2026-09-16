using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.Keyframes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Keyframes
{
    [TestClass]
    public sealed class SemanticKeyframeStoreTests
    {
        [TestMethod]
        public void CommitLoadAndSharedAssociation_AreIndependentOfT09()
        {
            var root = TemporaryDirectory();
            try
            {
                var fixture = KeyframeTestFactory.CreateFixture();
                KeyframeTestFactory.PublishBaseline(root, fixture);
                var store = new SemanticKeyframeArtifactStore(root);
                var commit = new SemanticKeyframeCommit(
                    "save-a",
                    300,
                    fixture.Descriptor,
                    fixture.Objects);

                Assert.IsTrue(store.Commit(commit).Success);
                Assert.IsTrue(store.Load("keyframe-a").Success);
                Assert.IsTrue(store.FindForReplaySave("save-a").Success);
                Assert.IsTrue(
                    store.Associate(
                        "save-b",
                        "keyframe-a",
                        420).Success);
                Assert.HasCount(2, store.ListAssociations());

                Assert.IsTrue(store.RemoveAssociation("save-a"));
                Assert.IsFalse(
                    store.RemoveUnreferencedDescriptor("keyframe-a"));
                Assert.IsTrue(store.FindForReplaySave("save-b").Success);
                Assert.IsTrue(store.RemoveAssociation("save-b"));

                var objectCount = ObjectFiles(root).Count;
                Assert.IsTrue(
                    store.RemoveUnreferencedDescriptor("keyframe-a"));
                Assert.AreEqual(objectCount, ObjectFiles(root).Count);
                Assert.IsTrue(
                    File.Exists(
                        ObjectPath(
                            root,
                            fixture.Descriptor
                                .BaselineObjectSha256)),
                    "T09 baseline object must remain untouched.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void CorruptOrMissingObject_FailsClosedWithoutIndexRewrite()
        {
            var root = TemporaryDirectory();
            try
            {
                var fixture = KeyframeTestFactory.CreateFixture();
                KeyframeTestFactory.PublishBaseline(root, fixture);
                var store = new SemanticKeyframeArtifactStore(root);
                Assert.IsTrue(
                    store.Commit(
                        new SemanticKeyframeCommit(
                            "save-a",
                            300,
                            fixture.Descriptor,
                            fixture.Objects)).Success);

                var payloadHash =
                    fixture.Descriptor.Adapters[0].PayloadSha256;
                File.WriteAllBytes(
                    ObjectPath(root, payloadHash),
                    new byte[] { 1, 2, 3 });
                var reopened =
                    new SemanticKeyframeArtifactStore(root);
                Assert.AreEqual(
                    KeyframeArtifactStatus.Corrupt,
                    reopened.Load("keyframe-a").Status);
                Assert.AreEqual(
                    KeyframeArtifactStatus.NotFound,
                    reopened.FindForReplaySave("save-a").Status);
                Assert.IsTrue(
                    File.Exists(
                        ObjectPath(
                            root,
                            fixture.Descriptor
                                .BaselineObjectSha256)));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void EveryCrashStage_PublishesIndexOnlyAtFinalStage()
        {
            foreach (KeyframeTransactionStage stage in Enum.GetValues(
                         typeof(KeyframeTransactionStage)))
            {
                var root = TemporaryDirectory();
                try
                {
                    var fixture =
                        KeyframeTestFactory.CreateFixture(
                            "keyframe-" + (byte)stage);
                    KeyframeTestFactory.PublishBaseline(root, fixture);
                    var crashing = new SemanticKeyframeArtifactStore(
                        root,
                        value =>
                        {
                            if (value == stage)
                            {
                                throw new KeyframeSimulatedCrashException(
                                    value);
                            }
                        });
                    Assert.ThrowsExactly<
                        KeyframeSimulatedCrashException>(
                        () => crashing.Commit(
                            new SemanticKeyframeCommit(
                                "save-a",
                                300,
                                fixture.Descriptor,
                                fixture.Objects)),
                        stage.ToString());

                    var reopened =
                        new SemanticKeyframeArtifactStore(root);
                    var report = reopened.Recover();
                    var indexed =
                        stage
                        == KeyframeTransactionStage.IndexPublished;
                    Assert.AreEqual(
                        indexed,
                        reopened.FindForReplaySave("save-a").Success,
                        stage.ToString());
                    Assert.AreEqual(
                        indexed ? 0 : 1,
                        report.IncompleteTransactions.Count,
                        stage.ToString());
                }
                finally
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [TestMethod]
        public void TemporaryAndIncompleteFiles_AreNeverCandidates()
        {
            var root = TemporaryDirectory();
            try
            {
                var fixture = KeyframeTestFactory.CreateFixture();
                KeyframeTestFactory.PublishBaseline(root, fixture);
                var store = new SemanticKeyframeArtifactStore(root);
                var entryDirectory = Path.Combine(
                    root,
                    "keyframes",
                    "entries");
                File.WriteAllText(
                    Path.Combine(
                        entryDirectory,
                        "ghost.json.tmp-interrupted"),
                    "{}");
                var transaction = Path.Combine(
                    root,
                    "keyframes",
                    "transactions",
                    "interrupted");
                Directory.CreateDirectory(transaction);
                File.WriteAllText(
                    Path.Combine(transaction, "state.json"),
                    "{\"stage\":\"ObjectsPublished\"}");

                var report = store.Recover();
                Assert.HasCount(1, report.IncompleteTransactions);
                Assert.AreEqual(
                    KeyframeArtifactStatus.NotFound,
                    store.FindForReplaySave("save-a").Status);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void CorruptIndex_IsQuarantinedAndReplacedFailClosed()
        {
            var root = TemporaryDirectory();
            try
            {
                var fixture = KeyframeTestFactory.CreateFixture();
                KeyframeTestFactory.PublishBaseline(root, fixture);
                var store = new SemanticKeyframeArtifactStore(root);
                Assert.IsTrue(
                    store.Commit(
                        new SemanticKeyframeCommit(
                            "save-a",
                            300,
                            fixture.Descriptor,
                            fixture.Objects)).Success);
                var indexPath = Path.Combine(
                    root,
                    "keyframes",
                    "index.json");
                File.WriteAllText(indexPath, "{\"corrupt\":true}");

                var reopened =
                    new SemanticKeyframeArtifactStore(root);
                var report = reopened.Recover();
                Assert.IsTrue(
                    report.InvalidArtifacts.Any(
                        value => value.StartsWith(
                            "index.json:",
                            StringComparison.Ordinal)));
                Assert.AreEqual(
                    KeyframeArtifactStatus.NotFound,
                    reopened.FindForReplaySave("save-a").Status);
                Assert.HasCount(
                    1,
                    Directory.EnumerateFiles(
                            Path.Combine(
                                root,
                                "keyframes",
                                "quarantine"),
                            "*.corrupt.json")
                        .ToArray());
                Assert.HasCount(
                    0,
                    KeyframeArtifactIndexCodec.Deserialize(
                            File.ReadAllBytes(indexPath))
                        .Entries);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string TemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "HollowKnightTAS-Keyframes-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ObjectPath(
            string root,
            string hash)
        {
            return Path.Combine(
                root,
                "objects",
                "sha256",
                hash.Substring(0, 2),
                hash);
        }

        private static System.Collections.Generic.List<string>
            ObjectFiles(string root)
        {
            return Directory.EnumerateFiles(
                    Path.Combine(root, "objects", "sha256"),
                    "*",
                    SearchOption.AllDirectories)
                .ToList();
        }
    }
}
