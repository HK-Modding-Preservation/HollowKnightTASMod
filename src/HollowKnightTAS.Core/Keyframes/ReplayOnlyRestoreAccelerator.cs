using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Core.Keyframes
{
    public sealed class ReplayOnlyRestoreAccelerator :
        IReplayRestoreAccelerator
    {
        public ReplayOnlyRestoreAccelerator(
            IEnumerable<string> reasonCodes)
        {
            ReasonCodes = new ReadOnlyCollection<string>(
                (reasonCodes
                 ?? throw new ArgumentNullException(
                     nameof(reasonCodes)))
                .Select(
                    value => KeyframeAdapterEnvelope.RequireId(
                        value,
                        nameof(reasonCodes)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
            if (ReasonCodes.Count == 0)
            {
                throw new ArgumentException(
                    "ReplayOnly requires at least one reason.",
                    nameof(reasonCodes));
            }
        }

        public IReadOnlyList<string> ReasonCodes { get; }

        public ReplayRestoreAccelerationPlan TryPlan(
            ReplaySaveDescriptor save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }
            return new ReplayRestoreAccelerationPlan(
                ReplayRestoreAccelerationStatus.Unavailable,
                0,
                "ReplayOnly: " + string.Join(",", ReasonCodes));
        }

        public ReplayRestoreAccelerationResult TryRestore(
            ReplayRestoreAccelerationPlan plan,
            CancellationToken cancellationToken)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            return new ReplayRestoreAccelerationResult(
                false,
                0,
                "ReplayOnly never applies a semantic keyframe.");
        }
    }
}
