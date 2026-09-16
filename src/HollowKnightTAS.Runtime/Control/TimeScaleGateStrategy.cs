using System;

namespace HollowKnightTAS.Runtime.Control
{
    public sealed class TimeScaleGateStrategy : IDisposable
    {
        private readonly TimeSettingsLease lease;

        public TimeScaleGateStrategy(TimeSettingsLease lease)
        {
            this.lease = lease ?? throw new ArgumentNullException(nameof(lease));
        }

        public const string StrategyId = "time-scale-movie-gate-v1";

        public TimeSettingsSnapshot Before => lease.Before;
        public TimeSettingsRestoreReport? RestoreReport =>
            lease.RestoreReport;

        public void Close()
        {
            lease.SetPaused();
        }

        public void OpenStepWindow()
        {
            lease.SetStepWindowOpen();
        }

        public TimeSettingsRestoreReport Restore(string reason)
        {
            return lease.Restore(reason);
        }

        public void Dispose()
        {
            lease.Dispose();
        }
    }
}
