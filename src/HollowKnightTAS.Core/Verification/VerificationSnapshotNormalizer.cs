using System;
using System.Globalization;
using HollowKnightTAS.Core.State;

namespace HollowKnightTAS.Core.Verification
{
    public static class VerificationSnapshotNormalizer
    {
        public const string ProjectionId = "v1-float32-decimal-4";
        private const int FloatDecimalPlaces = 4;

        public static byte[] Normalize(byte[] canonicalSnapshotBytes)
        {
            if (canonicalSnapshotBytes == null)
            {
                throw new ArgumentNullException(
                    nameof(canonicalSnapshotBytes));
            }

            var source = SemanticSnapshotCanonicalizer.Deserialize(
                canonicalSnapshotBytes);
            var builder = new SemanticSnapshotBuilder(source.SchemaVersion);
            foreach (var key in SemanticSnapshotSchemas.Keys(source.SchemaVersion))
            {
                var value = source.Values[key];
                switch (value.Kind)
                {
                    case SemanticValueKind.Boolean:
                        builder.AddBoolean(
                            key,
                            bool.Parse(value.DisplayValue));
                        break;
                    case SemanticValueKind.Int32:
                        builder.AddInt32(
                            key,
                            int.Parse(
                                value.DisplayValue,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture));
                        break;
                    case SemanticValueKind.Int64:
                        builder.AddInt64(
                            key,
                            long.Parse(
                                value.DisplayValue,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture));
                        break;
                    case SemanticValueKind.Float32Bits:
                        builder.AddFloat32(
                            key,
                            NormalizeFloat(
                                float.Parse(
                                    value.DisplayValue,
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture)));
                        break;
                    case SemanticValueKind.Utf8String:
                        builder.AddString(key, value.DisplayValue);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unsupported semantic value kind: "
                            + value.Kind);
                }
            }

            return SemanticSnapshotCanonicalizer.Serialize(builder.Build());
        }

        private static float NormalizeFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return value;
            }

            var rounded = (float)Math.Round(
                value,
                FloatDecimalPlaces,
                MidpointRounding.AwayFromZero);
            return rounded == 0f ? 0f : rounded;
        }
    }
}
