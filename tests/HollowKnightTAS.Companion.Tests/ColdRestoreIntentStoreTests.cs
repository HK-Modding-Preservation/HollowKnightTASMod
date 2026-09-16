using System;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.ReplaySave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class ColdRestoreIntentStoreTests
    {
        [TestMethod]
        public void FailureDiagnostic_IsBoundedDurableAndDoesNotChangeChain()
        {
            var root = TemporaryDirectory();
            try
            {
                var store = new ColdRestoreIntentStore(root, "companion-0001", Secret());
                var intent = CreateIntent();
                var prepared = store.Prepare(intent, "claim-0001", intent.CreatedAtUtc,
                    intent.CreatedAtUtc.AddMinutes(5));
                store.PersistFailureDiagnostic(intent.OperationId,
                    new InvalidOperationException("first failure " + new string('x', 20000)));
                var file = Path.Combine(root, "operations", intent.OperationId, "failure.txt");
                var first = File.ReadAllText(file);
                StringAssert.Contains(first, "first failure");
                StringAssert.EndsWith(first, "[truncated]");
                Assert.IsTrue(first.Length < 16500);
                store.PersistFailureDiagnostic(intent.OperationId, new Exception("later failure"));
                Assert.AreEqual(first, File.ReadAllText(file));
                Assert.AreEqual(prepared.Latest.Sequence, store.Load(intent.OperationId).Latest.Sequence);
                Assert.ThrowsExactly<ArgumentException>(() =>
                    store.PersistFailureDiagnostic("../escape", new Exception("invalid")));
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void PersistedChain_ReopensAndCompletesAcrossRuntimeSessions()
        {
            var root = TemporaryDirectory();
            try
            {
                var secret = Secret();
                var store = new ColdRestoreIntentStore(
                    root,
                    "companion-0001",
                    secret);
                var intent = CreateIntent();
                var snapshot = store.Prepare(
                    intent,
                    "claim-0001",
                    intent.CreatedAtUtc,
                    intent.CreatedAtUtc.AddMinutes(5));
                Assert.AreEqual(
                    ColdRestoreOperationState.Prepared,
                    snapshot.Latest.State);

                snapshot = SourceQuiesced(store, snapshot);
                snapshot = SourceExited(store, snapshot);
                snapshot = Launch(store, snapshot);
                snapshot = NewSessionAttached(store, snapshot);
                snapshot = store.Claim(
                    intent.OperationId,
                    snapshot.Latest.Sequence,
                    snapshot.Claim.ClaimId,
                    snapshot.Claim.MacSha256,
                    intent.BuildFingerprint,
                    intent.CreatedAtUtc.AddSeconds(5),
                    "runtime-cold-0001",
                    "session-cold-0001",
                    5252,
                    intent.CreatedAtUtc.AddSeconds(3));
                snapshot = RuntimeTransition(
                    store,
                    snapshot,
                    ColdRestoreOperationState.BaselineReady,
                    6,
                    "baseline-ready");
                snapshot = RuntimeTransition(
                    store,
                    snapshot,
                    ColdRestoreOperationState.ReplayingPrefix,
                    7,
                    "replaying-prefix");
                snapshot = RuntimeTransition(
                    store,
                    snapshot,
                    ColdRestoreOperationState.PausedAtTarget,
                    8,
                    "paused-at-target");
                snapshot = store.Transition(
                    intent.OperationId,
                    snapshot.Latest.Sequence,
                    ColdRestoreOperationState.Completed,
                    intent.CreatedAtUtc.AddSeconds(9),
                    ColdRestoreActorRole.Companion,
                    "companion-0001",
                    string.Empty,
                    5252,
                    intent.CreatedAtUtc.AddSeconds(3),
                    "completed");

                var reopened = new ColdRestoreIntentStore(
                    root,
                    "companion-0001",
                    secret).Load(intent.OperationId);
                Assert.AreEqual(10, reopened.Records.Count);
                Assert.AreEqual(
                    ColdRestoreOperationState.Completed,
                    reopened.Latest.State);
                Assert.IsTrue(
                    ColdRestoreOperationStateMachine.IsTerminal(
                        reopened.Latest.State));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void WrongOrDuplicateClaim_FailsWithoutAdvancingState()
        {
            var root = TemporaryDirectory();
            try
            {
                var store = new ColdRestoreIntentStore(
                    root,
                    "companion-0001",
                    Secret());
                var intent = CreateIntent();
                var snapshot = store.Prepare(
                    intent,
                    "claim-0001",
                    intent.CreatedAtUtc,
                    intent.CreatedAtUtc.AddMinutes(5));
                snapshot = SourceQuiesced(store, snapshot);
                snapshot = SourceExited(store, snapshot);
                snapshot = Launch(store, snapshot);
                snapshot = NewSessionAttached(store, snapshot);

                var sequence = snapshot.Latest.Sequence;
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => store.Claim(
                        intent.OperationId,
                        sequence,
                        snapshot.Claim.ClaimId,
                        new string('0', 64),
                        intent.BuildFingerprint,
                        intent.CreatedAtUtc.AddSeconds(5),
                        "runtime-cold-0001",
                        "session-cold-0001",
                        5252,
                        intent.CreatedAtUtc.AddSeconds(3)));
                Assert.AreEqual(
                    sequence,
                    store.Load(intent.OperationId).Latest.Sequence);

                store.Claim(
                    intent.OperationId,
                    sequence,
                    snapshot.Claim.ClaimId,
                    snapshot.Claim.MacSha256,
                    intent.BuildFingerprint,
                    intent.CreatedAtUtc.AddSeconds(5),
                    "runtime-cold-0001",
                    "session-cold-0001",
                    5252,
                    intent.CreatedAtUtc.AddSeconds(3));
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => store.Claim(
                        intent.OperationId,
                        sequence,
                        snapshot.Claim.ClaimId,
                        snapshot.Claim.MacSha256,
                        intent.BuildFingerprint,
                        intent.CreatedAtUtc.AddSeconds(5),
                        "runtime-cold-0001",
                        "session-cold-0001",
                        5252,
                        intent.CreatedAtUtc.AddSeconds(3)));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void TamperedIntent_IsRejectedByClaimAndEventBindings()
        {
            var root = TemporaryDirectory();
            try
            {
                var store = new ColdRestoreIntentStore(
                    root,
                    "companion-0001",
                    Secret());
                var intent = CreateIntent();
                store.Prepare(
                    intent,
                    "claim-0001",
                    intent.CreatedAtUtc,
                    intent.CreatedAtUtc.AddMinutes(5));
                var path = Path.Combine(
                    root,
                    "operations",
                    intent.OperationId,
                    "intent.json");
                var text = File.ReadAllText(path, Encoding.UTF8)
                    .Replace(
                        "\"targetMovieTick\":250",
                        "\"targetMovieTick\":251",
                        StringComparison.Ordinal);
                File.WriteAllText(path, text, new UTF8Encoding(false));

                Assert.ThrowsExactly<InvalidDataException>(
                    () => store.Load(intent.OperationId));
                CollectionAssert.Contains(
                    new ColdRestoreIntentStore(
                            root,
                            "companion-0001",
                            Secret())
                        .Recover()
                        .CorruptOperationIds
                        .ToArray(),
                    intent.OperationId);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void CrashBeforePublish_LeavesOnlyIncompleteTransaction()
        {
            var root = TemporaryDirectory();
            try
            {
                var intent = CreateIntent();
                var crashing = new ColdRestoreIntentStore(
                    root,
                    "companion-0001",
                    Secret(),
                    stage =>
                    {
                        if (stage == ColdRestoreStoreStage.GenesisWritten)
                        {
                            throw new ColdRestoreStoreSimulatedCrashException(
                                stage);
                        }
                    });
                Assert.ThrowsExactly<
                    ColdRestoreStoreSimulatedCrashException>(
                    () => crashing.Prepare(
                        intent,
                        "claim-0001",
                        intent.CreatedAtUtc,
                        intent.CreatedAtUtc.AddMinutes(5)));

                var recovered = new ColdRestoreIntentStore(
                    root,
                    "companion-0001",
                    Secret());
                Assert.IsEmpty(recovered.ListOperationIds());
                Assert.HasCount(1, recovered.Recover().IncompleteTransactionIds);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static ColdRestoreOperationSnapshot SourceQuiesced(
            ColdRestoreIntentStore store,
            ColdRestoreOperationSnapshot snapshot)
        {
            var intent = snapshot.Intent;
            return store.Transition(
                intent.OperationId,
                snapshot.Latest.Sequence,
                ColdRestoreOperationState.SourceQuiesced,
                intent.CreatedAtUtc.AddSeconds(1),
                ColdRestoreActorRole.Runtime,
                "runtime-source-0001",
                intent.SourceSessionId,
                intent.SourceProcessId,
                intent.SourceProcessStartedAtUtc,
                "source-quiesced");
        }

        private static ColdRestoreOperationSnapshot SourceExited(
            ColdRestoreIntentStore store,
            ColdRestoreOperationSnapshot snapshot)
        {
            var intent = snapshot.Intent;
            return store.Transition(
                intent.OperationId,
                snapshot.Latest.Sequence,
                ColdRestoreOperationState.SourceExited,
                intent.CreatedAtUtc.AddSeconds(2),
                ColdRestoreActorRole.Companion,
                "companion-0001",
                string.Empty,
                intent.SourceProcessId,
                intent.SourceProcessStartedAtUtc,
                "source-exited");
        }

        private static ColdRestoreOperationSnapshot Launch(
            ColdRestoreIntentStore store,
            ColdRestoreOperationSnapshot snapshot)
        {
            var intent = snapshot.Intent;
            return store.Transition(
                intent.OperationId,
                snapshot.Latest.Sequence,
                ColdRestoreOperationState.Launching,
                intent.CreatedAtUtc.AddSeconds(3),
                ColdRestoreActorRole.Companion,
                "companion-0001",
                string.Empty,
                5252,
                intent.CreatedAtUtc.AddSeconds(3),
                "launching");
        }

        private static ColdRestoreOperationSnapshot NewSessionAttached(
            ColdRestoreIntentStore store,
            ColdRestoreOperationSnapshot snapshot)
        {
            var intent = snapshot.Intent;
            return store.Transition(
                intent.OperationId,
                snapshot.Latest.Sequence,
                ColdRestoreOperationState.NewSessionAttached,
                intent.CreatedAtUtc.AddSeconds(4),
                ColdRestoreActorRole.Companion,
                "companion-0001",
                "session-cold-0001",
                5252,
                intent.CreatedAtUtc.AddSeconds(3),
                "new-session-attached");
        }

        private static ColdRestoreOperationSnapshot RuntimeTransition(
            ColdRestoreIntentStore store,
            ColdRestoreOperationSnapshot snapshot,
            ColdRestoreOperationState state,
            int seconds,
            string detail)
        {
            var intent = snapshot.Intent;
            return store.Transition(
                intent.OperationId,
                snapshot.Latest.Sequence,
                state,
                intent.CreatedAtUtc.AddSeconds(seconds),
                ColdRestoreActorRole.Runtime,
                "runtime-cold-0001",
                "session-cold-0001",
                5252,
                intent.CreatedAtUtc.AddSeconds(3),
                detail);
        }

        private static ColdRestoreIntent CreateIntent()
        {
            var created = new DateTimeOffset(
                2026,
                9,
                1,
                8,
                0,
                0,
                TimeSpan.Zero);
            return new ColdRestoreIntent(
                ColdRestoreIntent.CurrentSchemaVersion,
                "intent-0001",
                "operation-0001",
                ColdRestoreOperationKind.ApplyBranchAndSeek,
                "save-0001",
                "session-source-0001",
                4242,
                created.AddMinutes(-2),
                created,
                created.AddMinutes(15),
                900,
                3,
                Hash('1'),
                Hash('2'),
                Hash('3'),
                Hash('4'),
                Hash('5'),
                250,
                string.Empty,
                "parent-operation-0001",
                "AiSdk",
                new ColdRestoreBuildFingerprint(
                    Hash('a'),
                    Hash('b'),
                    Hash('c'),
                    Hash('d'),
                    Hash('e'),
                    Hash('f'),
                    Hash('1'),
                    Hash('2')));
        }

        private static byte[] Secret()
        {
            return Enumerable.Range(0, 32)
                .Select(value => (byte)value)
                .ToArray();
        }

        private static string Hash(char value)
        {
            return new string(value, 64);
        }

        private static string TemporaryDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "hktas-cold-restore-tests-"
                + Guid.NewGuid().ToString("N"));
        }
    }
}
