using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Being on the PATH is not the same as winning: the shell takes the first entry
/// that carries the command name.
/// </summary>
public class PathPrecedenceTests
{
    const string ProxyDir = @"C:\proxy";

    static PathPrecedence.PathEntry Entry(string dir, params string[] commands) => new(dir, commands);

    [Fact]
    public void No_Shadow_When_The_Proxy_Dir_Comes_First()
    {
        var path = new[]
        {
            Entry(ProxyDir, "ng", "npm"),
            Entry(@"C:\npm", "ng", "npm"),
        };

        Assert.Empty(PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm"]));
    }

    [Fact]
    public void An_Earlier_Entry_Carrying_The_Same_Command_Shadows_It()
    {
        var path = new[]
        {
            Entry(@"C:\Windows\system32", "where", "cmd"),
            Entry(@"C:\npm", "ng"),
            Entry(ProxyDir, "ng", "npm"),
        };

        var shadows = PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm"]);

        PathPrecedence.Shadow shadow = Assert.Single(shadows);
        Assert.Equal("ng", shadow.ToolName);
        Assert.Equal(@"C:\npm", shadow.ShadowingDir);
    }

    [Fact]
    public void Entries_After_The_Proxy_Dir_Never_Shadow()
    {
        var path = new[]
        {
            Entry(ProxyDir, "ng", "npm", "npx"),
            Entry(@"C:\npm", "ng", "npm", "npx"),
        };

        Assert.Empty(PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm", "npx"]));
    }

    [Fact]
    public void Each_Tool_Is_Reported_Once_For_The_First_Entry_That_Takes_It()
    {
        var path = new[]
        {
            Entry(@"C:\first", "ng"),
            Entry(@"C:\second", "ng", "npm"),
            Entry(ProxyDir, "ng", "npm"),
        };

        var shadows = PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm"]);

        Assert.Equal(2, shadows.Count);
        Assert.Equal(@"C:\first", Assert.Single(shadows, s => s.ToolName == "ng").ShadowingDir);
        Assert.Equal(@"C:\second", Assert.Single(shadows, s => s.ToolName == "npm").ShadowingDir);
    }

    [Fact]
    public void Command_Names_Follow_The_Platform_Case_Rules()
    {
        var path = new[]
        {
            Entry(@"C:\npm", "NG"),
            Entry(ProxyDir, "ng"),
        };

        var shadows = PathPrecedence.FindShadows(path, ProxyDir, ["ng"]);

        // Windows resolves command names case-insensitively, Unix does not.
        if (OperatingSystem.IsWindows())
            Assert.Single(shadows);
        else
            Assert.Empty(shadows);
    }

    [Fact]
    public void The_Result_Does_Not_Depend_On_The_Callers_Collection_Type()
    {
        // A HashSet with its own comparer must not change the verdict.
        var asArray = new[] { Entry(@"C:\npm", "ng"), Entry(ProxyDir, "ng") };
        var asSet = new[]
        {
            new PathPrecedence.PathEntry(@"C:\npm", new HashSet<string>(["ng"], StringComparer.Ordinal)),
            new PathPrecedence.PathEntry(ProxyDir, new HashSet<string>(["ng"], StringComparer.Ordinal)),
        };

        Assert.Equal(
            PathPrecedence.FindShadows(asArray, ProxyDir, ["ng"]).Count,
            PathPrecedence.FindShadows(asSet, ProxyDir, ["ng"]).Count);
    }

    [Fact]
    public void A_Trailing_Separator_Still_Identifies_The_Proxy_Dir()
    {
        var path = new[]
        {
            Entry(ProxyDir + Path.DirectorySeparatorChar, "ng"),
            Entry(@"C:\npm", "ng"),
        };

        Assert.Empty(PathPrecedence.FindShadows(path, ProxyDir, ["ng"]));
    }

    [Fact]
    public void Unrelated_Commands_On_Earlier_Entries_Are_Ignored()
    {
        var path = new[]
        {
            Entry(@"C:\git\cmd", "git", "gitk"),
            Entry(ProxyDir, "ng"),
        };

        Assert.Empty(PathPrecedence.FindShadows(path, ProxyDir, ["ng"]));
    }

    [Fact]
    public void A_Tool_Whose_Proxy_Is_Missing_Is_Reported()
    {
        // The exact state a deleted proxy leaves behind: the name resolves
        // somewhere else entirely, and looking only at the proxy dir sees nothing.
        var path = new[]
        {
            Entry(ProxyDir, "npm", "npx"),
            Entry(@"C:\npm", "ng"),
        };

        var shadows = PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm", "npx"]);

        PathPrecedence.Shadow shadow = Assert.Single(shadows);
        Assert.Equal("ng", shadow.ToolName);
        Assert.Equal(PathPrecedence.Unreachable.NoProxy, shadow.Reason);
    }

    [Fact]
    public void A_Missing_Proxy_Is_Reported_Even_With_No_Other_Copy_On_The_Path()
    {
        var path = new[] { Entry(ProxyDir, "npm") };

        var shadows = PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm"]);

        Assert.Equal(PathPrecedence.Unreachable.NoProxy, Assert.Single(shadows).Reason);
    }

    [Fact]
    public void Shadowing_Wins_Over_Missing_When_An_Earlier_Entry_Answers()
    {
        // 'ng' never reaches the proxy dir, so the earlier entry is the thing to fix.
        var path = new[]
        {
            Entry(@"C:\npm", "ng"),
            Entry(ProxyDir, "npm"),
        };

        PathPrecedence.Shadow shadow = Assert.Single(PathPrecedence.FindShadows(path, ProxyDir, ["ng"]));

        Assert.Equal(PathPrecedence.Unreachable.Shadowed, shadow.Reason);
        Assert.Equal(@"C:\npm", shadow.ShadowingDir);
    }

    [Fact]
    public void Tools_The_Proxy_Dir_Answers_For_Are_Not_Reported()
    {
        var path = new[]
        {
            Entry(ProxyDir, "ng", "npm"),
            Entry(@"C:\npm", "ng", "npm"),
        };

        Assert.Empty(PathPrecedence.FindShadows(path, ProxyDir, ["ng", "npm"]));
    }

    [Fact]
    public void A_Terminal_Started_Before_The_Setup_Is_Detected()
    {
        string[] stored = [ProxyDir, @"C:\npm"];
        string[] process = [@"C:\npm"];

        Assert.True(PathPrecedence.IsStaleTerminal(stored, process, ProxyDir));
    }

    [Fact]
    public void A_Terminal_That_Already_Has_The_Proxy_Dir_Is_Not_Stale()
    {
        string[] both = [ProxyDir, @"C:\npm"];

        Assert.False(PathPrecedence.IsStaleTerminal(both, both, ProxyDir));
    }

    [Fact]
    public void Not_Being_Set_Up_At_All_Is_Not_A_Stale_Terminal()
    {
        // Nothing to have missed: that is 'run nem setup', not 'restart your terminal'.
        string[] stored = [@"C:\npm"];
        string[] process = [@"C:\npm"];

        Assert.False(PathPrecedence.IsStaleTerminal(stored, process, ProxyDir));
    }

    [Fact]
    public void Nothing_Is_Shadowed_When_The_Env_Proxies_No_Tools_Yet()
    {
        var path = new[]
        {
            Entry(@"C:\npm", "ng", "npm"),
            Entry(ProxyDir),
        };

        Assert.Empty(PathPrecedence.FindShadows(path, ProxyDir, []));
    }
}
