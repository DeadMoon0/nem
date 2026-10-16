using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Pins how nem reads the registry's documents and where it sends requests, so a
/// change to the parsing cannot silently lose versions, engines or advisories.
/// </summary>
public class RegistryDocumentTests
{
    [Fact]
    public void Packument_Parse_Reads_Versions_Engines_And_DistTags()
    {
        const string json = """
            {
              "name": "tool",
              "dist-tags": { "latest": "2.0.0", "broken": 3 },
              "versions": {
                "1.0.0": { "engines": { "node": ">=18" } },
                "1.5.0": { "engines": [ "node >= 0.4" ] },
                "2.0.0": { }
              }
            }
            """;

        Packument packument = Assert.IsType<Packument>(Packument.Parse(json));

        Assert.Equal(">=18", packument.Versions["1.0.0"].EnginesNode);
        Assert.Null(packument.Versions["1.5.0"].EnginesNode);
        Assert.Null(packument.Versions["2.0.0"].EnginesNode);
        Assert.Equal("2.0.0", Assert.Single(packument.DistTags).Value);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Packument_Parse_Rejects_What_Is_Not_A_Document(string json)
    {
        Assert.Null(Packument.Parse(json));
    }

    [Fact]
    public void ParseAdvisories_Reads_The_Bulk_Answer_And_Fills_Missing_Fields()
    {
        const string json = """
            {
              "minimist": [
                { "severity": "critical", "title": "Prototype pollution", "url": "https://x/1", "vulnerable_versions": "<1.2.6" },
                { }
              ]
            }
            """;

        var advisories = NpmRegistryClient.ParseAdvisories(json)!["minimist"];

        Assert.Equal(new RegistryAdvisory("critical", "Prototype pollution", "https://x/1", "<1.2.6"), advisories[0]);
        Assert.Equal(new RegistryAdvisory("unknown", "Unknown advisory", "", ""), advisories[1]);
    }

    [Theory]
    [InlineData(null, "https://registry.npmjs.org/")]
    [InlineData("  ", "https://registry.npmjs.org/")]
    [InlineData("file:///C:/registry", "https://registry.npmjs.org/")]
    [InlineData("https://npm.example.com/repository/npm", "https://npm.example.com/repository/npm/")]
    [InlineData("https://npm.example.com//", "https://npm.example.com/")]
    public void The_Registry_Url_Is_Normalized_To_An_Http_Base_With_A_Trailing_Slash(string? configured, string expected)
    {
        Assert.Equal(expected, new NpmRegistryClient(configured).Registry.AbsoluteUri);
    }
}
