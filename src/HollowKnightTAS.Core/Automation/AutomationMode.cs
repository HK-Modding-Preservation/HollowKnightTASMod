using System;

namespace HollowKnightTAS.Core.Automation
{
    public enum AutomationMode
    {
        Disabled = 0,
        ReadOnly = 1,
        ApprovedControl = 2
    }

    public static class AutomationModeCodec
    {
        public static bool TryParse(
            string? value,
            out AutomationMode mode)
        {
            if (string.Equals(
                    value,
                    nameof(AutomationMode.Disabled),
                    StringComparison.Ordinal))
            {
                mode = AutomationMode.Disabled;
                return true;
            }

            if (string.Equals(
                    value,
                    nameof(AutomationMode.ReadOnly),
                    StringComparison.Ordinal))
            {
                mode = AutomationMode.ReadOnly;
                return true;
            }

            if (string.Equals(
                    value,
                    nameof(AutomationMode.ApprovedControl),
                    StringComparison.Ordinal))
            {
                mode = AutomationMode.ApprovedControl;
                return true;
            }

            mode = AutomationMode.ReadOnly;
            return false;
        }

        public static AutomationMode Normalize(string? value)
        {
            return TryParse(value, out var mode)
                ? mode
                : AutomationMode.ReadOnly;
        }
    }
}
