using System;
using System.IO;

namespace HollowKnightTAS.Automation.Client
{
    public sealed class AutomationConnectOptions
    {
        public string ClientId { get; set; } =
            "sdk-" + Guid.NewGuid().ToString("N");

        public string BootstrapPath { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "HollowKnightTAS",
                "automation",
                "automation-v1.json");

        public TimeSpan Timeout { get; set; } =
            TimeSpan.FromSeconds(10);
    }

    public sealed class AutomationHandshake
    {
        public AutomationHandshake(
            string clientId,
            string sessionId,
            string manifestSha256,
            string mode)
        {
            ClientId = clientId;
            SessionId = sessionId;
            ManifestSha256 = manifestSha256;
            Mode = mode;
        }

        public string ClientId { get; }
        public string SessionId { get; }
        public string ManifestSha256 { get; }
        public string Mode { get; }
    }
}
