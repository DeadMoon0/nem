using Xunit;
using nem.Common.Models;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The config file format: comments are readable, and the explanatory header only
/// goes into the '.jsonc' file, never into a '.json' one that other tools parse
/// strictly.
/// </summary>
public class NemConfigFileTests
{
    static NemConfig SampleConfig() => new()
    {
        NodeVersion = "22.23.2",
        Tools = [new NemToolConfig { ToolName = "typescript", ToolVersion = "5.6.3" }],
    };

    [Fact]
    public void Parse_Skips_Block_And_Line_Comments()
    {
        string jsonc = """
            /* the node version this project needs */
            {
              // pinned by the release branch
              "NodeVersion": "22.23.2",
              "Tools": [
                { "ToolName": "typescript", "ToolVersion": "5.6.3" } // the compiler
              ]
            }
            """;

        NemConfig config = NemConfigFile.Parse(jsonc);

        Assert.Equal("22.23.2", config.NodeVersion);
        Assert.Single(config.Tools);
        Assert.Equal("typescript", config.Tools[0].ToolName);
    }

    [Fact]
    public void Parse_Treats_Empty_Content_As_An_Empty_Config()
    {
        foreach (string content in new[] { "", "   ", "\r\n" })
        {
            NemConfig config = NemConfigFile.Parse(content);
            Assert.Null(config.NodeVersion);
            Assert.Empty(config.Tools);
        }
    }

    [Fact]
    public void A_Serialized_Header_Does_Not_Stop_It_From_Being_Read_Back()
    {
        string text = NemConfigFile.Serialize(SampleConfig(), withHeader: true);

        Assert.StartsWith("/*", text);
        NemConfig parsed = NemConfigFile.Parse(text);
        Assert.Equal("22.23.2", parsed.NodeVersion);
        Assert.Equal("5.6.3", parsed.Tools[0].ToolVersion);
    }

    [Fact]
    public void Serializing_Without_The_Header_Yields_Plain_Json()
    {
        string text = NemConfigFile.Serialize(SampleConfig(), withHeader: false);

        Assert.DoesNotContain("/*", text);
        Assert.DoesNotContain("//", text);
        Assert.StartsWith("{", text);
    }

    [Fact]
    public void Only_The_Jsonc_Config_May_Hold_Comments()
    {
        Assert.True(NemConfigFile.AllowsComments(Path.Combine("p", "nem.jsonc")));
        Assert.False(NemConfigFile.AllowsComments(Path.Combine("p", "nem.json")));
    }

    [Fact]
    public void Writing_A_Jsonc_Config_Adds_The_Header()
    {
        using var tmp = new TempDir();
        string configPath = Path.Combine(tmp.FullName, "nem.jsonc");

        NemConfigFile.Write(configPath, SampleConfig());

        string text = File.ReadAllText(configPath);
        Assert.Contains(NemConfigFile.Header, text);
        Assert.Equal("22.23.2", NemConfigFile.Read(configPath).NodeVersion);
    }

    [Fact]
    public void Writing_A_Legacy_Json_Config_Leaves_It_Comment_Free()
    {
        using var tmp = new TempDir();
        string configPath = Path.Combine(tmp.FullName, "nem.json");

        NemConfigFile.Write(configPath, SampleConfig());

        string text = File.ReadAllText(configPath);
        Assert.DoesNotContain("/*", text);
        Assert.StartsWith("{", text);
        Assert.Equal("22.23.2", NemConfigFile.Read(configPath).NodeVersion);
    }

    [Fact]
    public void The_Header_Uses_This_Machines_Line_Endings()
    {
        Assert.Equal(NemConfigFile.Header.ReplaceLineEndings(), NemConfigFile.Header);
    }
}
