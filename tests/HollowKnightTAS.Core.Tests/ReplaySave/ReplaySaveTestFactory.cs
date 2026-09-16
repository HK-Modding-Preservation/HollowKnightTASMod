using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Input;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.Recording;
using HollowKnightTAS.Core.ReplaySave;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Tests.ReplaySave
{
    internal static class ReplaySaveTestFactory
    {
        public static readonly DateTimeOffset RequestedAt =
            new DateTimeOffset(2026, 7, 28, 5, 0, 0, TimeSpan.Zero);

        public static ReplaySaveCommit Commit(
            string id = "save-0001",
            ReplaySaveReason reason = ReplaySaveReason.Manual,
            int retention = 0,
            long requestedTick = 0,
            int recordCount = 8,
            int recordsPerSegment = 3)
        {
            var snapshot = Snapshot("GG_Workshop");
            var snapshotBytes = SemanticSnapshotCanonicalizer.Serialize(snapshot);
            var semanticHash = SemanticSnapshotHasher.ComputeSha256(snapshot);
            var baseline = new BaselineBundle(
                1,
                "slot-2-baseline",
                semanticHash,
                2,
                RequestedAt.AddMinutes(-1),
                new byte[] { 1, 3, 3, 7, 9 },
                Encoding.UTF8.GetBytes("{\"mods\":true}"),
                snapshotBytes);
            var baselineBytes = BaselineBundleCodec.Serialize(baseline);
            var manifestBytes = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"test\":\"replay-save\"}");
            var manifestHash = Sha256Utility.ComputeHex(manifestBytes);
            var records = Records(recordCount);
            var segments = Segments(records, recordsPerSegment);
            var movie = ReplayMovieBuilder.Build(
                records,
                id + ".hktas",
                "1.5.78.11833",
                "1.5.78.11833-77",
                manifestHash,
                baseline.BaselineId,
                baseline.BaselineSemanticSha256);
            var movieBytes = new MovieCanonicalWriter().WriteUtf8(movie);
            return ReplaySaveCommit.Create(
                id,
                reason == ReplaySaveReason.AutomaticInterval
                    ? "Auto " + id
                    : "Manual " + id,
                reason,
                RequestedAt.AddSeconds(requestedTick),
                RequestedAt.AddSeconds(requestedTick + 1),
                requestedTick,
                recordCount - 1,
                manifestBytes,
                baselineBytes,
                movieBytes,
                segments,
                snapshotBytes,
                Encoding.UTF8.GetBytes(
                    "{\"firstMovieTick\":0,\"lastMovieTick\":"
                    + (recordCount - 1)
                    + "}"),
                "GG_Workshop",
                0,
                reason == ReplaySaveReason.AutomaticInterval ? retention : 0);
        }

        public static SemanticSnapshot Snapshot(string scene)
        {
            var builder = new SemanticSnapshotBuilder();
            builder.AddString("game.state", "PLAYING");
            builder.AddString("hero.actorState", "IDLE");
            builder.AddBoolean("hero.cState.attacking", false);
            builder.AddBoolean("hero.cState.dashing", false);
            builder.AddBoolean("hero.cState.falling", false);
            builder.AddBoolean("hero.cState.jumping", false);
            builder.AddBoolean("hero.cState.onGround", true);
            builder.AddBoolean("hero.cState.wallSliding", false);
            builder.AddFloat32Bits("hero.position.x", 0x41480000);
            builder.AddFloat32Bits(
                "hero.position.y",
                unchecked((int)0xc0500000));
            builder.AddFloat32Bits("hero.velocity.x", 0);
            builder.AddFloat32Bits(
                "hero.velocity.y",
                unchecked((int)0x80000000));
            builder.AddInt32("player.health", 5);
            builder.AddInt32("player.maxHealth", 9);
            builder.AddInt32("player.mp", 33);
            builder.AddString("scene.name", scene);
            return builder.Build();
        }

        public static List<JournalRecord> Records(int count)
        {
            var result = new List<JournalRecord>(count);
            var previous = TasAction.None;
            for (var index = 0; index < count; index++)
            {
                var held = index < 2
                    ? TasAction.None
                    : index < 5
                        ? TasAction.Right
                        : TasAction.Attack;
                var inputTick = checked((ulong)(100 + index));
                var sample = InputSample.FromHeld(
                    inputTick,
                    held,
                    previous);
                result.Add(
                    JournalRecord.Create(
                        index,
                        sample,
                        new TickStamp(
                            inputTick,
                            200 + index,
                            50 + index / 2,
                            index < 6 ? 0 : 1,
                            TickPhase.InControlCommitted)));
                previous = held;
            }

            return result;
        }

        public static List<byte[]> Segments(
            IReadOnlyList<JournalRecord> records,
            int recordsPerSegment)
        {
            var result = new List<byte[]>();
            var previous = ReplayJournalSegment.GenesisPreviousSha256;
            var sequence = 0;
            for (var offset = 0; offset < records.Count; offset += recordsPerSegment)
            {
                var segment = new ReplayJournalSegment(
                    1,
                    sequence++,
                    previous,
                    records
                        .Skip(offset)
                        .Take(recordsPerSegment)
                        .ToArray());
                var bytes = segment.Serialize();
                result.Add(bytes);
                previous = Sha256Utility.ComputeHex(bytes);
            }

            return result;
        }

        public static string TemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "HollowKnightTAS-T09-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
