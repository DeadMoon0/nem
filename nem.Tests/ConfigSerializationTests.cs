using Xunit;
using Newtonsoft.Json;
using nem.Common.Models;

namespace nem.Tests;

/// <summary>
/// nem.json is hand-editable project state, so its JSON shape (PascalCase
/// keys, optional Tools) must stay stable.
/// </summary>
public class ConfigSerializationTests
{
    [Fact]
    public void RoundTrip_Preserves_NodeVersion_And_Tools()
    {
        var config = new NemConfig
        {
            NodeVersion = "22.23.2",
            Tools = [new NemToolConfig { ToolName = "typescript", ToolVersion = "5.6.3" }],
        };

        string json = JsonConvert.SerializeObject(config, Formatting.Indented);
        NemConfig parsed = JsonConvert.DeserializeObject<NemConfig>(json)!;

        Assert.Equal("22.23.2", parsed.NodeVersion);
        Assert.Single(parsed.Tools);
        Assert.Equal("typescript", parsed.Tools[0].ToolName);
        Assert.Equal("5.6.3", parsed.Tools[0].ToolVersion);
    }

    [Fact]
    public void Serialized_Json_Uses_Pascal_Case_Keys()
    {
        var config = new NemConfig
        {
            NodeVersion = "22.23.2",
            Tools = [new NemToolConfig { ToolName = "typescript", ToolVersion = "5.6.3" }],
        };

        string json = JsonConvert.SerializeObject(config);

        Assert.Contains("\"NodeVersion\":\"22.23.2\"", json);
        Assert.Contains("\"ToolName\":\"typescript\"", json);
        Assert.Contains("\"ToolVersion\":\"5.6.3\"", json);
    }

    [Fact]
    public void Empty_Object_Yields_Null_NodeVersion_And_Empty_Tools()
    {
        NemConfig parsed = JsonConvert.DeserializeObject<NemConfig>("{}")!;

        Assert.Null(parsed.NodeVersion);
        Assert.Empty(parsed.Tools);
    }

    [Fact]
    public void Tools_Defaults_To_A_Writable_List()
    {
        var config = new NemConfig();
        config.Tools.Add(new NemToolConfig { ToolName = "typescript", ToolVersion = "5.6.3" });
        Assert.Single(config.Tools);
    }
}
