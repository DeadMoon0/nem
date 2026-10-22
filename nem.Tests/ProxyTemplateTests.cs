using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The proxies nem ships ask 'nem resolve' in their own shell's syntax and start
/// the tool themselves; proxies an older nem wrote are brought up to date.
/// </summary>
public class ProxyTemplateTests
{
    static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "ProxyFiles");

    [Theory]
    [InlineData("NAME", "--shell sh")]
    [InlineData("NAME.bat", "--shell cmd")]
    [InlineData("NAME.ps1", "--shell ps")]
    public void Every_proxy_resolves_in_its_own_shell_syntax_and_never_keeps_nem_running(string template, string syntax)
    {
        string text = File.ReadAllText(Path.Combine(TemplatesDir, template));

        Assert.Contains("nem resolve", text);
        Assert.Contains(syntax, text);
        Assert.DoesNotContain("nem run", text);
    }

    [Fact]
    public void The_batch_proxy_keeps_delayed_expansion_off_so_a_bang_in_a_path_survives()
    {
        string bat = File.ReadAllText(Path.Combine(TemplatesDir, "NAME.bat"));

        Assert.Contains("setlocal disabledelayedexpansion", bat);
        Assert.DoesNotContain("enabledelayedexpansion", bat);
    }

    [Fact]
    public void A_proxy_an_older_nem_wrote_is_rewritten_from_the_current_template()
    {
        using var tmp = new TempDir();
        string stale = Path.Combine(tmp.FullName, "ng.bat");
        File.WriteAllText(stale, "@echo off\r\nnem run %~n0 -- %*\r\n");

        ProxyService.RefreshProxies(TemplatesDir, tmp.FullName);

        Assert.Equal(File.ReadAllText(Path.Combine(TemplatesDir, "NAME.bat")), File.ReadAllText(stale));
    }

    [Fact]
    public void Files_that_are_no_proxy_are_left_alone()
    {
        using var tmp = new TempDir();
        string notes = Path.Combine(tmp.FullName, "notes.txt");
        File.WriteAllText(notes, "mine");

        ProxyService.RefreshProxies(TemplatesDir, tmp.FullName);

        Assert.Equal("mine", File.ReadAllText(notes));
    }
}
