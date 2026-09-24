using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.FullRun;
using HollowKnightTAS.Core.Movie;
using HollowKnightTAS.Core.ReplaySave;

namespace HollowKnightTAS.Companion.Services
{
    public sealed class FullRunBootstrapStore
    {
        private readonly string root;
        private byte[]? pendingMovie;
        private FullRunBootDescriptor? staged;
        private bool committed;

        public FullRunBootstrapStore(string localBootstrapRoot)
        {
            if (string.IsNullOrWhiteSpace(localBootstrapRoot))
                throw new ArgumentException("Bootstrap root is required.", nameof(localBootstrapRoot));
            root = Path.GetFullPath(localBootstrapRoot);
        }

        public string GetDescriptorPath(string token)
        {
            if (!FullRunBootDescriptor.IsGateToken(token))
                throw new ArgumentException("Invalid gate token.", nameof(token));
            return Path.Combine(root, token, "boot.json");
        }

        public FullRunBootDescriptor StageRecording(string gateToken, string runId,
            bool mouseEnabled)
        {
            RequireFresh();
            pendingMovie = null;
            staged = new FullRunBootDescriptor(gateToken, runId, "Record",
                mouseEnabled, string.Empty, string.Empty);
            return staged;
        }

        public FullRunBootDescriptor StageReplay(string gateToken, string runId,
            MovieV2Document movie)
        {
            RequireFresh();
            if (movie == null) throw new ArgumentNullException(nameof(movie));
            var canonical = new MovieV2Codec().WriteCanonical(movie);
            pendingMovie = new UTF8Encoding(false, true).GetBytes(canonical);
            var moviePath = Path.Combine(root, gateToken, "movie.hktas");
            staged = new FullRunBootDescriptor(gateToken, runId, "Replay",
                movie.Header.MouseEnabled, moviePath, Sha256Utility.ComputeHex(pendingMovie));
            return staged;
        }

        public string CommitAndHash(FullRunBootDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (committed || !ReferenceEquals(descriptor, staged))
                throw new InvalidOperationException("Only the staged descriptor may be committed once.");
            var directory = Path.Combine(root, descriptor.GateToken);
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Bootstrap identity already exists.");
            Directory.CreateDirectory(directory);
            RestrictToCurrentUser(directory);
            if (descriptor.Mode == "Replay")
            {
                if (pendingMovie == null || descriptor.MoviePath != Path.Combine(directory, "movie.hktas")
                    || Sha256Utility.ComputeHex(pendingMovie) != descriptor.MovieSha256)
                    throw new InvalidDataException("Staged replay movie identity changed.");
                WriteOnce(descriptor.MoviePath, pendingMovie);
            }
            var bytes = FullRunBootDescriptor.Serialize(descriptor);
            WriteOnce(Path.Combine(directory, "boot.json"), bytes);
            committed = true;
            return Sha256Utility.ComputeHex(bytes);
        }

        private void RequireFresh()
        {
            if (staged != null || committed)
                throw new InvalidOperationException("Bootstrap store is already staged.");
        }

        private static void WriteOnce(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }

        private static void RestrictToCurrentUser(string directory)
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new InvalidOperationException("Current Windows SID is unavailable.");
            var security = new DirectorySecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
    }
}
