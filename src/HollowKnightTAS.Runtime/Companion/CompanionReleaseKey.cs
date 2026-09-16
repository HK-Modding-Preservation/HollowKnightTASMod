using System;
using HollowKnightTAS.Core.Deployment;

namespace HollowKnightTAS.Runtime.Companion
{
    internal static class CompanionReleaseKey
    {
        private const string ModulusBase64 =
            "tXD2Le2gDa5d6qSZkaT3LZZAeAg9Ze+c48fu/CnSCiHfHnA8h18YTaIM7Y2JlmravKcvzgTE26LbKsEiOuZx4IYIlBmm28Wi1Rsdo0Lqs2s3nvfyHpKP75FtrtmE3swozJHovMou8S6z8ge8JsvqOqBO9Z56Tnf5niCy8R5cdHSc7Cv0BBEByovMfGkS65mOz1vi0KUxvU+qDHWjPvVBt1Tg92ZMnUXF3vwSTu3B8iX8QS25uvfcFADBT3XqbMc08phWFbOlC+cBr0nfdUAef2z8f3d6EQvdN0ScmqbJboEYYy8qRURqg9Hka8JrU24vKlCoTND8aqlarbdLfsRbYMicqvbrS1fi1vOHB4EEP11eh3e65sTSkGTt5t5mWrjxrM5/+l0UKTh1kTq7TerqGlQ3LD+Pd/G3Sj9vLxNIBI0ikyTCJOyNG7ldKwGtaFAOSk5Egi5Bwgk3GEltgDlJqAC3K9PqK9EpVo2TNOBt1PmCXVw2UkAAJlbOMllNVa/R";
        private const string ExponentBase64 = "AQAB";

        public static RsaPublicKey Create()
        {
            return new RsaPublicKey(
                Convert.FromBase64String(ModulusBase64),
                Convert.FromBase64String(ExponentBase64));
        }
    }
}
