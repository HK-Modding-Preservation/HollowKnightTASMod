using System;
using System.Text;

namespace HollowKnightTAS.ClockPayload
{
    public static class SceneRngSeedDerivation
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        public static int Derive(
            int rootSeed,
            string targetScene,
            int nextSceneEpoch)
        {
            if (string.IsNullOrEmpty(targetScene))
            {
                throw new ArgumentException(
                    "A target scene is required.",
                    nameof(targetScene));
            }
            if (nextSceneEpoch <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nextSceneEpoch));
            }

            var hash = FnvOffsetBasis;
            AppendInt32(ref hash, rootSeed);
            AppendByte(ref hash, 0xff);
            foreach (var value in Encoding.UTF8.GetBytes(targetScene))
            {
                AppendByte(ref hash, value);
            }
            AppendByte(ref hash, 0);
            AppendInt32(ref hash, nextSceneEpoch);
            return unchecked((int)hash);
        }

        private static void AppendInt32(ref uint hash, int value)
        {
            var bits = unchecked((uint)value);
            AppendByte(ref hash, (byte)bits);
            AppendByte(ref hash, (byte)(bits >> 8));
            AppendByte(ref hash, (byte)(bits >> 16));
            AppendByte(ref hash, (byte)(bits >> 24));
        }

        private static void AppendByte(ref uint hash, byte value)
        {
            hash ^= value;
            hash *= FnvPrime;
        }
    }
}
