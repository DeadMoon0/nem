using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// One install per store entry at a time: a second one waits for the first,
/// installs of other entries do not.
/// </summary>
public class InstallLockTests
{
    [Fact]
    public void A_second_install_of_the_same_entry_waits_for_the_first()
    {
        using var tmp = new TempDir();
        string lockFile = InstallLock.LockFilePath(tmp.FullName, Path.Combine(tmp.FullName, "store", "node", "22.23.3"));
        int waits = 0;

        using (InstallLock? first = InstallLock.AcquireFile(lockFile, TimeSpan.Zero, onWait: null))
        {
            Assert.NotNull(first);
            Assert.Null(InstallLock.AcquireFile(lockFile, TimeSpan.FromMilliseconds(300), onWait: () => waits++));
        }

        Assert.Equal(1, waits);
        using InstallLock? afterwards = InstallLock.AcquireFile(lockFile, TimeSpan.Zero, onWait: null);
        Assert.NotNull(afterwards);
    }

    [Fact]
    public void Installs_of_different_entries_do_not_wait_for_each_other()
    {
        using var tmp = new TempDir();
        string lockA = InstallLock.LockFilePath(tmp.FullName, Path.Combine(tmp.FullName, "store", "node", "20.20.2"));
        string lockB = InstallLock.LockFilePath(tmp.FullName, Path.Combine(tmp.FullName, "store", "node", "22.23.3"));

        using InstallLock? a = InstallLock.AcquireFile(lockA, TimeSpan.Zero, onWait: null);
        using InstallLock? b = InstallLock.AcquireFile(lockB, TimeSpan.Zero, onWait: null);

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public void The_same_entry_spelled_differently_shares_one_lock()
    {
        using var tmp = new TempDir();
        string entry = Path.Combine(tmp.FullName, "store", "node", "22.23.3");

        Assert.Equal(
            InstallLock.LockFilePath(tmp.FullName, entry),
            InstallLock.LockFilePath(tmp.FullName, entry + Path.DirectorySeparatorChar));
        if (OperatingSystem.IsWindows())
            Assert.Equal(InstallLock.LockFilePath(tmp.FullName, entry), InstallLock.LockFilePath(tmp.FullName, entry.ToUpperInvariant()));
    }
}
