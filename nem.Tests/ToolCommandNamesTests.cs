using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The commands a tool version ships, read from the registry before it is
/// installed - they are the proxies 'nem tool add' registers.
/// </summary>
public class ToolCommandNamesTests
{
    static Packument Parse(string name, string versionsJson) =>
        Assert.IsType<Packument>(Packument.Parse($$"""{ "name": "{{name}}", "versions": {{versionsJson}} }"""));

    [Fact]
    public void A_package_with_several_commands_gets_a_proxy_for_each()
    {
        Packument packument = Parse("typescript", """{ "5.4.5": { "bin": { "tsc": "bin/tsc", "tsserver": "bin/tsserver" } } }""");

        Assert.Equal(["tsc", "tsserver"], packument.Versions["5.4.5"].Bins);
    }

    [Fact]
    public void A_single_command_is_named_after_the_package_without_its_scope()
    {
        Packument packument = Parse("@scope/tool", """{ "1.0.0": { "bin": "cli.js" } }""");

        Assert.Equal(["tool"], packument.Versions["1.0.0"].Bins);
    }

    [Fact]
    public void A_library_without_commands_gets_no_proxy()
    {
        Packument packument = Parse("lodash", """{ "4.17.21": { } }""");

        Assert.Empty(packument.Versions["4.17.21"].Bins);
    }

    [Fact]
    public void Each_version_keeps_its_own_commands()
    {
        Packument packument = Parse("tool", """
            {
              "1.0.0": { "bin": { "tool": "a.js" } },
              "2.0.0": { "bin": { "tool": "a.js", "tool-server": "b.js" } }
            }
            """);

        Assert.Equal(["tool"], packument.Versions["1.0.0"].Bins);
        Assert.Equal(["tool", "tool-server"], packument.Versions["2.0.0"].Bins);
    }
}
