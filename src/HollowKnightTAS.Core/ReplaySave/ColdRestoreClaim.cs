using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.ReplaySave
{
    public sealed class ColdRestoreClaim
    {
        public const int CurrentSchemaVersion = 1;

        public ColdRestoreClaim(
            int schemaVersion,
            string claimId,
            string intentSha256,
            string companionInstanceId,
            string sourceSessionId,
            DateTimeOffset issuedAtUtc,
            DateTimeOffset expiresAtUtc,
            string macSha256)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            ClaimId = ColdRestoreIntent.RequireIdentifier(
                claimId,
                nameof(claimId));
            IntentSha256 = ReplaySaveDescriptor.RequireSha256(
                intentSha256,
                nameof(intentSha256));
            CompanionInstanceId = ColdRestoreIntent.RequireIdentifier(
                companionInstanceId,
                nameof(companionInstanceId));
            SourceSessionId = ColdRestoreIntent.RequireIdentifier(
                sourceSessionId,
                nameof(sourceSessionId));
            IssuedAtUtc = issuedAtUtc.ToUniversalTime();
            ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
            if (ExpiresAtUtc <= IssuedAtUtc)
            {
                throw new ArgumentException(
                    "Claim expiry must follow issuance.",
                    nameof(expiresAtUtc));
            }

            MacSha256 = ReplaySaveDescriptor.RequireSha256(
                macSha256,
                nameof(macSha256));
        }

        public int SchemaVersion { get; }
        public string ClaimId { get; }
        public string IntentSha256 { get; }
        public string CompanionInstanceId { get; }
        public string SourceSessionId { get; }
        public DateTimeOffset IssuedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public string MacSha256 { get; }
    }

    public static class ColdRestoreClaimCodec
    {
        public const int MaximumBytes = 16 * 1024;

        public static byte[] Serialize(ColdRestoreClaim value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(1024);
            builder.Append('{');
            ReplaySaveJson.AppendString(
                builder,
                "claimId",
                value.ClaimId);
            ReplaySaveJson.AppendString(
                builder,
                "companionInstanceId",
                value.CompanionInstanceId);
            ReplaySaveJson.AppendString(
                builder,
                "expiresAtUtc",
                ReplaySaveJson.FormatUtc(value.ExpiresAtUtc));
            ReplaySaveJson.AppendString(
                builder,
                "intentSha256",
                value.IntentSha256);
            ReplaySaveJson.AppendString(
                builder,
                "issuedAtUtc",
                ReplaySaveJson.FormatUtc(value.IssuedAtUtc));
            ReplaySaveJson.AppendString(
                builder,
                "macSha256",
                value.MacSha256);
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                value.SchemaVersion);
            ReplaySaveJson.AppendString(
                builder,
                "sourceSessionId",
                value.SourceSessionId);
            builder.Append('}');
            return ToBoundedBytes(builder);
        }

        public static ColdRestoreClaim Deserialize(byte[] bytes)
        {
            var data = ReplaySaveJson.Deserialize<ColdRestoreClaimData>(
                bytes,
                MaximumBytes);
            try
            {
                var value = new ColdRestoreClaim(
                    data.SchemaVersion,
                    data.ClaimId ?? string.Empty,
                    data.IntentSha256 ?? string.Empty,
                    data.CompanionInstanceId ?? string.Empty,
                    data.SourceSessionId ?? string.Empty,
                    ReplaySaveJson.ParseUtc(
                        data.IssuedAtUtc,
                        "issuedAtUtc"),
                    ReplaySaveJson.ParseUtc(
                        data.ExpiresAtUtc,
                        "expiresAtUtc"),
                    data.MacSha256 ?? string.Empty);
                RequireCanonical(bytes, Serialize(value));
                return value;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException)
            {
                throw new InvalidDataException(
                    "Cold-restore claim is invalid.",
                    exception);
            }
        }

        internal static byte[] SerializeMacPayload(
            string claimId,
            string companionInstanceId,
            DateTimeOffset expiresAtUtc,
            string intentSha256,
            DateTimeOffset issuedAtUtc,
            string sourceSessionId)
        {
            var builder = new StringBuilder(768);
            builder.Append('{');
            ReplaySaveJson.AppendString(builder, "claimId", claimId);
            ReplaySaveJson.AppendString(
                builder,
                "companionInstanceId",
                companionInstanceId);
            ReplaySaveJson.AppendString(
                builder,
                "expiresAtUtc",
                ReplaySaveJson.FormatUtc(expiresAtUtc));
            ReplaySaveJson.AppendString(
                builder,
                "intentSha256",
                intentSha256);
            ReplaySaveJson.AppendString(
                builder,
                "issuedAtUtc",
                ReplaySaveJson.FormatUtc(issuedAtUtc));
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                ColdRestoreClaim.CurrentSchemaVersion);
            ReplaySaveJson.AppendString(
                builder,
                "sourceSessionId",
                sourceSessionId);
            builder.Append('}');
            return ToBoundedBytes(builder);
        }

        internal static void RequireCanonical(
            byte[] actual,
            byte[] expected)
        {
            if (actual == null || actual.Length != expected.Length)
            {
                throw new InvalidDataException(
                    "Cold-restore JSON is not canonical.");
            }

            var difference = 0;
            for (var index = 0; index < actual.Length; index++)
            {
                difference |= actual[index] ^ expected[index];
            }

            if (difference != 0)
            {
                throw new InvalidDataException(
                    "Cold-restore JSON is not canonical.");
            }
        }

        private static byte[] ToBoundedBytes(StringBuilder builder)
        {
            var bytes = ReplaySaveJson.StrictUtf8.GetBytes(
                builder.ToString());
            if (bytes.Length == 0 || bytes.Length > MaximumBytes)
            {
                throw new InvalidDataException(
                    "Cold-restore claim exceeds its size limit.");
            }

            return bytes;
        }
    }

    public enum ColdRestoreClaimValidationCode : byte
    {
        Ready = 1,
        IntentHashMismatch = 2,
        CompanionMismatch = 3,
        SessionMismatch = 4,
        LifetimeMismatch = 5,
        NotYetValid = 6,
        Expired = 7,
        InvalidMac = 8
    }

    public sealed class ColdRestoreClaimValidationResult
    {
        public ColdRestoreClaimValidationResult(
            ColdRestoreClaimValidationCode code,
            string detail)
        {
            if (!Enum.IsDefined(
                    typeof(ColdRestoreClaimValidationCode),
                    code))
            {
                throw new ArgumentOutOfRangeException(nameof(code));
            }

            Code = code;
            Detail = detail ?? string.Empty;
        }

        public ColdRestoreClaimValidationCode Code { get; }
        public string Detail { get; }
        public bool Success => Code == ColdRestoreClaimValidationCode.Ready;
    }

    public static class ColdRestoreClaimAuthenticator
    {
        public const int MinimumSecretBytes = 32;

        public static ColdRestoreClaim Issue(
            ColdRestoreIntent intent,
            string claimId,
            string companionInstanceId,
            DateTimeOffset issuedAtUtc,
            DateTimeOffset expiresAtUtc,
            byte[] secret)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            RequireSecret(secret);
            var issued = issuedAtUtc.ToUniversalTime();
            var expires = expiresAtUtc.ToUniversalTime();
            if (issued < intent.CreatedAtUtc
                || expires <= issued
                || expires > intent.ExpiresAtUtc)
            {
                throw new ArgumentException(
                    "Claim lifetime must be inside the intent lifetime.",
                    nameof(expiresAtUtc));
            }

            ColdRestoreIntent.RequireIdentifier(claimId, nameof(claimId));
            ColdRestoreIntent.RequireIdentifier(
                companionInstanceId,
                nameof(companionInstanceId));
            var intentSha256 = ColdRestoreIntentCodec.ComputeSha256(intent);
            var payload = ColdRestoreClaimCodec.SerializeMacPayload(
                claimId,
                companionInstanceId,
                expires,
                intentSha256,
                issued,
                intent.SourceSessionId);
            var mac = ComputeMac(payload, secret);
            return new ColdRestoreClaim(
                ColdRestoreClaim.CurrentSchemaVersion,
                claimId,
                intentSha256,
                companionInstanceId,
                intent.SourceSessionId,
                issued,
                expires,
                mac);
        }

        public static ColdRestoreClaimValidationResult Validate(
            ColdRestoreClaim claim,
            ColdRestoreIntent intent,
            string expectedCompanionInstanceId,
            DateTimeOffset nowUtc,
            byte[] secret)
        {
            if (claim == null)
            {
                throw new ArgumentNullException(nameof(claim));
            }

            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            RequireSecret(secret);
            if (!string.Equals(
                    claim.IntentSha256,
                    ColdRestoreIntentCodec.ComputeSha256(intent),
                    StringComparison.Ordinal))
            {
                return Fail(
                    ColdRestoreClaimValidationCode.IntentHashMismatch,
                    "Claim is bound to a different intent.");
            }

            if (!string.Equals(
                    claim.CompanionInstanceId,
                    expectedCompanionInstanceId,
                    StringComparison.Ordinal))
            {
                return Fail(
                    ColdRestoreClaimValidationCode.CompanionMismatch,
                    "Claim is bound to a different Companion instance.");
            }

            if (!string.Equals(
                    claim.SourceSessionId,
                    intent.SourceSessionId,
                    StringComparison.Ordinal))
            {
                return Fail(
                    ColdRestoreClaimValidationCode.SessionMismatch,
                    "Claim source session does not match the intent.");
            }

            if (claim.IssuedAtUtc < intent.CreatedAtUtc
                || claim.ExpiresAtUtc > intent.ExpiresAtUtc)
            {
                return Fail(
                    ColdRestoreClaimValidationCode.LifetimeMismatch,
                    "Claim lifetime is outside the intent lifetime.");
            }

            var now = nowUtc.ToUniversalTime();
            if (now < claim.IssuedAtUtc)
            {
                return Fail(
                    ColdRestoreClaimValidationCode.NotYetValid,
                    "Claim issuance time is in the future.");
            }

            if (now >= claim.ExpiresAtUtc || intent.IsExpired(now))
            {
                return Fail(
                    ColdRestoreClaimValidationCode.Expired,
                    "Claim or intent has expired.");
            }

            var payload = ColdRestoreClaimCodec.SerializeMacPayload(
                claim.ClaimId,
                claim.CompanionInstanceId,
                claim.ExpiresAtUtc,
                claim.IntentSha256,
                claim.IssuedAtUtc,
                claim.SourceSessionId);
            var expectedMac = ComputeMac(payload, secret);
            if (!FixedTimeEquals(expectedMac, claim.MacSha256))
            {
                return Fail(
                    ColdRestoreClaimValidationCode.InvalidMac,
                    "Claim authentication failed.");
            }

            return new ColdRestoreClaimValidationResult(
                ColdRestoreClaimValidationCode.Ready,
                string.Empty);
        }

        private static ColdRestoreClaimValidationResult Fail(
            ColdRestoreClaimValidationCode code,
            string detail)
        {
            return new ColdRestoreClaimValidationResult(code, detail);
        }

        private static void RequireSecret(byte[] secret)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }

            if (secret.Length < MinimumSecretBytes)
            {
                throw new ArgumentException(
                    "Cold-restore claim secret is too short.",
                    nameof(secret));
            }
        }

        private static string ComputeMac(byte[] payload, byte[] secret)
        {
            using (var hmac = new HMACSHA256(secret))
            {
                var bytes = hmac.ComputeHash(payload);
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(
                        value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }

    [DataContract]
    internal sealed class ColdRestoreClaimData
    {
        [DataMember(Name = "claimId")]
        public string? ClaimId { get; set; }

        [DataMember(Name = "companionInstanceId")]
        public string? CompanionInstanceId { get; set; }

        [DataMember(Name = "expiresAtUtc")]
        public string? ExpiresAtUtc { get; set; }

        [DataMember(Name = "intentSha256")]
        public string? IntentSha256 { get; set; }

        [DataMember(Name = "issuedAtUtc")]
        public string? IssuedAtUtc { get; set; }

        [DataMember(Name = "macSha256")]
        public string? MacSha256 { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "sourceSessionId")]
        public string? SourceSessionId { get; set; }
    }
}
