using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HollowKnightTAS.Core.Deployment
{
    public sealed class CompanionManifestFile
    {
        public CompanionManifestFile(string path, string sha256)
        {
            Path = path
                   ?? throw new ArgumentNullException(nameof(path));
            Sha256 = sha256
                     ?? throw new ArgumentNullException(nameof(sha256));
        }

        public string Path { get; }
        public string Sha256 { get; }
    }

    public sealed class CompanionBundleManifest
    {
        public CompanionBundleManifest(
            int schemaVersion,
            string product,
            string version,
            string rid,
            int runtimeProtocolMinimum,
            int runtimeProtocolMaximum,
            string entrypoint,
            IEnumerable<CompanionManifestFile> files,
            string signature)
        {
            SchemaVersion = schemaVersion;
            Product = product
                      ?? throw new ArgumentNullException(nameof(product));
            Version = version
                      ?? throw new ArgumentNullException(nameof(version));
            Rid = rid
                  ?? throw new ArgumentNullException(nameof(rid));
            RuntimeProtocolMinimum = runtimeProtocolMinimum;
            RuntimeProtocolMaximum = runtimeProtocolMaximum;
            Entrypoint = entrypoint
                         ?? throw new ArgumentNullException(
                             nameof(entrypoint));
            Files = new ReadOnlyCollection<CompanionManifestFile>(
                (files ?? throw new ArgumentNullException(nameof(files)))
                .ToArray());
            Signature = signature
                        ?? throw new ArgumentNullException(
                            nameof(signature));
        }

        public int SchemaVersion { get; }
        public string Product { get; }
        public string Version { get; }
        public string Rid { get; }
        public int RuntimeProtocolMinimum { get; }
        public int RuntimeProtocolMaximum { get; }
        public string Entrypoint { get; }
        public IReadOnlyList<CompanionManifestFile> Files { get; }
        public string Signature { get; }

        public CompanionBundleManifest WithSignature(string signature)
        {
            return new CompanionBundleManifest(
                SchemaVersion,
                Product,
                Version,
                Rid,
                RuntimeProtocolMinimum,
                RuntimeProtocolMaximum,
                Entrypoint,
                Files,
                signature);
        }
    }

    public sealed class RsaPublicKey
    {
        private readonly byte[] modulus;
        private readonly byte[] exponent;

        public RsaPublicKey(byte[] modulus, byte[] exponent)
        {
            if (modulus == null || modulus.Length < 256)
            {
                throw new ArgumentException(
                    "RSA modulus must contain at least 2048 bits.",
                    nameof(modulus));
            }

            if (exponent == null || exponent.Length == 0)
            {
                throw new ArgumentException(
                    "RSA exponent is required.",
                    nameof(exponent));
            }

            this.modulus = (byte[])modulus.Clone();
            this.exponent = (byte[])exponent.Clone();
        }

        public byte[] Modulus => (byte[])modulus.Clone();
        public byte[] Exponent => (byte[])exponent.Clone();
    }
}
