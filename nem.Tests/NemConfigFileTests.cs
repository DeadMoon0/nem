using Xunit;
using nem.Common.Models;
using nem.Services;
using System;

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

    // ---- schema version ----

    [Fact]
    public void A_Config_Without_A_Version_Reads_As_The_Initial_Schema()
    {
        // Every config written before versioning existed looks like this. It must not
        // pass as the current schema, or a future nem would skip its migration.
        NemConfig config = NemConfigFile.Parse("""{ "NodeVersion": "22.23.2" }""");

        Assert.Equal(NemConfigVersion.Initial, config.Version);
    }

    [Fact]
    public void A_Stated_Version_Is_Read_Back()
    {
        NemConfig config = NemConfigFile.Parse($$"""{ "Version": {{NemConfigVersion.Current}}, "NodeVersion": "22.23.2" }""");

        Assert.Equal(NemConfigVersion.Current, config.Version);
    }

    [Fact]
    public void A_Config_From_A_Newer_Nem_Is_Refused_Rather_Than_Half_Read()
    {
        int tooNew = NemConfigVersion.Current + 1;

        var error = Assert.Throws<UnsupportedConfigVersionException>(
            () => NemConfigFile.Parse($$"""{ "Version": {{tooNew}}, "NodeVersion": "22.23.2" }"""));

        Assert.Equal(tooNew, error.FileVersion);
        Assert.Equal(NemConfigVersion.Current, error.SupportedVersion);
        Assert.Contains(tooNew.ToString(), error.Message);
    }

    [Fact]
    public void Refusing_A_Newer_Config_Names_The_File_It_Came_From()
    {
        using var tmp = new TempDir();
        string configPath = Path.Combine(tmp.FullName, "nem.jsonc");
        File.WriteAllText(configPath, $$"""{ "Version": {{NemConfigVersion.Current + 1}} }""");

        var error = Assert.Throws<UnsupportedConfigVersionException>(() => NemConfigFile.Read(configPath));

        Assert.Equal(configPath, error.ConfigFilePath);
        Assert.Contains(configPath, error.Message);
    }

    [Fact]
    public void Writing_Stamps_The_Current_Schema_Onto_The_Config_And_The_File()
    {
        using var tmp = new TempDir();
        string configPath = Path.Combine(tmp.FullName, "nem.jsonc");
        var config = new NemConfig { Version = NemConfigVersion.Initial, NodeVersion = "22.23.2" };

        NemConfigFile.Write(configPath, config);

        // The object and the file agree afterwards, so nothing has to remember to stamp it.
        Assert.Equal(NemConfigVersion.Current, config.Version);
        Assert.Equal(NemConfigVersion.Current, NemConfigFile.Read(configPath).Version);
    }

    [Fact]
    public void Serializing_Writes_The_Version_The_Config_Carries()
    {
        // Serialize stays pure so a config from an older schema can be rendered as-is.
        string text = NemConfigFile.Serialize(
            new NemConfig { Version = NemConfigVersion.Initial, NodeVersion = "22.23.2" }, withHeader: false);

        Assert.Contains($"\"Version\": {NemConfigVersion.Initial}", text);
    }

    [Fact]
    public void The_Version_Leads_The_File_So_It_Is_Visible_At_A_Glance()
    {
        string text = NemConfigFile.Serialize(SampleConfig(), withHeader: false);

        Assert.True(text.IndexOf("\"Version\"", StringComparison.Ordinal)
                  < text.IndexOf("\"NodeVersion\"", StringComparison.Ordinal));
    }
}
