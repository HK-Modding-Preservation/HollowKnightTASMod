using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    [TestClass]
    public sealed class ReplaySaveStoreTests
    {
        [TestMethod]
        public void SameContentObjects_AreDeduplicatedAcrossEntries()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var first = ReplaySaveTestFactory.Commit("save-a");
                var second = ReplaySaveTestFactory.Commit("save-b");
                var store = new ContentAddressedReplaySaveStore(directory);

                Assert.IsTrue(store.Commit(first).Success);
                var afterFirst = ObjectFiles(directory).Count;
                Assert.IsTrue(store.Commit(second).Success);
                var afterSecond = ObjectFiles(directory).Count;

                Assert.AreEqual(afterFirst, afterSecond);
                Assert.HasCount(2, store.List());
                Assert.IsTrue(store.Load("save-a").Success);
                Assert.IsTrue(store.Load("save-b").Success);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void EveryTransactionCrashPoint_ExposesOnlyAtomicallyPublishedEntry()
        {
            foreach (ReplaySaveTransactionStage stage in Enum.GetValues(
                         typeof(ReplaySaveTransactionStage)))
            {
                var directory = ReplaySaveTestFactory.TemporaryDirectory();
                try
                {
                    var commit = ReplaySaveTestFactory.Commit(
                        "crash-" + ((byte)stage).ToString("D2"));
                    var crashing = new ContentAddressedReplaySaveStore(
                        directory,
                        afterStage: value =>
                        {
                            if (value == stage)
                            {
                                throw new ReplaySaveSimulatedCrashException(value);
                            }
                        });
                    Assert.ThrowsExactly<ReplaySaveSimulatedCrashException>(
                        () => crashing.Commit(commit),
                        stage.ToString());

                    var recovered = new ContentAddressedReplaySaveStore(directory);
                    var report = recovered.Recover();
                    var shouldBeVisible =
                        stage >= ReplaySaveTransactionStage.EntryPublished;
                    Assert.AreEqual(
                        shouldBeVisible ? 1 : 0,
                        recovered.List().Count,
                        stage.ToString());
                    Assert.AreEqual(
                        shouldBeVisible,
                        recovered.Load(commit.Descriptor.ReplaySaveId).Success,
                        stage.ToString());
                    Assert.IsTrue(
                        shouldBeVisible
                        || report.IncompleteTransactionIds.Count == 1,
                        stage.ToString());
                }
                finally
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [TestMethod]
        public void CorruptObjectAndMissingSegment_ReturnSpecificFailClosedStatus()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var commit = ReplaySaveTestFactory.Commit();
                var store = new ContentAddressedReplaySaveStore(directory);
                Assert.IsTrue(store.Commit(commit).Success);

                File.WriteAllBytes(
                    ObjectPath(
                        directory,
                        commit.Descriptor.BaselineObjectSha256),
                    new byte[] { 7, 7, 7 });
                Assert.AreEqual(
                    ReplaySaveStatus.Corrupt,
                    new ContentAddressedReplaySaveStore(directory)
                        .Load(commit.Descriptor.ReplaySaveId)
                        .Status);
                File.WriteAllBytes(
                    ObjectPath(
                        directory,
                        commit.Descriptor.BaselineObjectSha256),
                    commit.Objects[commit.Descriptor.BaselineObjectSha256]);

                Assert.IsTrue(store.Commit(
                    ReplaySaveTestFactory.Commit("gap-save")).Success);
                var gapCommit = ReplaySaveTestFactory.Commit("gap-save-2");
                Assert.IsTrue(store.Commit(gapCommit).Success);
                File.Delete(
                    ObjectPath(
                        directory,
                        gapCommit.Descriptor
                            .JournalSegmentObjectSha256s.Last()));
                Assert.AreEqual(
                    ReplaySaveStatus.JournalGap,
                    new ContentAddressedReplaySaveStore(directory)
                        .Load(gapCommit.Descriptor.ReplaySaveId)
                        .Status);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void AutomaticRetentionKeepsNewestThreeAndEveryManualEntry()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var store = new ContentAddressedReplaySaveStore(directory);
                for (var index = 0; index < 5; index++)
                {
                    var automatic = ReplaySaveTestFactory.Commit(
                        "auto-" + index,
                        ReplaySaveReason.AutomaticInterval,
                        retention: 3,
                        requestedTick: index,
                        recordCount: 8 + index);
                    Assert.IsTrue(
                        store.Commit(automatic).Success,
                        automatic.Descriptor.ReplaySaveId);
                }

                for (var index = 0; index < 2; index++)
                {
                    Assert.IsTrue(
                        store.Commit(
                                ReplaySaveTestFactory.Commit(
                                    "manual-" + index,
                                    requestedTick: index,
                                    recordCount: 14 + index))
                            .Success);
                }

                var entries = store.List();
                Assert.HasCount(5, entries);
                CollectionAssert.AreEquivalent(
                    new[] { "auto-2", "auto-3", "auto-4" },
                    entries
                        .Where(
                            value => value.Reason
                                     == ReplaySaveReason.AutomaticInterval)
                        .Select(value => value.ReplaySaveId)
                        .ToArray());
                Assert.AreEqual(
                    2,
                    entries.Count(
                        value => value.Reason == ReplaySaveReason.Manual));
                Assert.AreEqual(
                    2,
                    Directory
                        .EnumerateFiles(
                            Path.Combine(directory, "trash"),
                            "*.json")
                        .Count());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void PinningAutomaticEntryMakesItImmuneToLaterRetention()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var store = new ContentAddressedReplaySaveStore(directory);
                Assert.IsTrue(
                    store.Commit(
                            ReplaySaveTestFactory.Commit(
                                "auto-pinned",
                                ReplaySaveReason.AutomaticInterval,
                                1))
                        .Success);
                Assert.IsTrue(store.PinAsManual("auto-pinned").Success);
                Assert.IsTrue(
                    store.Commit(
                            ReplaySaveTestFactory.Commit(
                                "auto-new",
                                ReplaySaveReason.AutomaticInterval,
                                1,
                                requestedTick: 1,
                                recordCount: 9))
                        .Success);

                var entries = store.List();
                Assert.HasCount(2, entries);
                Assert.AreEqual(
                    ReplaySaveReason.Manual,
                    entries.Single(
                        value => value.ReplaySaveId == "auto-pinned").Reason);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void MovieObjects_AreDurableDeduplicatedAndHashChecked()
        {
            var directory = ReplaySaveTestFactory.TemporaryDirectory();
            try
            {
                var store = new ContentAddressedReplaySaveStore(directory);
                var commit = ReplaySaveTestFactory.Commit();
                var movieBytes = commit.Objects[
                    commit.Descriptor.MovieObjectSha256];
                var first = store.PublishMovieObject(movieBytes);
                var second = store.PublishMovieObject(movieBytes);
                Assert.IsTrue(first.Success, first.Error);
                Assert.IsTrue(second.Success, second.Error);
                Assert.AreEqual(
                    commit.Descriptor.MovieObjectSha256,
                    first.Sha256);
                Assert.AreEqual(first.Sha256, second.Sha256);

                var loaded = store.LoadMovieObject(first.Sha256);
                Assert.IsTrue(loaded.Success, loaded.Error);
                CollectionAssert.AreEqual(movieBytes, loaded.Bytes!);
                Assert.HasCount(1, ObjectFiles(directory));

                File.WriteAllBytes(
                    ObjectPath(directory, first.Sha256),
                    new byte[] { 1, 2, 3 });
                var corrupt = store.LoadMovieObject(first.Sha256);
                Assert.IsFalse(corrupt.Success);
                Assert.IsNull(corrupt.Bytes);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static List<string> ObjectFiles(string directory)
        {
            return Directory
                .EnumerateFiles(
                    Path.Combine(directory, "objects", "sha256"),
                    "*",
                    SearchOption.AllDirectories)
                .ToList();
        }

        private static string ObjectPath(string root, string hash)
        {
            return Path.Combine(
                root,
                "objects",
                "sha256",
                hash.Substring(0, 2),
                hash);
        }
    }
}
