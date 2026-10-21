using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using nem.Common;

namespace nem.Services;

/// <summary>
/// Lets one install at a time work on an env. Two tools started together - an
/// IDE launching 'ng serve' and 'npm run watch' - would otherwise both install
/// into the same .nenv. The lock file lives in the nem system folder, not in the
/// env: 'nem install --clean' wipes the env, and an open file there would stop it.
/// </summary>
public sealed class InstallLock : IDisposable
{
    // How often a waiting install checks whether the lock is free again.
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    readonly FileStream _file;

    InstallLock(FileStream file) => _file = file;

    public void Dispose() => _file.Dispose();

    /// <summary>
    /// Takes the lock of <paramref name="envDir"/>, waiting up to
    /// <paramref name="timeout"/> for another install to finish.
    /// <paramref name="onWait"/> runs once, the first time the lock is busy.
    /// Returns null when the wait timed out.
    /// </summary>
    public static InstallLock? Acquire(string envDir, TimeSpan timeout, Action? onWait = null) =>
        AcquireFile(LockFilePath(IOPathManager.System.LockDirPath, envDir), timeout, onWait);

    internal static InstallLock? AcquireFile(string lockFilePath, TimeSpan timeout, Action? onWait)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        var waited = Stopwatch.StartNew();
        bool reportedWait = false;

        while (true)
        {
            try
            {
                // FileShare.None is the lock: Windows refuses a second open, and .NET
                // takes an exclusive flock on Unix.
                return new InstallLock(new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException)
            {
                if (waited.Elapsed >= timeout)
                    return null;
                if (!reportedWait)
                {
                    onWait?.Invoke();
                    reportedWait = true;
                }
                Thread.Sleep(PollInterval);
            }
        }
    }

    /// <summary>
    /// The lock file of an env: one per env folder, named after a hash of its path
    /// so any path fits into a file name.
    /// </summary>
    internal static string LockFilePath(string lockDir, string envDir)
    {
        string key = Path.GetFullPath(envDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Windows paths are case-insensitive, so 'C:\Repo' and 'c:\repo' must share a lock.
        if (OperatingSystem.IsWindows())
            key = key.ToUpperInvariant();

        // 16 hex digits (64 bits) keep any two env folders apart and the name short.
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(lockDir, $"install-{hash}.lock");
    }
}
