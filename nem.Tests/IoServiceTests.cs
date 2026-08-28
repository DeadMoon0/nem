using Xunit;
using Newtonsoft.Json;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// nem.json / .nenv creation and the walk-up that finds the project env.
/// </summary>
public class IoServiceTests
{
    [Fact]
    public void InitEnv_Creates_Config_And_Env_Dir()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, ".gitignore"), "node_modules\n");

        IOService.InitEnv(tmp.FullName, "22.23.2");

        string configPath = Path.Combine(tmp.FullName, "nem.json");
        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(configPath))!;
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.True(Directory.Exists(Path.Combine(tmp.FullName, ".nenv")));

        string gitignore = File.ReadAllText(Path.Combine(tmp.FullName, ".gitignore"));
        Assert.StartsWith("node_modules\n", gitignore);
        Assert.Contains("#nem", gitignore);
        Assert.Contains("/.nenv", gitignore);
    }

    [Fact]
    public void InitEnv_Keeps_Declared_Tools_When_Updating_The_Version()
    {
        using var tmp = new TempDir();
        var existing = new NemConfig
        {
            NodeVersion = "22.0.0",
            Tools = [new NemToolConfig { ToolName = "typescript", ToolVersion = "5.6.3" }],
        };
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.json"), JsonConvert.SerializeObject(existing));

        IOService.InitEnv(tmp.FullName, "22.23.2");

        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(Path.Combine(tmp.FullName, "nem.json")))!;
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Single(config.Tools);
        Assert.Equal("typescript", config.Tools[0].ToolName);
        Assert.Equal("5.6.3", config.Tools[0].ToolVersion);
    }

    [Fact]
    public void InitEnv_Does_Not_Duplicate_The_GitIgnore_Entry()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, ".gitignore"), "");

        IOService.InitEnv(tmp.FullName, "22.23.2");
        IOService.InitEnv(tmp.FullName, "22.23.2");

        string gitignore = File.ReadAllText(Path.Combine(tmp.FullName, ".gitignore"));
        Assert.Equal(1, gitignore.Split("/.nenv", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void InitEnv_Survives_A_Missing_GitIgnore()
    {
        using var tmp = new TempDir();

        IOService.InitEnv(tmp.FullName, "22.23.2");

        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(
            File.ReadAllText(Path.Combine(tmp.FullName, "nem.json")))!;
        Assert.Equal("22.23.2", config.NodeVersion);
    }

    [Fact]
    public void TryGetContainingEnv_Finds_The_Closest_Config()
    {
        using var tmp = new TempDir();
        string nested = Path.Combine(tmp.FullName, "a", "b");
        Directory.CreateDirectory(nested);
        string rootConfig = Path.Combine(tmp.FullName, "nem.json");
        File.WriteAllText(rootConfig, "{}");

        bool found = IOService.TryGetContainingEnv(nested, out string? foundPath);

        Assert.True(found);
        Assert.Equal(rootConfig, foundPath);
    }

    [Fact]
    public void TryGetContainingEnv_Returns_False_Without_A_Config_Above()
    {
        using var tmp = new TempDir();

        Assert.False(IOService.TryGetContainingEnv(Path.Combine(tmp.FullName, "deep"), out _));
    }
}
