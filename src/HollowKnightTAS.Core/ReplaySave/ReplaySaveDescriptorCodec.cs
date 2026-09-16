using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.ReplaySave
{
    public static class ReplaySaveDescriptorCodec
    {
        public const int MaximumBytes = 256 * 1024;

        public static byte[] Serialize(ReplaySaveDescriptor value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(2048);
            Append(builder, value);
            var bytes = ReplaySaveJson.StrictUtf8.GetBytes(builder.ToString());
            if (bytes.Length > MaximumBytes)
            {
                throw new InvalidDataException(
                    "Replay-save descriptor exceeds its size limit.");
            }

            return bytes;
        }

        public static ReplaySaveDescriptor Deserialize(byte[] bytes)
        {
            var data = ReplaySaveJson.Deserialize<ReplaySaveDescriptorData>(
                bytes,
                MaximumBytes);
            if (!Enum.TryParse(
                    data.Reason,
                    ignoreCase: false,
                    out ReplaySaveReason reason)
                || !Enum.IsDefined(typeof(ReplaySaveReason), reason))
            {
                throw new InvalidDataException(
                    "Replay-save reason is invalid.");
            }

            try
            {
                return new ReplaySaveDescriptor(
                    data.SchemaVersion,
                    data.ReplaySaveId ?? string.Empty,
                    data.Label ?? string.Empty,
                    reason,
                    ReplaySaveJson.ParseUtc(
                        data.RequestedAtUtc,
                        "requestedAtUtc"),
                    ReplaySaveJson.ParseUtc(
                        data.CreatedAtUtc,
                        "createdAtUtc"),
                    data.RequestedAtMovieTick,
                    data.EffectiveMovieTick,
                    data.ManifestSha256 ?? string.Empty,
                    data.BaselineObjectSha256 ?? string.Empty,
                    data.BaselineId ?? string.Empty,
                    data.BaselineSemanticSha256 ?? string.Empty,
                    data.MovieObjectSha256 ?? string.Empty,
                    data.JournalHeadSha256 ?? string.Empty,
                    data.JournalSegmentObjectSha256s
                    ?? Array.Empty<string>(),
                    data.SemanticSnapshotSha256 ?? string.Empty,
                    data.LedgerSummarySha256 ?? string.Empty,
                    data.SceneName ?? string.Empty,
                    data.SceneEpoch,
                    data.AutoRetentionCount,
                    data.LifecycleObjectSha256);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is ArgumentOutOfRangeException)
            {
                throw new InvalidDataException(
                    "Replay-save descriptor invariants are invalid.",
                    exception);
            }
        }

        internal static void Append(
            StringBuilder builder,
            ReplaySaveDescriptor value)
        {
            builder.Append('{');
            ReplaySaveJson.AppendInt32(
                builder,
                "autoRetentionCount",
                value.AutoRetentionCount);
            ReplaySaveJson.AppendString(
                builder,
                "baselineId",
                value.BaselineId);
            ReplaySaveJson.AppendString(
                builder,
                "baselineObjectSha256",
                value.BaselineObjectSha256);
            ReplaySaveJson.AppendString(
                builder,
                "baselineSemanticSha256",
                value.BaselineSemanticSha256);
            ReplaySaveJson.AppendString(
                builder,
                "createdAtUtc",
                ReplaySaveJson.FormatUtc(value.CreatedAtUtc));
            ReplaySaveJson.AppendInt64(
                builder,
                "effectiveMovieTick",
                value.EffectiveMovieTick);
            ReplaySaveJson.AppendString(
                builder,
                "journalHeadSha256",
                value.JournalHeadSha256);

            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(
                builder,
                "journalSegmentObjectSha256s");
            builder.Append(":[");
            for (var index = 0;
                 index < value.JournalSegmentObjectSha256s.Count;
                 index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                CanonicalJsonWriter.AppendString(
                    builder,
                    value.JournalSegmentObjectSha256s[index]);
            }

            builder.Append(']');
            ReplaySaveJson.AppendString(
                builder,
                "label",
                value.Label);
            ReplaySaveJson.AppendString(
                builder,
                "ledgerSummarySha256",
                value.LedgerSummarySha256);
            if (value.LifecycleObjectSha256 != null)
                ReplaySaveJson.AppendString(builder, "lifecycleObjectSha256", value.LifecycleObjectSha256);
            ReplaySaveJson.AppendString(
                builder,
                "manifestSha256",
                value.ManifestSha256);
            ReplaySaveJson.AppendString(
                builder,
                "movieObjectSha256",
                value.MovieObjectSha256);
            ReplaySaveJson.AppendString(
                builder,
                "reason",
                value.Reason.ToString());
            ReplaySaveJson.AppendString(
                builder,
                "replaySaveId",
                value.ReplaySaveId);
            ReplaySaveJson.AppendString(
                builder,
                "requestedAtUtc",
                ReplaySaveJson.FormatUtc(value.RequestedAtUtc));
            ReplaySaveJson.AppendInt64(
                builder,
                "requestedAtMovieTick",
                value.RequestedAtMovieTick);
            ReplaySaveJson.AppendInt32(
                builder,
                "sceneEpoch",
                value.SceneEpoch);
            ReplaySaveJson.AppendString(
                builder,
                "sceneName",
                value.SceneName);
            ReplaySaveJson.AppendInt32(
                builder,
                "schemaVersion",
                value.SchemaVersion);
            ReplaySaveJson.AppendString(
                builder,
                "semanticSnapshotSha256",
                value.SemanticSnapshotSha256);
            builder.Append('}');
        }
    }
}
