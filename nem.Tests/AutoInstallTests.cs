using Xunit;
using nem.Common;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// When a tool start has to install first: whenever the store lacks the Node
/// version or a tool version the env's nem.jsonc declares. The project itself
/// carries nothing but that file.
/// </summary>
public class AutoInstallTests
{
    static NemConfig Config(string node, params string[] tools) => new()
    {
        NodeVersion = node,
        Tools = tools.Select(t => t.Split('=')).Select(t => new NemToolConfig { ToolName = t[0], ToolVersion = t[1] }).ToList(),
    };

    static EnvStore NewStore(TempDir tmp) => new(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));

    static EnvComposition Compose(NemConfig config, EnvStore store) =>
        Assert.IsType<EnvComposition.Result.Ok>(EnvComposition.Compose(config, store)).Env;

    [Fact]
    public void An_env_whose_node_and_tools_are_in_the_store_needs_no_install()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.23.3"));
        StoreFixture.CompleteEntry(store.ToolEntryDir("@angular/cli", "22.2.3", 22));

        EnvComposition env = Compose(Config("22.23.3", "@angular/cli=22.2.3"), store);

        Assert.True(env.IsInstalled);
        Assert.Empty(env.Missing);
    }

    [Fact]
    public void A_node_version_the_store_lacks_needs_an_install()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        StoreFixture.CompleteEntry(store.NodeEntryDir("20.20.2"));

        EnvComposition env = Compose(Config("22.23.3"), store);

        Assert.Equal(["Node 22.23.3"], env.Missing);
    }

    [Fact]
    public void A_tool_version_the_store_lacks_needs_an_install()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.23.3"));
        StoreFixture.CompleteEntry(store.ToolEntryDir("@angular/cli", "21.0.0", 22));

        EnvComposition env = Compose(Config("22.23.3", "@angular/cli=22.2.3"), store);

        Assert.Equal(["@angular/cli@22.2.3"], env.Missing);
    }

    [Fact]
    public void An_install_that_never_finished_counts_as_missing()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.23.3"));
        Directory.CreateDirectory(store.ToolEntryDir("typescript", "5.9.3", 22));

        EnvComposition env = Compose(Config("22.23.3", "typescript=5.9.3"), store);

        Assert.Equal(["typescript@5.9.3"], env.Missing);
    }

    [Fact]
    public void Envs_on_the_same_node_major_share_a_tool()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);

        string onOlderPatch = Assert.Single(Compose(Config("20.18.0", "@angular/cli=17.3.8"), store).Tools).Dir;
        string onNewerPatch = Assert.Single(Compose(Config("20.20.2", "@angular/cli=17.3.8"), store).Tools).Dir;

        Assert.Equal(onOlderPatch, onNewerPatch);
    }

    [Fact]
    public void A_tool_is_kept_apart_per_node_major_because_native_addons_are_built_for_one()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);

        string onNode20 = Assert.Single(Compose(Config("20.20.2", "esbuild=0.25.0"), store).Tools).Dir;
        string onNode22 = Assert.Single(Compose(Config("22.23.3", "esbuild=0.25.0"), store).Tools).Dir;

        Assert.NotEqual(onNode20, onNode22);
    }

    [Fact]
    public void A_partial_node_version_takes_the_newest_matching_one_in_the_store()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.9.0"));
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.23.3"));
        StoreFixture.CompleteEntry(store.NodeEntryDir("24.1.0"));

        EnvComposition env = Compose(Config("22"), store);

        Assert.Equal("22.23.3", env.NodeVersion);
        Assert.True(env.IsInstalled);
    }

    [Fact]
    public void A_partial_node_version_the_store_has_nothing_for_needs_an_install()
    {
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);

        EnvComposition env = Compose(Config("22"), store);

        Assert.Equal(["Node 22"], env.Missing);
    }

    [Fact]
    public void A_tool_declared_as_a_range_is_refused()
    {
        using var tmp = new TempDir();

        var result = EnvComposition.Compose(Config("22.23.3", "typescript=^5.9.0"), NewStore(tmp));

        Assert.Contains("exact version", Assert.IsType<EnvComposition.Result.Invalid>(result).Reason);
    }

    [Fact]
    public void A_config_without_a_node_version_is_refused()
    {
        using var tmp = new TempDir();

        Assert.IsType<EnvComposition.Result.Invalid>(EnvComposition.Compose(new NemConfig(), NewStore(tmp)));
    }

    [Fact]
    public void Declared_tools_come_before_node_on_the_path()
    {
        // A declared npm or corepack has to win over the one Node ships.
        using var tmp = new TempDir();
        EnvStore store = NewStore(tmp);

        EnvComposition env = Compose(Config("22.23.3", "npm=11.0.0"), store);

        Assert.StartsWith(Assert.Single(env.Tools).Dir, env.BinDirs[0]);
        Assert.StartsWith(env.NodeDir, env.BinDirs[^1]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("no")]
    [InlineData(" off ")]
    public void Auto_install_can_be_turned_off(string value)
    {
        Assert.True(AutoInstaller.IsOptedOut(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("true")]
    public void Auto_install_is_on_unless_turned_off(string? value)
    {
        Assert.False(AutoInstaller.IsOptedOut(value));
    }

    [Fact]
    public void Inspect_reports_a_config_it_cannot_read()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.jsonc"), "{ not json");
        Assert.True(IOPathManager.TryGetEnv(tmp.FullName, out IOPathManager.IOPathManagerEnv? env));

        Assert.IsType<AutoInstaller.EnvState.Invalid>(AutoInstaller.Inspect(env!, NewStore(tmp)));
    }
}
