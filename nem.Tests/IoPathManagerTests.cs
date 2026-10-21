using Xunit;
using nem.Common;

namespace nem.Tests;

/// <summary>
/// Where nem keeps its state: project-local (nem.jsonc + .nenv) versus
/// machine-wide (download cache, extract cache, proxies).
/// </summary>
public class IoPathManagerTests
{
    [Fact]
    public void Local_Puts_The_Config_In_The_Project_Directory()
    {
        var local = IOPathManager.Local("/some/project");

        Assert.Equal("nem.jsonc", local.ConfigFileName);
        Assert.Equal(Path.Combine("/some/project", "nem.jsonc"), local.ConfigFilePath);
    }

    [Fact]
    public void A_New_Env_Gets_The_Jsonc_Config_But_Both_Names_Are_Accepted()
    {
        Assert.Equal(["nem.jsonc", "nem.json"], IOPathManager.ConfigFileNames);
        Assert.Equal("nem.jsonc or nem.json", IOPathManager.ConfigFileNamesText);
    }

    [Fact]
    public void TryGetEnv_Reports_The_Config_The_Project_Actually_Has()
    {
        foreach (string configFileName in IOPathManager.ConfigFileNames)
        {
            using var tmp = new TempDir();
            File.WriteAllText(Path.Combine(tmp.FullName, configFileName), "{}");

            Assert.True(IOPathManager.TryGetEnv(tmp.FullName, out IOPathManager.IOPathManagerEnv? env));
            Assert.Equal(configFileName, env!.ConfigFileName);
            Assert.Equal(Path.Combine(tmp.FullName, configFileName), env.ConfigFilePath);
        }
    }

    [Fact]
    public void TryGetEnv_Prefers_The_Jsonc_Config_When_Both_Are_There()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.json"), "{}");
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.jsonc"), "{}");

        Assert.True(IOPathManager.TryGetEnv(tmp.FullName, out IOPathManager.IOPathManagerEnv? env));
        Assert.Equal(Path.Combine(tmp.FullName, "nem.jsonc"), env!.ConfigFilePath);
    }

    [Fact]
    public void TryGetEnv_Looks_At_That_Folder_Only()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.jsonc"), "{}");
        string child = Path.Combine(tmp.FullName, "child");
        Directory.CreateDirectory(child);

        Assert.False(IOPathManager.TryGetEnv(child, out IOPathManager.IOPathManagerEnv? env));
        Assert.Null(env);
    }

    [Fact]
    public void System_Dir_Lives_Under_The_User_Config_Folder()
    {
        string configFolder = Path.Combine(Path.GetTempPath(), "home", ".config");
        var system = new IOPathManager.IOPathManagerSystem(configFolder);

        Assert.Equal(Path.Combine(configFolder, "nem"), system.DirPath);
        Assert.Equal(Path.Combine(configFolder, "nem", "store"), system.StoreDirPath);
        Assert.Equal(Path.Combine(configFolder, "nem", "globals"), system.GlobalsDirPath);
        Assert.Equal(Path.Combine(configFolder, "nem", "proxy"), system.ProxyDirPath);
    }

    [Fact]
    public void Nem_home_moves_the_whole_nem_folder()
    {
        string home = Path.Combine(Path.GetTempPath(), "nem-test-home");
        var system = IOPathManager.IOPathManagerSystem.AtNemDir(home);

        Assert.Equal(Path.Combine(home, "store"), system.StoreDirPath);
        Assert.Equal(Path.Combine(home, "proxy"), system.ProxyDirPath);
        Assert.Throws<InvalidOperationException>(() => IOPathManager.IOPathManagerSystem.AtNemDir("relative/nem"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nem")]
    public void A_Config_Folder_That_Is_Not_Absolute_Is_Refused(string configFolder)
    {
        // A relative proxy dir would end up as a relative PATH entry in the shell rc files.
        Assert.Throws<InvalidOperationException>(() => new IOPathManager.IOPathManagerSystem(configFolder));
    }

    [Fact]
    public void The_Machine_System_Dir_Is_Absolute()
    {
        Assert.True(Path.IsPathFullyQualified(IOPathManager.System.ProxyDirPath));
    }

    [Theory]
    [InlineData("", "nem.jsonc")]
    [InlineData("a", "nem.jsonc")]
    [InlineData("a/b", "nem.jsonc")]
    [InlineData("a/b/c/d/e", "nem.jsonc")]
    // An env created before the '.jsonc' config is found from just as deep.
    [InlineData("", "nem.json")]
    [InlineData("a/b/c/d/e", "nem.json")]
    public void TryFindEnv_Finds_The_Env_From_Any_Depth(string sub, string configFileName)
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, configFileName), "{}");
        string start = Path.Combine(tmp.FullName, sub.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(start);

        Assert.True(IOPathManager.TryFindEnv(start, out IOPathManager.IOPathManagerEnv? env));
        Assert.Equal(tmp.FullName, env!.DirPath);
        Assert.Equal(Path.Combine(tmp.FullName, configFileName), env.ConfigFilePath);
    }

    [Fact]
    public void TryFindEnv_Works_For_Folders_That_Do_Not_Exist_Yet()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.json"), "{}");

        Assert.True(IOPathManager.TryFindEnv(Path.Combine(tmp.FullName, "not", "created"), out IOPathManager.IOPathManagerEnv? env));
        Assert.Equal(tmp.FullName, env!.DirPath);
    }

    [Fact]
    public void TryFindEnv_Resolves_Relative_Paths_Against_The_Current_Directory()
    {
        string originalCwd = Directory.GetCurrentDirectory();
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.json"), "{}");
        string child = Path.Combine(tmp.FullName, "child");
        Directory.CreateDirectory(child);

        Directory.SetCurrentDirectory(child);
        try
        {
            Assert.True(IOPathManager.TryFindEnv(".", out IOPathManager.IOPathManagerEnv? env));
            Assert.Equal(tmp.FullName, env!.DirPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    [Fact]
    public void TryFindEnv_Lets_The_Closest_Config_Shadow_The_One_Above()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.json"), "{}");
        string inner = Path.Combine(tmp.FullName, "packages", "legacy");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "nem.json"), "{}");

        Assert.True(IOPathManager.TryFindEnv(Path.Combine(inner, "src"), out IOPathManager.IOPathManagerEnv? env));
        Assert.Equal(inner, env!.DirPath);
    }

    [Fact]
    public void TryFindEnv_Returns_False_Without_A_Config_Anywhere_Above()
    {
        using var tmp = new TempDir();

        Assert.False(IOPathManager.TryFindEnv(Path.Combine(tmp.FullName, "deep"), out IOPathManager.IOPathManagerEnv? env));
        Assert.Null(env);
    }

    [Fact]
    public void TryFindEnv_Stops_At_The_Filesystem_Root()
    {
        // Walking up from a drive/filesystem root must terminate, not loop.
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        IOPathManager.TryFindEnv(root, out _);
    }
}
