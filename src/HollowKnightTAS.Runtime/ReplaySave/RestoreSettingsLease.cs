using System;
using HollowKnightTAS.Core.Ledger;
using HollowKnightTAS.Runtime.Control;
using UnityEngine;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    public sealed class RestoreSettingsLease : IDisposable
    {
        private bool restored;

        public RestoreSettingsLease()
        {
            Before = TimeSettingsSnapshot.Capture();
        }

        public TimeSettingsSnapshot Before { get; }
        public TimeSettingsSnapshot? After { get; private set; }
        public bool Equivalent => After != null && Before.Equals(After);

        public bool Restore()
        {
            if (restored)
            {
                return Equivalent;
            }

            Time.timeScale = SingleBits.ToSingle(Before.TimeScaleBits);
            Time.fixedDeltaTime =
                SingleBits.ToSingle(Before.FixedDeltaTimeBits);
            Application.targetFrameRate = Before.TargetFrameRate;
            QualitySettings.vSyncCount = Before.VSyncCount;
            After = TimeSettingsSnapshot.Capture();
            restored = true;
            return Equivalent;
        }

        public void Dispose()
        {
            Restore();
        }
    }
}
