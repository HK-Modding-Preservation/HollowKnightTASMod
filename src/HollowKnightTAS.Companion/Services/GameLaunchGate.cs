using System;
using System.IO;

namespace HollowKnightTAS.Companion.Services
{
    // File sharing is process-wide and releases on process exit. Unlike a
    // thread-owned mutex, the handle can safely span asynchronous launch work.
    public sealed class GameLaunchGate : IDisposable
    {
        private readonly FileStream handle;

        private GameLaunchGate(FileStream handle) { this.handle = handle; }

        public static GameLaunchGate Acquire(string? lockDirectory = null)
        {
            var directory = lockDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HollowKnightTAS", "locks");
            Directory.CreateDirectory(directory);
            try
            {
                return new GameLaunchGate(new FileStream(Path.Combine(directory, "game-launch.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    "Another game launch is active, or the launch lock is unavailable. No game was started.", exception);
            }
        }

        // Keep the zero-byte file: deleting it would allow different callers
        // to hold different file objects under the same name on some systems.
        public void Dispose() => handle.Dispose();
    }
}
