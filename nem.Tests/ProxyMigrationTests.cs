using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// After an update, the first nem call brings the proxies up to date - except the
/// one that is running right now, which the next call takes care of.
/// </summary>
public class ProxyMigrationTests
{
    static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "ProxyFiles");
    static string Template(string name) => File.ReadAllText(Path.Combine(TemplatesDir, name));
    const string OldProxy = "@echo off\r\nnem run %~n0 -- %*\r\n";

    static string ProxyDir(TempDir tmp, params string[] staleProxies)
    {
        string dir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "proxy")).FullName;
        foreach (string name in staleProxies)
            File.WriteAllText(Path.Combine(dir, name), OldProxy);
        File.WriteAllText(Path.Combine(dir, ProxyMigrator.MarkerFileName), "1.3.1");
        return dir;
    }

    static string Marker(string proxyDir) => File.ReadAllText(Path.Combine(proxyDir, ProxyMigrator.MarkerFileName));

    [Fact]
    public void The_first_call_after_an_update_brings_every_proxy_up_to_date()
    {
        using var tmp = new TempDir();
        string dir = ProxyDir(tmp, "npm.bat", "ng.bat");

        ProxyMigrator.Migrate(TemplatesDir, dir, "1.5.0", runningTool: null);

        Assert.Equal(Template("NAME.bat"), File.ReadAllText(Path.Combine(dir, "npm.bat")));
        Assert.Equal(Template("NAME.bat"), File.ReadAllText(Path.Combine(dir, "ng.bat")));
        Assert.Equal("1.5.0", Marker(dir));
    }

    [Fact]
    public void Without_an_update_the_proxies_are_not_even_looked_at()
    {
        using var tmp = new TempDir();
        string dir = ProxyDir(tmp, "npm.bat");
        File.WriteAllText(Path.Combine(dir, ProxyMigrator.MarkerFileName), "1.5.0");

        ProxyMigrator.Migrate(TemplatesDir, dir, "1.5.0", runningTool: null);

        Assert.Equal(OldProxy, File.ReadAllText(Path.Combine(dir, "npm.bat")));
    }

    [Fact]
    public void The_running_proxy_is_left_for_the_next_call()
    {
        using var tmp = new TempDir();
        string dir = ProxyDir(tmp, "npm.bat", "ng.bat");

        ProxyMigrator.Migrate(TemplatesDir, dir, "1.5.0", runningTool: "ng");

        Assert.Equal(OldProxy, File.ReadAllText(Path.Combine(dir, "ng.bat")));
        Assert.Equal(Template("NAME.bat"), File.ReadAllText(Path.Combine(dir, "npm.bat")));
        Assert.Equal("1.3.1", Marker(dir));

        ProxyMigrator.Migrate(TemplatesDir, dir, "1.5.0", runningTool: "npm");

        Assert.Equal(Template("NAME.bat"), File.ReadAllText(Path.Combine(dir, "ng.bat")));
        Assert.Equal("1.5.0", Marker(dir));
    }

    [Theory]
    [InlineData(new[] { "resolve", "ng", "--shell", "cmd" }, "ng")]
    [InlineData(new[] { "run", "npm" }, "npm")]
    [InlineData(new[] { "install" }, null)]
    [InlineData(new[] { "run" }, null)]
    public void The_running_proxy_is_the_tool_a_proxy_asked_for(string[] nemArguments, string? expected)
    {
        Assert.Equal(expected, ProxyMigrator.RunningProxyOf(nemArguments));
    }

    [Fact]
    public void Uninstalling_does_not_refresh_the_proxies_it_is_about_to_delete()
    {
        Assert.False(ProxyMigrator.AppliesTo(["setup", "--uninstall"]));
        Assert.True(ProxyMigrator.AppliesTo(["setup"]));
        Assert.True(ProxyMigrator.AppliesTo([]));
    }
}
