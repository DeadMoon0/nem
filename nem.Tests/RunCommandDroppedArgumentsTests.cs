using Xunit;
using nem.Commands;

namespace nem.Tests;

/// <summary>
/// Only what follows '--' reaches the tool. Anything the parser took for nem's own
/// has to be named back to the user rather than dropped.
/// </summary>
public class RunCommandDroppedArgumentsTests
{
    static ILookup<string, string?> Parsed(params (string Key, string? Value)[] options) =>
        options.ToLookup(option => option.Key, option => option.Value);

    [Fact]
    public void Nothing_Parsed_Means_Nothing_Was_Lost()
    {
        Assert.Equal("", RunCommand.FormatDropped(Parsed()));
    }

    [Fact]
    public void A_Flag_Is_Written_The_Way_It_Was_Typed()
    {
        Assert.Equal("--version", RunCommand.FormatDropped(Parsed(("version", null))));
    }

    [Fact]
    public void An_Option_Keeps_Its_Value()
    {
        Assert.Equal("--port 4201", RunCommand.FormatDropped(Parsed(("port", "4201"))));
    }

    [Fact]
    public void Several_Options_Are_Listed_In_Order()
    {
        Assert.Equal(
            "--port 4201 --open",
            RunCommand.FormatDropped(Parsed(("port", "4201"), ("open", null))));
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void A_Name_That_Already_Carries_Its_Dashes_Does_Not_Get_More(string key)
    {
        Assert.Equal(key, RunCommand.FormatDropped(Parsed((key, null))));
    }

    [Fact]
    public void A_Repeated_Option_Is_Listed_Once_Per_Value()
    {
        Assert.Equal(
            "--define a --define b",
            RunCommand.FormatDropped(Parsed(("define", "a"), ("define", "b"))));
    }
}
