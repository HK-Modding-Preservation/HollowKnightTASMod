using System;
using System.Collections.Generic;

namespace HollowKnightTAS.Core.Ledger
{
    public enum LedgerValidationError
    {
        None = 0,
        EmptyLedger,
        NullRecord,
        FirstSequenceNotOne,
        DuplicateSequence,
        NonMonotonicSequence,
        MissingSequence,
        MetadataChanged,
        NonMonotonicInputTick,
        NonMonotonicVisualTick,
        NonMonotonicFixedTick,
        NonMonotonicSceneEpoch,
        SceneEpochSkipped,
        SceneChangedWithoutEvent,
        InvalidActiveSceneChange,
        FixedStepCountMismatch,
        LedgerGap
    }

    public sealed class LedgerValidationReport
    {
        public LedgerValidationReport(
            bool isValid,
            LedgerValidationError error,
            int recordIndex,
            long sequence,
            string message,
            int recordCount,
            int visualBoundaryCount,
            int fixedEventCount,
            int sceneTransitionCount,
            int minimumFixedStepsPerVisual,
            int maximumFixedStepsPerVisual)
        {
            IsValid = isValid;
            Error = error;
            RecordIndex = recordIndex;
            Sequence = sequence;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            RecordCount = recordCount;
            VisualBoundaryCount = visualBoundaryCount;
            FixedEventCount = fixedEventCount;
            SceneTransitionCount = sceneTransitionCount;
            MinimumFixedStepsPerVisual = minimumFixedStepsPerVisual;
            MaximumFixedStepsPerVisual = maximumFixedStepsPerVisual;
        }

        public bool IsValid { get; }
        public LedgerValidationError Error { get; }
        public int RecordIndex { get; }
        public long Sequence { get; }
        public string Message { get; }
        public int RecordCount { get; }
        public int VisualBoundaryCount { get; }
        public int FixedEventCount { get; }
        public int SceneTransitionCount { get; }
        public int MinimumFixedStepsPerVisual { get; }
        public int MaximumFixedStepsPerVisual { get; }
    }

    public static class TickLedgerValidator
    {
        public static LedgerValidationReport Validate(
            IEnumerable<TickLedgerRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            TickLedgerRecord? previous = null;
            string? sessionId = null;
            string? manifestSha256 = null;
            string? runId = null;
            string? profile = null;
            string? sceneName = null;
            var sceneEpoch = 0;
            var fixedSincePreviousVisual = 0;
            var recordIndex = 0;
            var visualBoundaries = 0;
            var fixedEvents = 0;
            var sceneTransitions = 0;
            var minimumFixed = int.MaxValue;
            var maximumFixed = int.MinValue;
            long activeSequence = 0;

            foreach (var current in records)
            {
                recordIndex++;
                if (current == null)
                {
                    return Fail(
                        LedgerValidationError.NullRecord,
                        recordIndex,
                        0,
                        "Ledger contains a null record.",
                        recordIndex,
                        visualBoundaries,
                        fixedEvents,
                        sceneTransitions,
                        minimumFixed,
                        maximumFixed);
                }

                activeSequence = current.Sequence;
                if (previous == null)
                {
                    if (current.Sequence != 1)
                    {
                        return Fail(
                            LedgerValidationError.FirstSequenceNotOne,
                            recordIndex,
                            current.Sequence,
                            "The first ledger sequence must be 1.",
                            recordIndex,
                            visualBoundaries,
                            fixedEvents,
                            sceneTransitions,
                            minimumFixed,
                            maximumFixed);
                    }

                    sessionId = current.SessionId;
                    manifestSha256 = current.ManifestSha256;
                    runId = current.RunId;
                    profile = current.Profile;
                    sceneName = current.SceneName;
                    sceneEpoch = current.Stamp.SceneEpoch;
                }
                else
                {
                    if (current.Sequence == previous.Sequence)
                    {
                        return FailAt(
                            LedgerValidationError.DuplicateSequence,
                            "Ledger sequence is duplicated.");
                    }

                    if (current.Sequence < previous.Sequence)
                    {
                        return FailAt(
                            LedgerValidationError.NonMonotonicSequence,
                            "Ledger sequence moved backwards.");
                    }

                    if (current.Sequence != previous.Sequence + 1)
                    {
                        return FailAt(
                            LedgerValidationError.MissingSequence,
                            "Ledger sequence has a missing line before this record.");
                    }

                    if (!string.Equals(sessionId, current.SessionId, StringComparison.Ordinal)
                        || !string.Equals(
                            manifestSha256,
                            current.ManifestSha256,
                            StringComparison.Ordinal)
                        || !string.Equals(runId, current.RunId, StringComparison.Ordinal)
                        || !string.Equals(profile, current.Profile, StringComparison.Ordinal))
                    {
                        return FailAt(
                            LedgerValidationError.MetadataChanged,
                            "Ledger session, manifest, run, or profile metadata changed.");
                    }

                    if (current.Stamp.InputTick < previous.Stamp.InputTick)
                    {
                        return FailAt(
                            LedgerValidationError.NonMonotonicInputTick,
                            "inputTick moved backwards.");
                    }

                    if (current.Stamp.VisualTick < previous.Stamp.VisualTick)
                    {
                        return FailAt(
                            LedgerValidationError.NonMonotonicVisualTick,
                            "visualTick moved backwards.");
                    }

                    if (current.Stamp.FixedTick < previous.Stamp.FixedTick)
                    {
                        return FailAt(
                            LedgerValidationError.NonMonotonicFixedTick,
                            "fixedTick moved backwards.");
                    }

                    if (current.Stamp.SceneEpoch < sceneEpoch)
                    {
                        return FailAt(
                            LedgerValidationError.NonMonotonicSceneEpoch,
                            "sceneEpoch moved backwards.");
                    }

                    if (current.Stamp.SceneEpoch > sceneEpoch)
                    {
                        if (current.Stamp.SceneEpoch != sceneEpoch + 1)
                        {
                            return FailAt(
                                LedgerValidationError.SceneEpochSkipped,
                                "sceneEpoch skipped one or more values.");
                        }

                        if (current.Stamp.Phase != TickPhase.ActiveSceneChanged
                            || string.Equals(
                                sceneName,
                                current.SceneName,
                                StringComparison.Ordinal))
                        {
                            return FailAt(
                                LedgerValidationError.InvalidActiveSceneChange,
                                "A scene epoch change requires ActiveSceneChanged and a new scene.");
                        }

                        sceneEpoch = current.Stamp.SceneEpoch;
                        sceneName = current.SceneName;
                        sceneTransitions++;
                    }
                    else if (!string.Equals(
                                 sceneName,
                                 current.SceneName,
                                 StringComparison.Ordinal))
                    {
                        return FailAt(
                            LedgerValidationError.SceneChangedWithoutEvent,
                            "Scene name changed without an ActiveSceneChanged epoch.");
                    }
                    else if (current.Stamp.Phase == TickPhase.ActiveSceneChanged)
                    {
                        return FailAt(
                            LedgerValidationError.InvalidActiveSceneChange,
                            "ActiveSceneChanged did not advance the epoch and scene.");
                    }
                }

                if (current.Stamp.Phase == TickPhase.FixedUpdateBegin)
                {
                    fixedSincePreviousVisual++;
                    fixedEvents++;
                }
                else if (current.Stamp.Phase == TickPhase.VisualUpdateBegin)
                {
                    if (current.FixedStepsSincePreviousVisual
                        != fixedSincePreviousVisual)
                    {
                        return FailAt(
                            LedgerValidationError.FixedStepCountMismatch,
                            "fixedStepsSincePreviousVisual does not match fixed events.");
                    }

                    minimumFixed = Math.Min(
                        minimumFixed,
                        current.FixedStepsSincePreviousVisual);
                    maximumFixed = Math.Max(
                        maximumFixed,
                        current.FixedStepsSincePreviousVisual);
                    fixedSincePreviousVisual = 0;
                    visualBoundaries++;
                }

                if (current.Stamp.Phase == TickPhase.LedgerGap)
                {
                    return FailAt(
                        LedgerValidationError.LedgerGap,
                        "The ledger contains an explicit dropped-record gap.");
                }

                previous = current;
            }

            if (recordIndex == 0)
            {
                return Fail(
                    LedgerValidationError.EmptyLedger,
                    0,
                    0,
                    "Ledger is empty.",
                    0,
                    0,
                    0,
                    0,
                    int.MaxValue,
                    int.MinValue);
            }

            return new LedgerValidationReport(
                true,
                LedgerValidationError.None,
                0,
                0,
                "Valid",
                recordIndex,
                visualBoundaries,
                fixedEvents,
                sceneTransitions,
                minimumFixed == int.MaxValue ? 0 : minimumFixed,
                maximumFixed == int.MinValue ? 0 : maximumFixed);

            LedgerValidationReport FailAt(
                LedgerValidationError error,
                string message)
            {
                return Fail(
                    error,
                    recordIndex,
                    activeSequence,
                    message,
                    recordIndex,
                    visualBoundaries,
                    fixedEvents,
                    sceneTransitions,
                    minimumFixed,
                    maximumFixed);
            }
        }

        private static LedgerValidationReport Fail(
            LedgerValidationError error,
            int recordIndex,
            long currentSequence,
            string message,
            int recordCount,
            int visualBoundaryCount,
            int fixedEventCount,
            int sceneTransitionCount,
            int minimumFixedSteps,
            int maximumFixedSteps)
        {
            return new LedgerValidationReport(
                false,
                error,
                recordIndex,
                currentSequence,
                message,
                recordCount,
                visualBoundaryCount,
                fixedEventCount,
                sceneTransitionCount,
                minimumFixedSteps == int.MaxValue ? 0 : minimumFixedSteps,
                maximumFixedSteps == int.MinValue ? 0 : maximumFixedSteps);
        }
    }
}
