using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The store keeps its layout in step with nem: older stores are migrated step by
/// step, a store from a newer nem is refused instead of misread.
/// </summary>
public class StoreMigrationTests
{
    static EnvStore NewStore(TempDir tmp) => new(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));

    static string Marker(EnvStore store) => Path.Combine(store.StoreDir, StoreMigrator.MarkerFileName);
    static int Version(EnvStore store) => int.Parse(File.ReadAllText(Marker(store)).Trim());

    [Fact]
    public void A_new_store_gets_the_current_format()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);

        StoreMigrator.Migrate(store, [], currentVersion: 3);

        Assert.Equal(3, Version(store));
    }

    [Fact]
    public void A_store_from_before_the_marker_counts_as_format_1()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.23.3"));
        var ran = new List<int>();

        StoreMigrator.Migrate(store, [new StoreMigration(1, _ => ran.Add(1))], currentVersion: 2);

        Assert.Equal([1], ran);
        Assert.Equal(2, Version(store));
    }

    [Fact]
    public void A_store_from_a_newer_nem_is_refused_with_the_way_out()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        Directory.CreateDirectory(store.StoreDir);
        File.WriteAllText(Marker(store), "5");

        var refused = Assert.Throws<UnsupportedStoreVersionException>(() => StoreMigrator.Migrate(store, [], currentVersion: 1));

        Assert.Contains("dotnet tool update -g nem", refused.Message);
    }

    [Fact]
    public void Steps_run_in_order_from_the_stored_format()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        Directory.CreateDirectory(store.StoreDir);
        File.WriteAllText(Marker(store), "2");
        var ran = new List<int>();
        StoreMigration[] steps = [new(3, _ => ran.Add(3)), new(1, _ => ran.Add(1)), new(2, _ => ran.Add(2))];

        StoreMigrator.Migrate(store, steps, currentVersion: 4);

        Assert.Equal([2, 3], ran);
        Assert.Equal(4, Version(store));
    }

    [Fact]
    public void An_interrupted_migration_resumes_at_the_step_that_failed()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        Directory.CreateDirectory(store.StoreDir);
        File.WriteAllText(Marker(store), "1");
        var ran = new List<int>();
        bool fail = true;
        StoreMigration[] steps =
        [
            new(1, _ => ran.Add(1)),
            new(2, _ => { if (fail) throw new IOException("disk full"); ran.Add(2); }),
        ];

        Assert.Throws<IOException>(() => StoreMigrator.Migrate(store, steps, currentVersion: 3));
        Assert.Equal(2, Version(store));

        fail = false;
        StoreMigrator.Migrate(store, steps, currentVersion: 3);

        Assert.Equal([1, 2], ran);
        Assert.Equal(3, Version(store));
    }
}
