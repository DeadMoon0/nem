using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The store keeps what tool starts use and drops what nobody used for long -
/// without a list of envs.
/// </summary>
public class EnvStoreTests
{
    static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    static readonly TimeSpan HalfAYear = TimeSpan.FromDays(180);

    static EnvStore NewStore(TempDir tmp) => new(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));

    static string Entry(EnvStore store, string version, DateTime lastUsed)
    {
        string dir = store.NodeEntryDir(version);
        StoreFixture.CompleteEntry(dir);
        EnvStore.Touch(dir, lastUsed, force: true);
        return dir;
    }

    [Fact]
    public void An_entry_used_within_the_age_limit_is_kept()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        string recent = Entry(store, "22.23.3", Now - TimeSpan.FromDays(179));

        var result = store.Prune(Now, HalfAYear);

        Assert.Empty(result.Removed);
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public void An_entry_nobody_used_for_longer_than_the_limit_is_removed()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        string old = Entry(store, "18.20.8", Now - TimeSpan.FromDays(181));

        var result = store.Prune(Now, HalfAYear);

        Assert.Equal("18.20.8", Assert.Single(result.Removed).Name);
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void An_entry_an_install_is_working_on_is_kept()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        string old = Entry(store, "18.20.8", Now - TimeSpan.FromDays(400));

        using (store.Lock(old, TimeSpan.Zero))
        {
            var result = store.Prune(Now, HalfAYear);
            Assert.Single(result.Busy);
        }

        Assert.True(Directory.Exists(old));
    }

    [Fact]
    public void An_install_that_never_finished_is_removed_once_old()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        string broken = Directory.CreateDirectory(store.ToolEntryDir("typescript", "5.9.3", 22)).FullName;
        Directory.SetLastWriteTimeUtc(broken, Now - TimeSpan.FromDays(200));

        var result = store.Prune(Now, HalfAYear);

        Assert.False(Assert.Single(result.Removed).Complete);
    }

    [Fact]
    public void A_tool_start_refreshes_the_last_use_at_most_once_a_day()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        string entry = Entry(store, "22.23.3", Now);

        EnvStore.Touch(entry, Now + TimeSpan.FromHours(23));
        Assert.Equal(Now, EnvStore.LastUsed(entry));

        EnvStore.Touch(entry, Now + TimeSpan.FromHours(25));
        Assert.Equal(Now + TimeSpan.FromHours(25), EnvStore.LastUsed(entry));
    }

    [Fact]
    public void A_scoped_tool_gets_one_folder()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);

        string entry = store.ToolEntryDir("@angular/cli", "22.2.3", 22);

        Assert.Equal(Path.Combine(store.StoreDir, "tools"), Path.GetDirectoryName(entry));
    }
}
