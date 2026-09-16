using System;
using HollowKnightTAS.Companion.Automation;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Movie;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Companion.Tests
{
    [TestClass]
    public sealed class InputBatchTransactionWorkspaceTests
    {
        private const string Client = "client-a";
        private const string Connection = "connection-a";
        private const string Lease = "lease-a";
        private const string Session = "session-a";
        private static readonly string Manifest = new string('0', 64);

        [TestMethod]
        public void ChunkedTransactionCommitsTenMillionTicksWithoutExpansion()
        {
            var workspace = new InputBatchTransactionWorkspace();
            var begun = workspace.Begin(
                Client,
                Connection,
                Lease,
                Session,
                Manifest,
                17,
                3);

            var first = workspace.Append(
                begun.TransactionId,
                Client,
                Connection,
                Lease,
                17,
                3,
                0,
                Movie(6000000, TasAction.Right));
            var second = workspace.Append(
                begun.TransactionId,
                Client,
                Connection,
                Lease,
                17,
                3,
                1,
                Movie(4000000, TasAction.Attack));
            var committed = workspace.Commit(
                begun.TransactionId,
                Client,
                Connection,
                Lease,
                17,
                3);

            Assert.IsTrue(first.Success, first.Detail);
            Assert.IsTrue(second.Success, second.Detail);
            Assert.IsTrue(committed.Success, committed.Detail);
            Assert.AreEqual(2, committed.ChunkCount);
            Assert.AreEqual(10000000, committed.ExpandedTicks);
            Assert.AreEqual(2, committed.ChunkSha256s.Count);
            Assert.IsNotNull(committed.CanonicalMovieUtf8);
            Assert.IsTrue(MovieProtocolV1.IsLowerSha256(committed.MovieId));
            Assert.IsTrue(
                committed.CanonicalMovieUtf8!.Length < 2048,
                "Frame runs must not be expanded per tick.");
        }

        [TestMethod]
        public void ChunkOrderAndBindingMismatchFailClosed()
        {
            var workspace = new InputBatchTransactionWorkspace();
            var begun = workspace.Begin(
                Client,
                Connection,
                Lease,
                Session,
                Manifest,
                9,
                1);
            var outOfOrder = workspace.Append(
                begun.TransactionId,
                Client,
                Connection,
                Lease,
                9,
                1,
                1,
                Movie(1, TasAction.Jump));
            var stale = workspace.Append(
                begun.TransactionId,
                Client,
                Connection,
                Lease,
                10,
                1,
                0,
                Movie(1, TasAction.Jump));

            Assert.AreEqual("ChunkOrder", outOfOrder.Code);
            Assert.AreEqual("TransactionBindingMismatch", stale.Code);
            Assert.AreEqual(
                "EmptyBatch",
                workspace.Commit(
                    begun.TransactionId,
                    Client,
                    Connection,
                    Lease,
                    9,
                    1).Code);
        }

        [TestMethod]
        public void DisconnectCancelsUncommittedTransaction()
        {
            var workspace = new InputBatchTransactionWorkspace();
            var begun = workspace.Begin(
                Client,
                Connection,
                Lease,
                Session,
                Manifest,
                2,
                4);
            workspace.ReleaseConnection(Connection);

            Assert.AreEqual(
                "TransactionNotFound",
                workspace.Cancel(
                    begun.TransactionId,
                    Client,
                    Connection,
                    Lease,
                    2,
                    4).Code);
        }

        private static byte[] Movie(long count, TasAction action)
        {
            var span = new MovieSourceSpan("batch.hktas", 1, 1, 1);
            return new MovieCanonicalWriter().WriteUtf8(
                new MovieDocument(
                    "batch.hktas",
                    new MovieHeader(
                        MovieProtocolV1.Version,
                        "1.5.78.11833",
                        "1.5.78.11833-77",
                        Manifest,
                        "none",
                        "none",
                        MovieProtocolV1.TickUnit),
                    new MovieCommand[]
                    {
                        new FrameRunCommand(
                            count,
                            action,
                            0,
                            0,
                            false,
                            span)
                    }));
        }
    }
}
