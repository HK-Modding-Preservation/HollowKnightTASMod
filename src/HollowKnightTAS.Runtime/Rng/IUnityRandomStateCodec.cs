using HollowKnightTAS.Core.Rng;

namespace HollowKnightTAS.Runtime.Rng
{
    public interface IUnityRandomStateCodec
    {
        string CodecId { get; }
        byte[] Encode(UnityEngine.Random.State state);
        RngStateFingerprint Fingerprint(UnityEngine.Random.State state);
        RngStateFingerprint CaptureCurrent();
    }
}
