using System;
using System.Text.RegularExpressions;

namespace nem.Commands;

/// <summary>
/// Classifies <c>nem update</c> arguments: a bare Node version spec ("22",
/// "22.0", "22.23.2", with optional leading "v" and trailing ".") selects the
/// Node version flow; anything else is a tool package spec.
/// </summary>
internal static class NodeVersionSpec
{
    private static readonly Regex NodeVersionPattern =
        new(@"^[vV]?\d+([.]\d+){0,2}\.?$", RegexOptions.Compiled);

    public static bool IsNodeVersionSpec(string? input)
    {
        return NodeVersionPattern.IsMatch(input ?? "");
    }
}
