using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using HollowKnightTAS.Core.Ledger;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HollowKnightTAS.Core.Tests.Ledger
{
    [TestClass]
    public sealed class TickLedgerValidatorTests
    {
        [TestMethod]
        public void ValidZeroFixedFixture_PassesAndAllowsZero()
        {
            var report = TickLedgerValidator.Validate(
                ReadFixture("valid-zero-fixed.jsonl"));

            Assert.IsTrue(report.IsValid, report.Message);
            Assert.AreEqual(0, report.MinimumFixedStepsPerVisual);
            Assert.AreEqual(0, report.MaximumFixedStepsPerVisual);
        }

        [TestMethod]
        public void ValidMultiFixedFixture_PassesAndAllowsMultiple()
        {
            var report = TickLedgerValidator.Validate(
                ReadFixture("valid-multi-fixed.jsonl"));

            Assert.IsTrue(report.IsValid, report.Message);
            Assert.AreEqual(0, report.MinimumFixedStepsPerVisual);
            Assert.AreEqual(2, report.MaximumFixedStepsPerVisual);
        }

        [TestMethod]
        public void InvalidNonMonotonicFixture_ReportsFirstRollback()
        {
            var report = TickLedgerValidator.Validate(
                ReadFixture("invalid-nonmonotonic.jsonl"));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(
                LedgerValidationError.NonMonotonicInputTick,
                report.Error);
            Assert.AreEqual(4, report.RecordIndex);
            Assert.AreEqual(4L, report.Sequence);
        }

        [TestMethod]
        public void MissingSequence_ReportsFirstLineAfterGap()
        {
            var records = ReadFixture("valid-zero-fixed.jsonl").ToList();
            records.RemoveAt(2);

            var report = TickLedgerValidator.Validate(records);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(LedgerValidationError.MissingSequence, report.Error);
            Assert.AreEqual(3, report.RecordIndex);
            Assert.AreEqual(4L, report.Sequence);
        }

        [TestMethod]
        public void DuplicateSequence_ReportsDuplicateLine()
        {
            var records = ReadFixture("valid-zero-fixed.jsonl").ToList();
            records[2] = Clone(records[2], 2);

            var report = TickLedgerValidator.Validate(records);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(LedgerValidationError.DuplicateSequence, report.Error);
            Assert.AreEqual(3, report.RecordIndex);
            Assert.AreEqual(2L, report.Sequence);
        }

        [TestMethod]
        public void SceneNameChangeWithoutActiveSceneEvent_FailsPrecisely()
        {
            var records = ReadFixture("valid-zero-fixed.jsonl").Take(2).ToList();
            var previous = records[1];
            records.Add(
                Clone(
                    previous,
                    3,
                    new TickStamp(
                        previous.Stamp.InputTick,
                        previous.Stamp.VisualTick,
                        previous.Stamp.FixedTick,
                        previous.Stamp.SceneEpoch,
                        TickPhase.InControlCommitted),
                    "OtherScene"));

            var report = TickLedgerValidator.Validate(records);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(
                LedgerValidationError.SceneChangedWithoutEvent,
                report.Error);
            Assert.AreEqual(3, report.RecordIndex);
        }

        [TestMethod]
        public void Serializer_EmitsRoundTripTextAndExactBits()
        {
            var record = CreateRecord(
                1,
                TickPhase.ProfileApplied,
                0,
                time: 0.1f,
                fixedTime: 0.08f);

            using var document = JsonDocument.Parse(
                TickLedgerRecordJson.Serialize(record));
            var root = document.RootElement;

            Assert.AreEqual(
                record.TimeBits,
                root.GetProperty("timeBits").GetInt32());
            Assert.AreEqual(
                record.FixedTimeBits,
                root.GetProperty("fixedTimeBits").GetInt32());
            Assert.AreEqual(
                0.1f,
                float.Parse(
                    root.GetProperty("time").GetString()!,
                    CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void JsonlSink_WritesStrictlyOrderedParseableLines()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "hktas-ledger-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                using (var sink = new TickLedgerJsonlSink(
                           path,
                           32,
                           TimeSpan.FromSeconds(2)))
                {
                    Assert.IsTrue(
                        sink.TryEmit(
                            CreateRecord(1, TickPhase.ProfileApplied, 0)));
                    Assert.IsTrue(
                        sink.TryEmit(
                            CreateRecord(2, TickPhase.VisualUpdateBegin, 0)));
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.AreEqual(0L, sink.DroppedCount);
                }

                var lines = File.ReadAllLines(path);
                Assert.AreEqual(2, lines.Length);
                using var first = JsonDocument.Parse(lines[0]);
                using var second = JsonDocument.Parse(lines[1]);
                Assert.AreEqual(1L, first.RootElement.GetProperty("sequence").GetInt64());
                Assert.AreEqual(2L, second.RootElement.GetProperty("sequence").GetInt64());
                Assert.IsFalse(File.Exists(path + ".incomplete"));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void JsonlSink_QuotaPreservesCompleteLinesAndMarksLoss()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-ledger-quota-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "ledger.jsonl");
            var first = CreateRecord(1, TickPhase.ProfileApplied, 0);
            var firstLine = TickLedgerRecordJson.Serialize(first) + "\n";
            var size = System.Text.Encoding.UTF8.GetByteCount(firstLine);
            try
            {
                using (var sink = new TickLedgerJsonlSink(path, 8, TimeSpan.FromSeconds(2), size))
                {
                    Assert.IsTrue(sink.TryEmit(first));
                    Assert.IsTrue(sink.TryEmit(CreateRecord(2, TickPhase.VisualUpdateBegin, 0)));
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.IsTrue(sink.SizeLimitReached);
                    Assert.AreEqual(1L, sink.DroppedCount);
                    Assert.IsFalse(sink.TryEmit(CreateRecord(3, TickPhase.VisualUpdateBegin, 0)));
                    Assert.AreEqual(2L, sink.DroppedCount);
                    sink.Flush(TimeSpan.FromSeconds(2));
                }
                Assert.AreEqual(firstLine, File.ReadAllText(path));
                Assert.IsTrue(File.Exists(path + ".incomplete"));
                Assert.ThrowsExactly<IOException>(() => new TickLedgerJsonlSink(path, 8, TimeSpan.FromSeconds(2)));
            }
            finally { Directory.Delete(root, true); }
        }

        [TestMethod]
        public void JsonlSink_ExhaustedSharedBudgetDoesNotDeleteOldEvidence()
        {
            var root = Path.Combine(Path.GetTempPath(), "hktas-ledger-budget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "ledger.jsonl");
            try
            {
                File.WriteAllText(Path.Combine(root, "old.jsonl"), "1234");
                using (var sink = new TickLedgerJsonlSink(path, 8, TimeSpan.FromSeconds(2), 1000, root, 4))
                {
                    sink.TryEmit(CreateRecord(1, TickPhase.ProfileApplied, 0));
                    sink.Flush(TimeSpan.FromSeconds(2));
                    Assert.AreEqual(1L, sink.DroppedCount);
                }
                Assert.AreEqual(0L, new FileInfo(path).Length);
                Assert.AreEqual("1234", File.ReadAllText(Path.Combine(root, "old.jsonl")));
                Assert.IsTrue(File.Exists(path + ".incomplete"));
            }
            finally { Directory.Delete(root, true); }
        }

        private static IReadOnlyList<TickLedgerRecord> ReadFixture(string name)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "ledger",
                name);
            var records = new List<TickLedgerRecord>();
            foreach (var line in File.ReadLines(path))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var record = new TickLedgerRecord(
                    root.GetProperty("sequence").GetInt64(),
                    root.GetProperty("sessionId").GetString()!,
                    root.GetProperty("manifestSha256").GetString()!,
                    root.GetProperty("runId").GetString()!,
                    root.GetProperty("profile").GetString()!,
                    new TickStamp(
                        root.GetProperty("inputTick").GetUInt64(),
                        root.GetProperty("visualTick").GetInt64(),
                        root.GetProperty("fixedTick").GetInt64(),
                        root.GetProperty("sceneEpoch").GetInt32(),
                        Enum.Parse<TickPhase>(
                            root.GetProperty("phase").GetString()!)),
                    root.GetProperty("fixedStepsSincePreviousVisual").GetInt32(),
                    ParseFloat(root, "time"),
                    ParseFloat(root, "fixedTime"),
                    ParseFloat(root, "timeMinusFixedTime"),
                    ParseFloat(root, "deltaTime"),
                    ParseFloat(root, "unscaledDeltaTime"),
                    ParseFloat(root, "timeScale"),
                    ParseFloat(root, "realtimeSinceStartup"),
                    root.GetProperty("sceneName").GetString()!,
                    root.GetProperty("detail").GetString()!);

                Assert.AreEqual(
                    root.GetProperty("timeBits").GetInt32(),
                    record.TimeBits);
                Assert.AreEqual(
                    root.GetProperty("fixedTimeBits").GetInt32(),
                    record.FixedTimeBits);
                Assert.AreEqual(
                    root.GetProperty("timeScaleBits").GetInt32(),
                    record.TimeScaleBits);
                records.Add(record);
            }

            return records;
        }

        private static float ParseFloat(JsonElement root, string name)
        {
            return float.Parse(
                root.GetProperty(name).GetString()!,
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static TickLedgerRecord Clone(
            TickLedgerRecord value,
            long sequence,
            TickStamp? stamp = null,
            string? sceneName = null)
        {
            return new TickLedgerRecord(
                sequence,
                value.SessionId,
                value.ManifestSha256,
                value.RunId,
                value.Profile,
                stamp ?? value.Stamp,
                (stamp ?? value.Stamp).Phase == TickPhase.VisualUpdateBegin
                    ? value.FixedStepsSincePreviousVisual
                    : -1,
                SingleBits.ToSingle(value.TimeBits),
                SingleBits.ToSingle(value.FixedTimeBits),
                SingleBits.ToSingle(value.TimeMinusFixedTimeBits),
                SingleBits.ToSingle(value.DeltaTimeBits),
                SingleBits.ToSingle(value.UnscaledDeltaTimeBits),
                SingleBits.ToSingle(value.TimeScaleBits),
                SingleBits.ToSingle(value.RealtimeSinceStartupBits),
                sceneName ?? value.SceneName,
                value.Detail);
        }

        private static TickLedgerRecord CreateRecord(
            long sequence,
            TickPhase phase,
            int fixedSteps,
            float time = 0f,
            float fixedTime = 0f)
        {
            return new TickLedgerRecord(
                sequence,
                "test-session",
                "test-manifest",
                "test-run",
                "P60",
                new TickStamp(0, phase == TickPhase.VisualUpdateBegin ? 1 : 0, 0, 0, phase),
                phase == TickPhase.VisualUpdateBegin ? fixedSteps : -1,
                time,
                fixedTime,
                time - fixedTime,
                0f,
                0f,
                1f,
                0f,
                "TestScene",
                string.Empty);
        }
    }
}
