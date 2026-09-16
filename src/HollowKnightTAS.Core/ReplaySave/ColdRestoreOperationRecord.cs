using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using HollowKnightTAS.Core.Cryptography;

namespace HollowKnightTAS.Core.ReplaySave
{
    public enum ColdRestoreActorRole : byte
    {
        Runtime = 1,
        Companion = 2
    }

    public sealed class ColdRestoreOperationRecord
    {
        public const int CurrentSchemaVersion = 1;
        public static readonly string GenesisPreviousSha256 =
            new string('0', 64);

        public ColdRestoreOperationRecord(
            int schemaVersion,
            string operationId,
            string intentSha256,
            string claimId,
            long sequence,
            ColdRestoreOperationState state,
            string previousRecordSha256,
            DateTimeOffset occurredAtUtc,
            ColdRestoreActorRole actorRole,
            string actorInstanceId,
            string actorSessionId,
            int processId,
            DateTimeOffset processStartedAtUtc,
            string detailCode)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (!Enum.IsDefined(typeof(ColdRestoreOperationState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            if (!Enum.IsDefined(typeof(ColdRestoreActorRole), actorRole))
            {
                throw new ArgumentOutOfRangeException(nameof(actorRole));
            }

            ColdRestoreOperationStateMachine.RequireActor(state, actorRole);

            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            SchemaVersion = schemaVersion;
            OperationId = ColdRestoreIntent.RequireIdentifier(
                operationId,
                nameof(operationId));
            IntentSha256 = ReplaySaveDescriptor.RequireSha256(
                intentSha256,
                nameof(intentSha256));
            ClaimId = ColdRestoreIntent.RequireIdentifier(
                claimId,
                nameof(claimId));
            Sequence = sequence;
            State = state;
            PreviousRecordSha256 = ReplaySaveDescriptor.RequireSha256(
                previousRecordSha256,
                nameof(previousRecordSha256));
            if (sequence == 0)
            {
                if (state != ColdRestoreOperationState.Prepared
                    || !string.Equals(
                        PreviousRecordSha256,
                        GenesisPreviousSha256,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "The genesis record must be Prepared with the genesis hash.",
                        nameof(state));
                }
            }
            else if (state == ColdRestoreOperationState.Prepared
                     || string.Equals(
                         PreviousRecordSha256,
                         GenesisPreviousSha256,
                         StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Only the genesis record may be Prepared or use the genesis hash.",
                    nameof(state));
            }

            OccurredAtUtc = occurredAtUtc.ToUniversalTime();
            ActorRole = actorRole;
            ActorInstanceId = ColdRestoreIntent.RequireIdentifier(
                actorInstanceId,
                nameof(actorInstanceId));
            ActorSessionId = ColdRestoreIntent.RequireOptionalIdentifier(
                actorSessionId,
                nameof(actorSessionId));
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            ProcessId = processId;
            ProcessStartedAtUtc = processStartedAtUtc.ToUniversalTime();
            if (ProcessStartedAtUtc > OccurredAtUtc)
            {
                throw new ArgumentException(
                    "Actor process start cannot follow the event.",
                    nameof(processStartedAtUtc));
            }

            DetailCode = ColdRestoreIntent.RequireIdentifier(
                detailCode,
                nameof(detailCode));
        }

        public int SchemaVersion { get; }
        public string OperationId { get; }
        public string IntentSha256 { get; }
        public string ClaimId { get; }
        public long Sequence { get; }
        public ColdRestoreOperationState State { get; }
        public string PreviousRecordSha256 { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public ColdRestoreActorRole ActorRole { get; }
        public string ActorInstanceId { get; }
        public string ActorSessionId { get; }
        public int ProcessId { get; }
        public DateTimeOffset ProcessStartedAtUtc { get; }
        public string DetailCode { get; }

        public ColdRestoreOperationRecord CreateNext(
            ColdRestoreOperationState next,
            DateTimeOffset occurredAtUtc,
            ColdRestoreActorRole actorRole,
            string actorInstanceId,
            string actorSessionId,
            int processId,
            DateTimeOffset processStartedAtUtc,
            string detailCode)
        {
            ColdRestoreOperationStateMachine.RequireTransition(State, next);
            ColdRestoreOperationStateMachine.RequireActor(next, actorRole);
            var occurred = occurredAtUtc.ToUniversalTime();
            if (occurred < OccurredAtUtc)
            {
                throw new ArgumentException(
                    "Operation events must be monotonic in UTC.",
                    nameof(occurredAtUtc));
            }

            return new ColdRestoreOperationRecord(
                CurrentSchemaVersion,
                OperationId,
                IntentSha256,
                ClaimId,
                checked(Sequence + 1),
                next,
                ColdRestoreOperationRecordCodec.ComputeSha256(this),
                occurred,
                actorRole,
                actorInstanceId,
                actorSessionId,
                processId,
                processStartedAtUtc,
                detailCode);
        }
    }

    public static class ColdRestoreOperationRecordCodec
    {
        public const int MaximumBytes = 32 * 1024;

        public static byte[] Serialize(ColdRestoreOperationRecord value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(1536);
            builder.Append('{');
            ReplaySaveJson.AppendString(
                builder,
                "actorInstanceId",
                value.ActorInstanceId);
            ReplaySaveJson.AppendString(
                builder,
                "actorRole",
                value.ActorRole.ToString());
            ReplaySaveJson.AppendString(
                builder,
                "actorSessionId",
                value.ActorSessionId);
            ReplaySaveJson.AppendString(
                builder,
                "claimId",
                value.ClaimId);
            ReplaySaveJson.AppendString(
                builder,
                "detailCode",
                value.DetailCode);
            ReplaySaveJson.AppendString(
                builder,
                "intentSha256",
                value.IntentSha256);
            ReplaySaveJson.AppendString(
                builder,
                "occurredAtUtc",
                ReplaySaveJson.FormatUtc(value.OccurredAtUtc));
            ReplaySaveJson.AppendString(
                builder,
                "operationId",
                value.OperationId);
            ReplaySaveJson.AppendString(
                builder,
                "previousRecordSha256",
                value.PreviousRecordSha256);
            ReplaySaveJson.AppendInt32(
                builder,
                "processId",
                value.ProcessId);
            ReplaySaveJson.AppendString(
                builder,
                "processStartedAtUtc",
                ReplaySaveJson.FormatUtc(value.ProcessStartedAtUtc));
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                value.SchemaVersion);
            ReplaySaveJson.AppendInt64(
                builder,
                "sequence",
                value.Sequence);
            ReplaySaveJson.AppendString(
                builder,
                "state",
                value.State.ToString());
            builder.Append('}');
            var bytes = ReplaySaveJson.StrictUtf8.GetBytes(builder.ToString());
            if (bytes.Length == 0 || bytes.Length > MaximumBytes)
            {
                throw new InvalidDataException(
                    "Cold-restore operation record exceeds its size limit.");
            }

            return bytes;
        }

        public static ColdRestoreOperationRecord Deserialize(byte[] bytes)
        {
            var data = ReplaySaveJson.Deserialize<ColdRestoreOperationRecordData>(
                bytes,
                MaximumBytes);
            try
            {
                if (!Enum.TryParse(
                        data.State,
                        ignoreCase: false,
                        out ColdRestoreOperationState state)
                    || !Enum.IsDefined(
                        typeof(ColdRestoreOperationState),
                        state))
                {
                    throw new InvalidDataException(
                        "Cold-restore operation state is invalid.");
                }

                if (!Enum.TryParse(
                        data.ActorRole,
                        ignoreCase: false,
                        out ColdRestoreActorRole actorRole)
                    || !Enum.IsDefined(
                        typeof(ColdRestoreActorRole),
                        actorRole))
                {
                    throw new InvalidDataException(
                        "Cold-restore actor role is invalid.");
                }

                var value = new ColdRestoreOperationRecord(
                    data.SchemaVersion,
                    data.OperationId ?? string.Empty,
                    data.IntentSha256 ?? string.Empty,
                    data.ClaimId ?? string.Empty,
                    data.Sequence,
                    state,
                    data.PreviousRecordSha256 ?? string.Empty,
                    ReplaySaveJson.ParseUtc(
                        data.OccurredAtUtc,
                        "occurredAtUtc"),
                    actorRole,
                    data.ActorInstanceId ?? string.Empty,
                    data.ActorSessionId ?? string.Empty,
                    data.ProcessId,
                    ReplaySaveJson.ParseUtc(
                        data.ProcessStartedAtUtc,
                        "processStartedAtUtc"),
                    data.DetailCode ?? string.Empty);
                ColdRestoreClaimCodec.RequireCanonical(bytes, Serialize(value));
                return value;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException)
            {
                throw new InvalidDataException(
                    "Cold-restore operation record is invalid.",
                    exception);
            }
        }

        public static string ComputeSha256(ColdRestoreOperationRecord value)
        {
            return Sha256Utility.ComputeHex(Serialize(value));
        }
    }

    [DataContract]
    internal sealed class ColdRestoreOperationRecordData
    {
        [DataMember(Name = "actorInstanceId")]
        public string? ActorInstanceId { get; set; }

        [DataMember(Name = "actorRole")]
        public string? ActorRole { get; set; }

        [DataMember(Name = "actorSessionId")]
        public string? ActorSessionId { get; set; }

        [DataMember(Name = "claimId")]
        public string? ClaimId { get; set; }

        [DataMember(Name = "detailCode")]
        public string? DetailCode { get; set; }

        [DataMember(Name = "intentSha256")]
        public string? IntentSha256 { get; set; }

        [DataMember(Name = "occurredAtUtc")]
        public string? OccurredAtUtc { get; set; }

        [DataMember(Name = "operationId")]
        public string? OperationId { get; set; }

        [DataMember(Name = "previousRecordSha256")]
        public string? PreviousRecordSha256 { get; set; }

        [DataMember(Name = "processId")]
        public int ProcessId { get; set; }

        [DataMember(Name = "processStartedAtUtc")]
        public string? ProcessStartedAtUtc { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "sequence")]
        public long Sequence { get; set; }

        [DataMember(Name = "state")]
        public string? State { get; set; }
    }
}
