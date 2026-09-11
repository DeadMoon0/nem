using Xunit;
using nem.Commands;

namespace nem.Tests;

/// <summary>
/// Building the new machine PATH value. The entries around the proxy directory
/// must survive verbatim - writing them back expanded would permanently resolve
/// %SystemRoot% and friends in a system-wide value.
/// </summary>
public class SetupMachinePathTests
{
    const string Proxy = @"C:\Users\me\AppData\Roaming\nem\proxy";

    [Fact]
    public void The_Entry_Goes_First()
    {
        string result = SetupCommand.PrependPathEntry(@"C:\Windows;C:\Windows\system32", Proxy);

        Assert.Equal($@"{Proxy};C:\Windows;C:\Windows\system32", result);
    }

    [Fact]
    public void Unexpanded_Entries_Are_Passed_Through_Untouched()
    {
        string result = SetupCommand.PrependPathEntry(@"%SystemRoot%\system32;%ProgramFiles%\Git\cmd", Proxy);

        Assert.Equal($@"{Proxy};%SystemRoot%\system32;%ProgramFiles%\Git\cmd", result);
    }

    [Fact]
    public void Re_Running_Setup_Does_Not_Stack_Duplicates()
    {
        string once = SetupCommand.PrependPathEntry(@"C:\Windows", Proxy);
        string twice = SetupCommand.PrependPathEntry(once, Proxy);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void An_Existing_Entry_Is_Moved_To_The_Front_Whatever_Its_Case()
    {
        string result = SetupCommand.PrependPathEntry($@"C:\Windows;{Proxy.ToUpperInvariant()};C:\Git", Proxy);

        Assert.Equal($@"{Proxy};C:\Windows;C:\Git", result);
    }

    [Fact]
    public void An_Empty_Path_Yields_Just_The_Entry()
    {
        Assert.Equal(Proxy, SetupCommand.PrependPathEntry("", Proxy));
    }

    [Fact]
    public void Blank_Segments_Are_Dropped()
    {
        string result = SetupCommand.PrependPathEntry(@"C:\Windows;;  ;C:\Git", Proxy);

        Assert.Equal($@"{Proxy};C:\Windows;C:\Git", result);
    }
}
