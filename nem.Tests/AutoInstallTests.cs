using Xunit;
using nem.Common;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// When a tool start has to install the env first: whenever the env was not
/// installed for exactly the Node version and tools nem.jsonc declares now.
/// </summary>
public class AutoInstallTests
{
    static NemConfig Config(string node, params string[] tools) => new()
    {
        NodeVersion = node,
        Tools = tools.Select(t => t.Split('=')).Select(t => new NemToolConfig { ToolName = t[0], ToolVersion = t[1] }).ToList(),
    };

    [Fact]
    public void An_env_installed_for_the_declared_config_needs_no_install()
    {
        var config = Config("22.23.3", "@angular/cli=22.2.3");

        Assert.True(InstallStamp.Matches(InstallStamp.For(config), config));
    }

    [Fact]
    public void An_env_that_was_never_installed_needs_an_install()
    {
        Assert.False(InstallStamp.Matches(null, Config("22.23.3")));
    }

    [Fact]
    public void A_changed_node_version_needs_an_install()
    {
        string installed = InstallStamp.For(Config("20.20.2", "@angular/cli=17.3.8"));

        Assert.False(InstallStamp.Matches(installed, Config("22.23.3", "@angular/cli=17.3.8")));
    }

    [Fact]
    public void A_changed_tool_version_needs_an_install()
    {
        string installed = InstallStamp.For(Config("22.23.3", "@angular/cli=21.0.0"));

        Assert.False(InstallStamp.Matches(installed, Config("22.23.3", "@angular/cli=22.2.3")));
    }

    [Fact]
    public void An_added_tool_needs_an_install()
    {
        string installed = InstallStamp.For(Config("22.23.3", "@angular/cli=22.2.3"));

        Assert.False(InstallStamp.Matches(installed, Config("22.23.3", "@angular/cli=22.2.3", "typescript=5.9.3")));
    }

    [Fact]
    public void A_removed_tool_needs_an_install()
    {
        string installed = InstallStamp.For(Config("22.23.3", "@angular/cli=22.2.3", "typescript=5.9.3"));

        Assert.False(InstallStamp.Matches(installed, Config("22.23.3", "@angular/cli=22.2.3")));
    }

    [Fact]
    public void Reordering_the_tools_in_the_config_needs_no_install()
    {
        string installed = InstallStamp.For(Config("22.23.3", "@angular/cli=22.2.3", "typescript=5.9.3"));

        Assert.True(InstallStamp.Matches(installed, Config("22.23.3", "typescript=5.9.3", "@angular/cli=22.2.3")));
    }

    [Fact]
    public void A_stamp_with_windows_line_endings_still_counts()
    {
        var config = Config("22.23.3", "typescript=5.9.3");

        Assert.True(InstallStamp.Matches(InstallStamp.For(config).Replace("\n", "\r\n"), config));
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
    public void Inspect_reads_the_env_on_disk()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.jsonc"), """{ "NodeVersion": "22.23.3", "Tools": [] }""");
        Assert.True(IOPathManager.TryGetEnv(tmp.FullName, out IOPathManager.IOPathManagerEnv? env));

        Assert.Equal(AutoInstaller.EnvState.Outdated, AutoInstaller.Inspect(env!));

        Directory.CreateDirectory(env!.EnvDirPath);
        File.WriteAllText(Path.Combine(env.EnvDirPath, InstallStamp.FileName), InstallStamp.For(Config("22.23.3")));
        Assert.Equal(AutoInstaller.EnvState.Current, AutoInstaller.Inspect(env));
    }

    [Fact]
    public void Inspect_reports_a_config_it_cannot_read()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.jsonc"), "{ not json");
        Assert.True(IOPathManager.TryGetEnv(tmp.FullName, out IOPathManager.IOPathManagerEnv? env));

        Assert.Equal(AutoInstaller.EnvState.Unreadable, AutoInstaller.Inspect(env!));
    }
}
