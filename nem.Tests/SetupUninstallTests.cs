using Xunit;
using nem.Commands;

namespace nem.Tests;

/// <summary>
/// 'nem setup --uninstall' must undo exactly what setup did and nothing else:
/// the PATH entry and the rc lines, leaving everything around them intact.
/// </summary>
public class SetupUninstallTests
{
    const string Proxy = @"C:\Users\me\AppData\Roaming\nem\proxy";

    [Fact]
    public void Removing_The_Entry_Leaves_The_Rest_In_Order()
    {
        string result = SetupCommand.RemovePathEntry($@"{Proxy};C:\Windows;C:\Git", Proxy);

        Assert.Equal(@"C:\Windows;C:\Git", result);
    }

    [Fact]
    public void Unexpanded_Entries_Survive_The_Removal()
    {
        string result = SetupCommand.RemovePathEntry($@"%SystemRoot%\system32;{Proxy};%ProgramFiles%\Git\cmd", Proxy);

        Assert.Equal(@"%SystemRoot%\system32;%ProgramFiles%\Git\cmd", result);
    }

    [Fact]
    public void Every_Copy_Of_The_Entry_Goes()
    {
        string result = SetupCommand.RemovePathEntry($@"{Proxy};C:\Windows;{Proxy.ToUpperInvariant()}", Proxy);

        Assert.Equal(@"C:\Windows", result);
    }

    [Fact]
    public void A_Trailing_Separator_Does_Not_Save_An_Entry()
    {
        string result = SetupCommand.RemovePathEntry($@"{Proxy}\;C:\Windows", Proxy);

        Assert.Equal(@"C:\Windows", result);
    }

    [Fact]
    public void Removing_What_Is_Not_There_Changes_Nothing()
    {
        Assert.Equal(@"C:\Windows;C:\Git", SetupCommand.RemovePathEntry(@"C:\Windows;C:\Git", Proxy));
    }

    [Fact]
    public void Setup_Then_Uninstall_Returns_The_Original_Path()
    {
        const string original = @"%SystemRoot%\system32;C:\Windows;C:\Program Files\Git\cmd";

        string afterSetup = SetupCommand.PrependPathEntry(original, Proxy);

        Assert.Equal(original, SetupCommand.RemovePathEntry(afterSetup, Proxy));
    }

    [Fact]
    public void The_Rc_Block_Is_Removed_With_Its_Export_Line()
    {
        string content = string.Join("\n",
            "export EDITOR=vim",
            "# nem: prepend the nem proxy directory to the PATH",
            "export PATH=\"/home/me/.config/nem/proxy:$PATH\"",
            "alias ll='ls -la'");

        string stripped = SetupCommand.StripRcBlock(content);

        Assert.Equal("export EDITOR=vim\nalias ll='ls -la'", stripped);
    }

    [Fact]
    public void An_Rc_File_Without_The_Marker_Is_Untouched()
    {
        const string content = "export EDITOR=vim\nalias ll='ls -la'\n";

        Assert.Equal(content, SetupCommand.StripRcBlock(content));
    }

    [Fact]
    public void Adding_Then_Removing_Leaves_The_Rc_File_As_It_Was()
    {
        using var tmp = new TempDir();
        string profile = Path.Combine(tmp.FullName, ".profile");
        const string original = "export EDITOR=vim\nalias ll='ls -la'";
        File.WriteAllText(profile, original);

        SetupCommand.UpdateShellRcFiles(tmp.FullName, "/home/me/.config/nem/proxy");
        Assert.Contains("nem", File.ReadAllText(profile));

        List<string> cleaned = SetupCommand.RemoveFromShellRcFiles(tmp.FullName);

        Assert.Contains(profile, cleaned);
        Assert.Equal(original, File.ReadAllText(profile).TrimEnd('\n'));
    }

    [Fact]
    public void Removing_From_An_Untouched_Home_Reports_Nothing()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, ".profile"), "export EDITOR=vim\n");

        Assert.Empty(SetupCommand.RemoveFromShellRcFiles(tmp.FullName));
    }
}
