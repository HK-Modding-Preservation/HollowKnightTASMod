using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Ipc;

namespace HollowKnightTAS.Core.Deployment
{
    public enum CompanionBundleStatus
    {
        Valid = 0,
        ManifestMissing = 1,
        ManifestInvalid = 2,
        ProductMismatch = 3,
        RidMismatch = 4,
        ProtocolMismatch = 5,
        SignatureInvalid = 6,
        PathRejected = 7,
        FileMissing = 8,
        FileHashMismatch = 9,
        EntrypointMissing = 10,
        ReparsePointRejected = 11,
        VerificationFault = 12
    }

    public sealed class CompanionBundleVerificationResult
    {
        internal CompanionBundleVerificationResult(
            CompanionBundleStatus status,
            string detail,
            CompanionBundleManifest? manifest,
            string? entrypointPath,
            IReadOnlyList<string>? verifiedFiles)
        {
            Status = status;
            Detail = detail;
            Manifest = manifest;
            EntrypointPath = entrypointPath;
            VerifiedFiles = verifiedFiles
                            ?? Array.Empty<string>();
        }

        public bool Success => Status == CompanionBundleStatus.Valid;
        public CompanionBundleStatus Status { get; }
        public string Detail { get; }
        public CompanionBundleManifest? Manifest { get; }
        public string? EntrypointPath { get; }
        public IReadOnlyList<string> VerifiedFiles { get; }
    }

    public static class CompanionPathPolicy
    {
        public static bool TryResolve(
            string rootPath,
            string relativePath,
            out string? fullPath,
            out string error)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(rootPath)
                || string.IsNullOrWhiteSpace(relativePath))
            {
                error = "Root and relative path are required.";
                return false;
            }

            if (Path.IsPathRooted(relativePath)
                || relativePath.IndexOf('\\') >= 0
                || relativePath.IndexOf(':') >= 0
                || relativePath.IndexOf('\0') >= 0
                || relativePath.StartsWith("/", StringComparison.Ordinal)
                || relativePath.EndsWith("/", StringComparison.Ordinal)
                || relativePath.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                error = "Path is not a canonical relative bundle path.";
                return false;
            }

            var segments = relativePath.Split('/');
            if (segments.Length == 0
                || segments.Any(
                    segment => segment.Length == 0
                               || string.Equals(
                                   segment,
                                   ".",
                                   StringComparison.Ordinal)
                               || string.Equals(
                                   segment,
                                   "..",
                                   StringComparison.Ordinal)))
            {
                error = "Path contains a forbidden segment.";
                return false;
            }

            try
            {
                var root = Path.GetFullPath(rootPath)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                var candidate = Path.GetFullPath(
                    Path.Combine(
                        root,
                        relativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
                var rootPrefix = root + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(
                        rootPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Path escapes the bundle root.";
                    return false;
                }

                fullPath = candidate;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      || exception is NotSupportedException
                      || exception is PathTooLongException)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    public static class CompanionBundleVerifier
    {
        public static CompanionBundleVerificationResult Verify(
            string modRoot,
            string manifestPath,
            string expectedRid,
            ProtocolRange runtimeProtocols,
            RsaPublicKey publicKey)
        {
            try
            {
                if (!File.Exists(manifestPath))
                {
                    return Fail(
                        CompanionBundleStatus.ManifestMissing,
                        "Companion manifest is missing.");
                }

                if (IsReparsePoint(manifestPath))
                {
                    return Fail(
                        CompanionBundleStatus.ReparsePointRejected,
                        "Companion manifest is a reparse point.");
                }

                var manifestBytes = File.ReadAllBytes(manifestPath);
                var decode = CompanionManifestCodec.TryDeserialize(
                    manifestBytes);
                if (!decode.Success || decode.Manifest == null)
                {
                    return Fail(
                        CompanionBundleStatus.ManifestInvalid,
                        decode.ErrorCode + ": " + decode.Error);
                }

                var manifest = decode.Manifest;
                if (manifest.SchemaVersion != 1
                    || !string.Equals(
                        manifest.Product,
                        CompanionManifestCodec.Product,
                        StringComparison.Ordinal)
                    || !IsSafeVersion(manifest.Version))
                {
                    return Fail(
                        CompanionBundleStatus.ProductMismatch,
                        "Companion product/schema/version is invalid.",
                        manifest);
                }

                if (!string.Equals(
                        manifest.Rid,
                        expectedRid,
                        StringComparison.Ordinal))
                {
                    return Fail(
                        CompanionBundleStatus.RidMismatch,
                        "Companion RID does not match.",
                        manifest);
                }

                ProtocolRange manifestProtocols;
                try
                {
                    manifestProtocols = new ProtocolRange(
                        manifest.RuntimeProtocolMinimum,
                        manifest.RuntimeProtocolMaximum);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    return Fail(
                        CompanionBundleStatus.ProtocolMismatch,
                        exception.Message,
                        manifest);
                }

                if (!runtimeProtocols.Intersects(manifestProtocols))
                {
                    return Fail(
                        CompanionBundleStatus.ProtocolMismatch,
                        "Runtime and Companion protocols do not intersect.",
                        manifest);
                }

                if (!VerifySignature(manifest, publicKey))
                {
                    return Fail(
                        CompanionBundleStatus.SignatureInvalid,
                        "Companion manifest signature is invalid.",
                        manifest);
                }

                if (manifest.Files.Count == 0)
                {
                    return Fail(
                        CompanionBundleStatus.EntrypointMissing,
                        "Companion manifest declares no files.",
                        manifest);
                }

                var declared = new HashSet<string>(
                    StringComparer.Ordinal);
                var verified = new List<string>(manifest.Files.Count);
                string? entrypoint = null;
                foreach (var file in manifest.Files)
                {
                    if (!declared.Add(file.Path)
                        || !IsLowerSha256(file.Sha256))
                    {
                        return Fail(
                            CompanionBundleStatus.PathRejected,
                            "Duplicate path or invalid SHA-256: "
                            + file.Path,
                            manifest);
                    }

                    if (!CompanionPathPolicy.TryResolve(
                            modRoot,
                            file.Path,
                            out var fullPath,
                            out var pathError)
                        || fullPath == null)
                    {
                        return Fail(
                            CompanionBundleStatus.PathRejected,
                            "Rejected bundle path "
                            + file.Path
                            + ": "
                            + pathError,
                            manifest);
                    }

                    if (!File.Exists(fullPath))
                    {
                        return Fail(
                            CompanionBundleStatus.FileMissing,
                            "Declared bundle file is missing: "
                            + file.Path,
                            manifest);
                    }

                    if (ContainsReparsePoint(
                            modRoot,
                            file.Path))
                    {
                        return Fail(
                            CompanionBundleStatus.ReparsePointRejected,
                            "Bundle path traverses a reparse point: "
                            + file.Path,
                            manifest);
                    }

                    var actualHash =
                        Sha256Utility.ComputeFileHex(fullPath);
                    if (!string.Equals(
                            actualHash,
                            file.Sha256,
                            StringComparison.Ordinal))
                    {
                        return Fail(
                            CompanionBundleStatus.FileHashMismatch,
                            "Bundle hash mismatch: " + file.Path,
                            manifest);
                    }

                    verified.Add(fullPath);
                    if (string.Equals(
                            file.Path,
                            manifest.Entrypoint,
                            StringComparison.Ordinal))
                    {
                        entrypoint = fullPath;
                    }
                }

                if (entrypoint == null)
                {
                    return Fail(
                        CompanionBundleStatus.EntrypointMissing,
                        "Entrypoint is not a declared file.",
                        manifest);
                }

                return new CompanionBundleVerificationResult(
                    CompanionBundleStatus.Valid,
                    "Companion bundle verified.",
                    manifest,
                    entrypoint,
                    verified);
            }
            catch (Exception exception)
            {
                return Fail(
                    CompanionBundleStatus.VerificationFault,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public static string Sign(
            CompanionBundleManifest manifest,
            RSA privateKey)
        {
            if (privateKey == null)
            {
                throw new ArgumentNullException(nameof(privateKey));
            }

            var signature = privateKey.SignData(
                CompanionManifestCodec.SerializeUnsigned(manifest),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(signature);
        }

        private static bool VerifySignature(
            CompanionBundleManifest manifest,
            RsaPublicKey key)
        {
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(
                    manifest.Signature);
            }
            catch (FormatException)
            {
                return false;
            }

            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(
                    new RSAParameters
                    {
                        Modulus = key.Modulus,
                        Exponent = key.Exponent
                    });
                return rsa.VerifyData(
                    CompanionManifestCodec.SerializeUnsigned(manifest),
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
        }

        private static bool ContainsReparsePoint(
            string rootPath,
            string relativePath)
        {
            var current = Path.GetFullPath(rootPath);
            foreach (var segment in relativePath.Split('/'))
            {
                current = Path.Combine(current, segment);
                if (IsReparsePoint(current))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path)
                    & FileAttributes.ReparsePoint)
                   != 0;
        }

        private static bool IsLowerSha256(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            return value.All(
                character => character >= '0'
                             && character <= '9'
                             || character >= 'a'
                             && character <= 'f');
        }

        private static bool IsSafeVersion(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
            {
                return false;
            }

            return value.All(
                character => char.IsLetterOrDigit(character)
                             || character == '.'
                             || character == '-'
                             || character == '+');
        }

        private static CompanionBundleVerificationResult Fail(
            CompanionBundleStatus status,
            string detail,
            CompanionBundleManifest? manifest = null)
        {
            return new CompanionBundleVerificationResult(
                status,
                detail,
                manifest,
                null,
                null);
        }
    }
}
