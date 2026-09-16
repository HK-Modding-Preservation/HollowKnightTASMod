using System;
using System.Collections.Generic;
using System.Linq;
using HollowKnightTAS.Core.Automation;

namespace HollowKnightTAS.Companion.Automation
{
    public sealed class LeaseOperationResult
    {
        public LeaseOperationResult(
            bool success,
            string code,
            string detail,
            ControlLeaseDescriptor? lease)
        {
            Success = success;
            Code = code;
            Detail = detail;
            Lease = lease;
        }

        public bool Success { get; }
        public string Code { get; }
        public string Detail { get; }
        public ControlLeaseDescriptor? Lease { get; }
    }

    public sealed class ControlLeaseManager
    {
        public static readonly TimeSpan DefaultTtl =
            TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaximumTtl =
            TimeSpan.FromMinutes(5);

        private readonly object sync = new object();
        private readonly Func<DateTimeOffset> utcNow;
        private ControlLeaseDescriptor? active;
        private string activeConnectionId = string.Empty;

        public ControlLeaseManager(
            Func<DateTimeOffset>? utcNow = null)
        {
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public ControlLeaseDescriptor? Active
        {
            get
            {
                lock (sync)
                {
                    ExpireUnsafe();
                    return active;
                }
            }
        }

        public LeaseOperationResult Acquire(
            string clientId,
            string connectionId,
            IEnumerable<string> requestedScopes,
            TimeSpan requestedTtl,
            string sessionId,
            string manifestSha256,
            AutomationMode mode)
        {
            lock (sync)
            {
                ExpireUnsafe();
                if (mode != AutomationMode.ApprovedControl)
                {
                    return Fail(
                        "ControlNotApproved",
                        "ApprovedControl mode is required.");
                }

                var scopes = NormalizeScopes(requestedScopes);
                if (scopes.Length == 0)
                {
                    return Fail(
                        "InvalidScope",
                        "At least one registered write scope is required.");
                }

                if (active != null)
                {
                    return Fail(
                        "LeaseBusy",
                        "Another client owns the write lease.");
                }

                var ttl = NormalizeTtl(requestedTtl);
                var now = utcNow();
                active = new ControlLeaseDescriptor(
                    Guid.NewGuid().ToString("N"),
                    clientId,
                    scopes,
                    now,
                    now + ttl,
                    sessionId,
                    manifestSha256);
                activeConnectionId = connectionId;
                return Success(active);
            }
        }

        public LeaseOperationResult Renew(
            string clientId,
            string connectionId,
            string leaseId,
            TimeSpan requestedTtl)
        {
            lock (sync)
            {
                ExpireUnsafe();
                if (active == null
                    || active.ClientId != clientId
                    || active.LeaseId != leaseId
                    || activeConnectionId != connectionId)
                {
                    return Fail(
                        "LeaseNotOwned",
                        "The active lease is not owned by this connection.");
                }

                var now = utcNow();
                active = new ControlLeaseDescriptor(
                    active.LeaseId,
                    active.ClientId,
                    active.Scopes,
                    active.IssuedAtUtc,
                    now + NormalizeTtl(requestedTtl),
                    active.SessionId,
                    active.ManifestSha256);
                return Success(active);
            }
        }

        public LeaseOperationResult Release(
            string clientId,
            string connectionId,
            string leaseId)
        {
            lock (sync)
            {
                ExpireUnsafe();
                if (active == null)
                {
                    return Fail(
                        "LeaseNotFound",
                        "No write lease is active.");
                }

                if (active.ClientId != clientId
                    || active.LeaseId != leaseId
                    || activeConnectionId != connectionId)
                {
                    return Fail(
                        "LeaseNotOwned",
                        "The active lease is not owned by this connection.");
                }

                var released = active;
                active = null;
                activeConnectionId = string.Empty;
                return new LeaseOperationResult(
                    true,
                    "Released",
                    "Control lease released.",
                    released);
            }
        }

        public bool Validate(
            string clientId,
            string connectionId,
            string leaseId,
            string scope,
            string sessionId,
            string manifestSha256)
        {
            lock (sync)
            {
                ExpireUnsafe();
                return active != null
                       && activeConnectionId == connectionId
                       && active.Covers(
                           clientId,
                           leaseId,
                           scope,
                           sessionId,
                           manifestSha256,
                           utcNow());
            }
        }

        public void ReleaseConnection(string connectionId)
        {
            lock (sync)
            {
                if (active != null
                    && activeConnectionId == connectionId)
                {
                    active = null;
                    activeConnectionId = string.Empty;
                }
            }
        }

        public void Revoke()
        {
            lock (sync)
            {
                active = null;
                activeConnectionId = string.Empty;
            }
        }

        private static TimeSpan NormalizeTtl(TimeSpan requested)
        {
            if (requested <= TimeSpan.Zero)
            {
                return DefaultTtl;
            }

            return requested > MaximumTtl
                ? MaximumTtl
                : requested;
        }

        private static string[] NormalizeScopes(
            IEnumerable<string> requested)
        {
            return (requested ?? Array.Empty<string>())
                .Where(
                    scope =>
                        AutomationScope.IsKnown(scope)
                        && !AutomationScope.IsReadOnly(scope))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private void ExpireUnsafe()
        {
            if (active != null
                && utcNow() >= active.ExpiresAtUtc)
            {
                active = null;
                activeConnectionId = string.Empty;
            }
        }

        private static LeaseOperationResult Success(
            ControlLeaseDescriptor lease)
        {
            return new LeaseOperationResult(
                true,
                "Accepted",
                "Control lease accepted.",
                lease);
        }

        private static LeaseOperationResult Fail(
            string code,
            string detail)
        {
            return new LeaseOperationResult(
                false,
                code,
                detail,
                null);
        }
    }
}
