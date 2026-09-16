using System;

namespace HollowKnightTAS.Core.Automation
{
    public static class AutomationProtocol
    {
        public const int Version = 1;
        public const string Product = "HollowKnightTAS.Automation";
        public const string BootstrapFileName = "automation-v1.json";
        public const string Hello = "automationHello";
        public const string HelloAck = "automationHelloAck";
        public const string Command = "automationCommand";
        public const string Result = "automationResult";
        public const string Event = "automationEvent";

        public static bool IsMessageType(string? value)
        {
            return string.Equals(value, Hello, StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       HelloAck,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       Command,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       Result,
                       StringComparison.Ordinal)
                   || string.Equals(
                       value,
                       Event,
                       StringComparison.Ordinal);
        }
    }
}
