using Xunit;

namespace nem.Tests;

/// <summary>
/// Everything after '--' belongs to the tool and reaches it exactly as typed -
/// also arguments nem's own parser would reject.
/// </summary>
public class ToolArgumentsTests
{
    [Fact]
    public void A_scoped_registry_argument_reaches_the_tool_unchanged()
    {
        // What Claude Code passes to npm for a plugin in the @qbq-nmd scope.
        string[] args = ["run", "npm", "--", "view", "--json", "--@qbq-nmd:registry=https://feed.example/npm/", "--", "@qbq-nmd/plugin@1.0.0", "name"];

        CommandLine commandLine = CommandLine.Split(args);

        Assert.Equal(["run", "npm"], commandLine.NemArguments);
        Assert.Equal(["view", "--json", "--@qbq-nmd:registry=https://feed.example/npm/", "--", "@qbq-nmd/plugin@1.0.0", "name"], commandLine.ToolArguments.Args);
    }

    [Fact]
    public void Only_the_first_separator_is_nems()
    {
        CommandLine commandLine = CommandLine.Split(["run", "npm", "--", "exec", "--", "node", "-v"]);

        Assert.Equal(["exec", "--", "node", "-v"], commandLine.ToolArguments.Args);
    }

    [Fact]
    public void Without_a_separator_the_tool_gets_nothing_and_nem_parses_everything()
    {
        CommandLine commandLine = CommandLine.Split(["run", "npm", "--version"]);

        Assert.Equal(["run", "npm", "--version"], commandLine.NemArguments);
        Assert.Empty(commandLine.ToolArguments.Args);
    }

    [Fact]
    public void A_separator_with_nothing_after_it_gives_the_tool_no_arguments()
    {
        CommandLine commandLine = CommandLine.Split(["run", "npm", "--"]);

        Assert.Equal(["run", "npm"], commandLine.NemArguments);
        Assert.Empty(commandLine.ToolArguments.Args);
    }
}
