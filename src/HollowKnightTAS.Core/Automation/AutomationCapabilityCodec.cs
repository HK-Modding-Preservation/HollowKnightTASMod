using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Automation
{
    public static class AutomationCapabilityCodec
    {
        public static string SerializeArray(
            IEnumerable<AutomationCapability> capabilities)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            var builder = new StringBuilder(4096);
            builder.Append('[');
            var first = true;
            foreach (var capability in capabilities.OrderBy(
                         item => item.CommandId,
                         StringComparer.Ordinal))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                Append(builder, capability);
                first = false;
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static void Append(
            StringBuilder builder,
            AutomationCapability capability)
        {
            if (capability == null)
            {
                throw new ArgumentException(
                    "Capability entries cannot be null.",
                    nameof(capability));
            }

            builder.Append("{\"availability\":");
            CanonicalJsonWriter.AppendString(
                builder,
                capability.Availability);
            builder.Append(",\"commandId\":");
            CanonicalJsonWriter.AppendString(
                builder,
                capability.CommandId);
            builder.Append(",\"preconditions\":");
            CanonicalJsonWriter.AppendString(
                builder,
                capability.Preconditions);
            builder.Append(",\"readOnly\":");
            builder.Append(
                capability.ReadOnly ? "true" : "false");
            builder.Append(",\"requiresLease\":");
            builder.Append(
                capability.RequiresLease ? "true" : "false");
            builder.Append(",\"scope\":");
            CanonicalJsonWriter.AppendString(
                builder,
                capability.Scope);
            builder.Append(",\"sideEffects\":");
            CanonicalJsonWriter.AppendString(
                builder,
                capability.SideEffects);
            builder.Append('}');
        }
    }
}
