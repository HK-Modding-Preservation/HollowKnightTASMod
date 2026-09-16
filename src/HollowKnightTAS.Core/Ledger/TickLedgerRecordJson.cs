using System;
using System.Globalization;
using System.Text;
using HollowKnightTAS.Core.Serialization;

namespace HollowKnightTAS.Core.Ledger
{
    public static class TickLedgerRecordJson
    {
        public const int SchemaVersion = 1;

        public static string Serialize(TickLedgerRecord value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(768);
            builder.Append('{');
            AppendNumber(builder, "schemaVersion", SchemaVersion);
            AppendString(builder, "sessionId", value.SessionId);
            AppendString(builder, "manifestSha256", value.ManifestSha256);
            AppendString(builder, "runId", value.RunId);
            AppendString(builder, "profile", value.Profile);
            AppendNumber(builder, "sequence", value.Sequence);
            AppendUnsigned(builder, "inputTick", value.Stamp.InputTick);
            AppendNumber(builder, "visualTick", value.Stamp.VisualTick);
            AppendNumber(builder, "fixedTick", value.Stamp.FixedTick);
            AppendNumber(builder, "sceneEpoch", value.Stamp.SceneEpoch);
            AppendString(builder, "phase", value.Stamp.Phase.ToString());
            AppendNumber(
                builder,
                "fixedStepsSincePreviousVisual",
                value.FixedStepsSincePreviousVisual);
            AppendFloat(builder, "time", "timeBits", value.TimeBits);
            AppendFloat(builder, "fixedTime", "fixedTimeBits", value.FixedTimeBits);
            AppendFloat(
                builder,
                "timeMinusFixedTime",
                "timeMinusFixedTimeBits",
                value.TimeMinusFixedTimeBits);
            AppendFloat(builder, "deltaTime", "deltaTimeBits", value.DeltaTimeBits);
            AppendFloat(
                builder,
                "unscaledDeltaTime",
                "unscaledDeltaTimeBits",
                value.UnscaledDeltaTimeBits);
            AppendFloat(builder, "timeScale", "timeScaleBits", value.TimeScaleBits);
            AppendFloat(
                builder,
                "realtimeSinceStartup",
                "realtimeSinceStartupBits",
                value.RealtimeSinceStartupBits);
            AppendString(builder, "sceneName", value.SceneName);
            AppendString(builder, "detail", value.Detail);
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendFloat(
            StringBuilder builder,
            string valueName,
            string bitsName,
            int bits)
        {
            AppendString(
                builder,
                valueName,
                SingleBits.ToSingle(bits).ToString("R", CultureInfo.InvariantCulture));
            AppendNumber(builder, bitsName, bits);
        }

        private static void AppendString(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendPropertyPrefix(builder, name);
            CanonicalJsonWriter.AppendString(builder, value);
        }

        private static void AppendNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendUnsigned(
            StringBuilder builder,
            string name,
            ulong value)
        {
            AppendPropertyPrefix(builder, name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendPropertyPrefix(StringBuilder builder, string name)
        {
            if (builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.AppendString(builder, name);
            builder.Append(':');
        }
    }
}
