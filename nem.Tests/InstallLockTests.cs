using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// One install per env at a time: a second one waits for the first, installs into
/// other envs do not.
/// </summary>
public class InstallLockTests
{
    [Fact]
    public void A_second_install_in_the_same_env_waits_for_the_first()
    {
        using var tmp = new TempDir();
        string lockFile = InstallLock.LockFilePath(tmp.FullName, Path.Combine(tmp.FullName, "repo", ".nenv"));
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
    public void Installs_in_different_envs_do_not_wait_for_each_other()
    {
        using var tmp = new TempDir();
        string lockA = InstallLock.LockFilePath(tmp.FullName, Path.Combine(tmp.FullName, "a", ".nenv"));
        string lockB = InstallLock.LockFilePath(tmp.FullName, Path.Combine(tmp.FullName, "b", ".nenv"));

        using InstallLock? a = InstallLock.AcquireFile(lockA, TimeSpan.Zero, onWait: null);
        using InstallLock? b = InstallLock.AcquireFile(lockB, TimeSpan.Zero, onWait: null);

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public void The_same_env_spelled_differently_shares_one_lock()
    {
        using var tmp = new TempDir();
        string env = Path.Combine(tmp.FullName, "repo", ".nenv");

        Assert.Equal(
            InstallLock.LockFilePath(tmp.FullName, env),
            InstallLock.LockFilePath(tmp.FullName, env + Path.DirectorySeparatorChar));
        if (OperatingSystem.IsWindows())
            Assert.Equal(InstallLock.LockFilePath(tmp.FullName, env), InstallLock.LockFilePath(tmp.FullName, env.ToUpperInvariant()));
    }
}
