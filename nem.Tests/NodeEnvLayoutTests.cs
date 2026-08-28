using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The platform layout of a .nenv directory (binary names, npm entry point,
/// module root, PATH construction) is the single source of truth for all
/// services, so it must be stable for both operating systems.
/// </summary>
public class NodeEnvLayoutTests
{
    [Fact]
    public void Windows_Layout_Keeps_The_Env_Root_As_Bin_Dir()
    {
        var layout = NodeEnvLayout.Create(@"C:\proj\.nenv", isWindows: true);

        Assert.True(layout.IsWindows);
        Assert.Equal(@"C:\proj\.nenv", layout.EnvDir);
        Assert.Equal(@"C:\proj\.nenv", layout.BinDir);
        Assert.Equal(Path.Combine(@"C:\proj\.nenv", "node.exe"), layout.NodeBinary);
        Assert.Equal(Path.Combine(@"C:\proj\.nenv", "npm.cmd"), layout.NpmEntry);
        Assert.Equal(Path.Combine(@"C:\proj\.nenv", "node_modules"), layout.ModulesRoot);
    }

    [Fact]
    public void Unix_Layout_Uses_The_Node_Prefix_Directories()
    {
        var layout = NodeEnvLayout.Create("/home/user/.nenv", isWindows: false);

        Assert.False(layout.IsWindows);
        Assert.Equal("/home/user/.nenv", layout.EnvDir);
        Assert.Equal(Path.Combine("/home/user/.nenv", "bin"), layout.BinDir);
        Assert.Equal(Path.Combine("/home/user/.nenv", "bin", "node"), layout.NodeBinary);
        Assert.Equal(Path.Combine("/home/user/.nenv", "bin", "npm"), layout.NpmEntry);
        Assert.Equal(Path.Combine("/home/user/.nenv", "lib", "node_modules"), layout.ModulesRoot);
    }

    [Fact]
    public void BuildPathVariable_Puts_The_Env_Dirs_In_Front()
    {
        var layout = NodeEnvLayout.Create("/some/env");
        string existing = "keep1" + Path.PathSeparator + "keep2";

        string result = layout.BuildPathVariable(existing);

        string envDirs = string.Join(
            Path.PathSeparator,
            new[] { layout.BinDir, layout.EnvDir }.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(envDirs + Path.PathSeparator + existing, result);
    }

    [Fact]
    public void BuildPathVariable_Without_A_Existing_Path_Returns_Only_The_Env_Dirs()
    {
        var layout = NodeEnvLayout.Create("/some/env");
        string envDirs = string.Join(
            Path.PathSeparator,
            new[] { layout.BinDir, layout.EnvDir }.Distinct(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(envDirs, layout.BuildPathVariable(null));
        Assert.Equal(envDirs, layout.BuildPathVariable(""));
    }

    [Fact]
    public void BuildPathVariable_Never_Lists_The_Same_Directory_Twice()
    {
        // On Windows the bin dir and the env root are the same directory.
        var windows = NodeEnvLayout.Create(@"C:\proj\.nenv", isWindows: true);
        Assert.Equal(@"C:\proj\.nenv", windows.BuildPathVariable(null));

        // On Unix both the bin dir and the env root are prepended, in order.
        var unix = NodeEnvLayout.Create("/home/user/.nenv", isWindows: false);
        Assert.Equal(
            Path.Combine("/home/user/.nenv", "bin") + Path.PathSeparator + "/home/user/.nenv",
            unix.BuildPathVariable(null));
    }
}
