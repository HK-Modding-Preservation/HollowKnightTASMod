using System;

namespace HollowKnightTAS.Core.Automation
{
    public sealed class AutomationCapability
    {
        public AutomationCapability(
            string commandId,
            string scope,
            bool readOnly,
            bool requiresLease,
            string availability,
            string preconditions,
            string sideEffects)
        {
            CommandId = commandId;
            Scope = scope;
            ReadOnly = readOnly;
            RequiresLease = requiresLease;
            Availability = availability;
            Preconditions = preconditions;
            SideEffects = sideEffects;
        }

        public string CommandId { get; }
        public string Scope { get; }
        public bool ReadOnly { get; }
        public bool RequiresLease { get; }
        public string Availability { get; }
        public string Preconditions { get; }
        public string SideEffects { get; }
    }
}
