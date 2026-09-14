using Xunit;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// nem.jsonc / .nenv creation, including what happens to an env that still carries
/// the older nem.json.
/// </summary>
public class IoServiceTests
{
    [Fact]
    public void InitEnv_Creates_Config_And_Env_Dir()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, ".gitignore"), "node_modules\n");

        IOService.InitEnv(tmp.FullName, "22.23.2");

        string configPath = Path.Combine(tmp.FullName, "nem.jsonc");
        NemConfig config = NemConfigFile.Read(configPath);
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Contains(NemConfigFile.Header, File.ReadAllText(configPath));
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
        NemConfigFile.Write(Path.Combine(tmp.FullName, "nem.jsonc"), existing);

        IOService.InitEnv(tmp.FullName, "22.23.2");

        NemConfig config = NemConfigFile.Read(Path.Combine(tmp.FullName, "nem.jsonc"));
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Single(config.Tools);
        Assert.Equal("typescript", config.Tools[0].ToolName);
        Assert.Equal("5.6.3", config.Tools[0].ToolVersion);
    }

    /// <summary>
    /// An env created before the '.jsonc' config keeps its file: re-initializing it
    /// must update that one and not drop a second config next to it, which would
    /// shadow the first from then on.
    /// </summary>
    [Fact]
    public void InitEnv_Updates_A_Legacy_Json_Config_In_Place()
    {
        using var tmp = new TempDir();
        string legacyPath = Path.Combine(tmp.FullName, "nem.json");
        NemConfigFile.Write(legacyPath, new NemConfig
        {
            NodeVersion = "22.0.0",
            Tools = [new NemToolConfig { ToolName = "typescript", ToolVersion = "5.6.3" }],
        });

        IOService.InitEnv(tmp.FullName, "22.23.2");

        Assert.False(File.Exists(Path.Combine(tmp.FullName, "nem.jsonc")));
        string text = File.ReadAllText(legacyPath);
        Assert.DoesNotContain("/*", text);

        NemConfig config = NemConfigFile.Parse(text);
        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Single(config.Tools);
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

        NemConfig config = NemConfigFile.Read(Path.Combine(tmp.FullName, "nem.jsonc"));
        Assert.Equal("22.23.2", config.NodeVersion);
    }

}
