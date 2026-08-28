using Xunit;
using nem.Commands;

namespace nem.Tests;

/// <summary>
/// 'nem update <arg>' must decide between a Node version flow ("22") and a
/// tool package flow ("typescript") purely from the argument.
/// </summary>
public class NodeVersionSpecTests
{
    [Theory]
    [InlineData("22")]
    [InlineData("22.")]
    [InlineData("22.0")]
    [InlineData("22.0.")]
    [InlineData("22.0.2")]
    [InlineData("22.0.2.")]
    [InlineData("v22")]
    [InlineData("V22")]
    [InlineData("v22.23.2")]
    public void Node_Version_Specs_Are_Recognized(string spec)
        => Assert.True(NodeVersionSpec.IsNodeVersionSpec(spec));

    [Theory]
    [InlineData("typescript")]
    [InlineData("@angular/cli")]
    [InlineData("22.x")]
    [InlineData("1..2")]
    [InlineData(".22")]
    [InlineData("22..0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("22 22")]
    public void Anything_Else_Is_A_Tool_Package(string spec)
        => Assert.False(NodeVersionSpec.IsNodeVersionSpec(spec));

    [Fact]
    public void Null_And_Whitespace_Are_Not_Version_Specs()
    {
        Assert.False(NodeVersionSpec.IsNodeVersionSpec(null));
        Assert.False(NodeVersionSpec.IsNodeVersionSpec("  "));
    }
}
