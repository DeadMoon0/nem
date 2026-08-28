using Xunit;
using nem.Common;

namespace nem.Tests;

/// <summary>
/// Where nem keeps its state: project-local (nem.json + .nenv) versus
/// machine-wide (download cache, extract cache, proxies).
/// </summary>
public class IoPathManagerTests
{
    [Fact]
    public void Local_Puts_Config_And_Env_In_The_Project_Directory()
    {
        var local = IOPathManager.Local("/some/project");

        Assert.Equal("nem.json", local.ConfigFileName);
        Assert.Equal(Path.Combine("/some/project", "nem.json"), local.ConfigFilePath);
        Assert.Equal(".nenv", local.EnvDirName);
        Assert.Equal(Path.Combine("/some/project", ".nenv"), local.EnvDirPath);
    }

    [Fact]
    public void System_Dir_Lives_Under_The_User_Config_Folder()
    {
        string configFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Equal(Path.Combine(configFolder, "nem"), IOPathManager.System.DirPath);
        Assert.Equal(Path.Combine(configFolder, "nem", "download"), IOPathManager.System.DownloadCacheDirPath);
        Assert.Equal(Path.Combine(configFolder, "nem", "extract"), IOPathManager.System.ExtractCacheDirPath);
        Assert.Equal(Path.Combine(configFolder, "nem", "proxy"), IOPathManager.System.ProxyDirPath);
    }

    [Fact]
    public void EnsureEnvDirPath_Creates_The_Env_Directory()
    {
        using var tmp = new TempDir();
        var local = IOPathManager.Local(tmp.FullName);

        Assert.False(Directory.Exists(local.EnvDirPath));

        Assert.Equal(local.EnvDirPath, local.EnsureEnvDirPath());
        Assert.True(Directory.Exists(local.EnvDirPath));

        // Calling it again resolves to the same path.
        Assert.Equal(local.EnvDirPath, local.EnsureEnvDirPath());
    }
}
