using System;
using System.IO;
using HollowKnightTAS.Companion.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class StudioQuickSlotsTests
    {
        [TestMethod]
        public void NewStore_HasTenEmptySlots()
        {
            var path = TemporaryPath();
            try
            {
                var store = new StudioQuickSlots(path);

                Assert.AreEqual(10, store.Slots.Length);
                foreach (var slot in store.Slots)
                {
                    Assert.AreEqual(string.Empty, slot.SaveId);
                    Assert.AreEqual(-1, slot.Tick);
                    Assert.AreEqual(string.Empty, slot.PendingLabel);
                }
            }
            finally { DeleteIfExists(path); }
        }

        [TestMethod]
        public void Set_PersistsAndCanBeReloaded()
        {
            var path = TemporaryPath();
            try
            {
                var store = new StudioQuickSlots(path);
                store.Set(3, "save-003", 1234, "pending-003");

                var reloaded = new StudioQuickSlots(path);
                Assert.AreEqual("save-003", reloaded.Slots[3].SaveId);
                Assert.AreEqual(1234, reloaded.Slots[3].Tick);
                Assert.AreEqual("pending-003", reloaded.Slots[3].PendingLabel);
                Assert.AreEqual(string.Empty, reloaded.Slots[2].SaveId);
            }
            finally { DeleteIfExists(path); }
        }

        [TestMethod]
        public void Set_RejectsInvalidIndices()
        {
            var path = TemporaryPath();
            try
            {
                var store = new StudioQuickSlots(path);

                Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => store.Set(-1, "save", 1, "label"));
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => store.Set(10, "save", 1, "label"));
                Assert.IsFalse(File.Exists(path));
            }
            finally { DeleteIfExists(path); }
        }

        [TestMethod]
        public void CorruptConfiguration_IsRejectedWithoutOverwritingOriginal()
        {
            var path = TemporaryPath();
            const string corrupt = "[]";
            try
            {
                File.WriteAllText(path, corrupt);

                Assert.ThrowsExactly<InvalidDataException>(
                    () => new StudioQuickSlots(path));
                Assert.AreEqual(corrupt, File.ReadAllText(path));
            }
            finally { DeleteIfExists(path); }
        }

        [TestMethod]
        public void Reconcile_KeepsOldReferenceUntilMatchingEntryIsReady()
        {
            var path = TemporaryPath();
            try
            {
                var store = new StudioQuickSlots(path);
                store.Set(0, "old-save", 7, "restore-label");

                store.Reconcile("[{\"id\":\"new-save\",\"label\":\"restore-label\",\"status\":\"Pending\",\"effectiveMovieTick\":20}]");
                Assert.AreEqual("old-save", store.Slots[0].SaveId);
                Assert.AreEqual(7, store.Slots[0].Tick);
                Assert.AreEqual("restore-label", store.Slots[0].PendingLabel);

                store.Reconcile("[{\"id\":\"new-save\",\"label\":\"restore-label\",\"status\":\"Ready\",\"effectiveMovieTick\":20}]");
                Assert.AreEqual("new-save", store.Slots[0].SaveId);
                Assert.AreEqual(20, store.Slots[0].Tick);
                Assert.AreEqual(string.Empty, store.Slots[0].PendingLabel);
            }
            finally { DeleteIfExists(path); }
        }

        [TestMethod]
        public void Reconcile_UsesExactPendingLabelAndDoesNotBindUnrelatedAutosave()
        {
            var path = TemporaryPath();
            try
            {
                var store = new StudioQuickSlots(path);
                store.Set(0, "old-zero", 1, "manual-label");
                store.Set(1, "old-one", 2, "other-label");

                store.Reconcile("["
                    + "{\"id\":\"autosave-1\",\"label\":\"autosave-label\",\"status\":\"Ready\",\"effectiveMovieTick\":99},"
                    + "{\"id\":\"manual-save\",\"label\":\"manual-label\",\"status\":\"Ready\",\"effectiveMovieTick\":30}"
                    + "]");

                Assert.AreEqual("manual-save", store.Slots[0].SaveId);
                Assert.AreEqual(30, store.Slots[0].Tick);
                Assert.AreEqual(string.Empty, store.Slots[0].PendingLabel);
                Assert.AreEqual("old-one", store.Slots[1].SaveId);
                Assert.AreEqual(2, store.Slots[1].Tick);
                Assert.AreEqual("other-label", store.Slots[1].PendingLabel);
            }
            finally { DeleteIfExists(path); }
        }

        private static string TemporaryPath()
            => Path.Combine(Path.GetTempPath(), "hktas-quick-slots-" + Guid.NewGuid().ToString("N") + ".json");

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
