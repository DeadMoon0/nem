using Xunit;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// nem.jsonc creation and re-initialization.
/// </summary>
public class IoServiceTests
{
    [Fact]
    public void InitEnv_Creates_Only_The_Config()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, ".gitignore"), "node_modules\n");

        IOService.InitEnv(tmp.FullName, "22.23.2");

        string configPath = Path.Combine(tmp.FullName, "nem.jsonc");
        NemConfig config = NemConfigFile.Read(configPath);
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Equal(NemConfigVersion.Current, config.Version);
        Assert.Contains(NemConfigFile.Header, File.ReadAllText(configPath));

        // Node and the tools live in the shared store; the project gets nothing to ignore.
        Assert.Equal([".gitignore", "nem.jsonc"], Directory.EnumerateFileSystemEntries(tmp.FullName).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("node_modules\n", File.ReadAllText(Path.Combine(tmp.FullName, ".gitignore")));
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
        NemConfigFile.Write(Path.Combine(tmp.FullName, "nem.jsonc"), existing);

        IOService.InitEnv(tmp.FullName, "22.23.2");

        NemConfig config = NemConfigFile.Read(Path.Combine(tmp.FullName, "nem.jsonc"));
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Single(config.Tools);
        Assert.Equal("typescript", config.Tools[0].ToolName);
        Assert.Equal("5.6.3", config.Tools[0].ToolVersion);
    }

    [Fact]
    public void InitEnv_Keeps_A_Hand_Written_Comment_Readable()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.jsonc"),
            "// pinned for the 2026 release\n{ \"NodeVersion\": \"22.0.0\" }\n");

        IOService.InitEnv(tmp.FullName, "22.23.2");

        Assert.Equal("22.23.2", NemConfigFile.Read(Path.Combine(tmp.FullName, "nem.jsonc")).NodeVersion);
    }
}
