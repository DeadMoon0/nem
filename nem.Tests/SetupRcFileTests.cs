using Xunit;
using nem.Commands;

namespace nem.Tests;

/// <summary>
/// 'nem setup' on Unix must make the proxy directory permanent through the
/// shell rc files, without touching unrelated content and without re-adding
/// the block on later runs.
/// </summary>
public class SetupRcFileTests
{
    private const string ProxyDir = "/home/user/.config/nem/proxy";
    private const string Marker = "# nem: prepend the nem proxy directory to the PATH";
    private const string ExportLine = "export PATH=\"/home/user/.config/nem/proxy:$PATH\"";

    [Fact]
    public void Creates_A_Profile_With_The_Marker_And_Export_Line()
    {
        using var tmp = new TempDir();

        List<string> updated = SetupCommand.UpdateShellRcFiles(tmp.FullName, ProxyDir);

        Assert.Equal([Path.Combine(tmp.FullName, ".profile")], updated);
        Assert.Equal(Marker + "\n" + ExportLine + "\n", File.ReadAllText(Path.Combine(tmp.FullName, ".profile")));
    }

    [Fact]
    public void Appends_To_Existing_Rc_Files_Without_Losing_Their_Content()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, ".bashrc"), "# user's bashrc\n");

        List<string> updated = SetupCommand.UpdateShellRcFiles(tmp.FullName, ProxyDir);

        Assert.Equal(
            [Path.Combine(tmp.FullName, ".profile"), Path.Combine(tmp.FullName, ".bashrc")],
            updated);
        string bashrc = File.ReadAllText(Path.Combine(tmp.FullName, ".bashrc"));
        Assert.StartsWith("# user's bashrc\n", bashrc);
        Assert.Contains(Marker, bashrc);
        Assert.Contains(ExportLine, bashrc);
    }

    [Fact]
    public void A_Second_Run_Updates_Nothing()
    {
        using var tmp = new TempDir();
        string profile = Path.Combine(tmp.FullName, ".profile");

        SetupCommand.UpdateShellRcFiles(tmp.FullName, ProxyDir);
        string afterFirstRun = File.ReadAllText(profile);

        List<string> second = SetupCommand.UpdateShellRcFiles(tmp.FullName, ProxyDir);

        Assert.Empty(second);
        Assert.Equal(afterFirstRun, File.ReadAllText(profile));
    }

    [Fact]
    public void Files_That_Already_Carry_The_Marker_Are_Left_Alone()
    {
        using var tmp = new TempDir();
        string zshrc = Path.Combine(tmp.FullName, ".zshrc");
        string existing = "# custom\n" + Marker + "\nexport PATH=\"/custom:$PATH\"\n";
        File.WriteAllText(zshrc, existing);

        List<string> updated = SetupCommand.UpdateShellRcFiles(tmp.FullName, ProxyDir);

        Assert.Equal([Path.Combine(tmp.FullName, ".profile")], updated);
        Assert.Equal(existing, File.ReadAllText(zshrc));
    }

    [Fact]
    public void Missing_Optional_Rc_Files_Are_Ignored()
    {
        using var tmp = new TempDir();

        List<string> updated = SetupCommand.UpdateShellRcFiles(tmp.FullName, ProxyDir);

        Assert.Equal([Path.Combine(tmp.FullName, ".profile")], updated);
    }
}
