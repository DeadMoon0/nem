using System;
using System.Linq;
using nem.Common.Models;

namespace nem.Services;

/// <summary>
/// The record of what the last completed 'nem install' put into an env: its Node
/// version and tools, as one line each. Comparing it with the config is how a
/// tool start tells, without starting Node, whether the env still matches
/// nem.jsonc - after a pull or a branch switch it often does not.
/// </summary>
public static class InstallStamp
{
    /// <summary>The stamp's file name inside the env folder (.nenv).</summary>
    public const string FileName = ".nem-installed";

    /// <summary>
    /// The stamp text for a config. Tools are sorted, so reordering them in
    /// nem.jsonc does not count as a change; their versions and the Node version
    /// are compared as written.
    /// </summary>
    public static string For(NemConfig config)
    {
        var lines = config.Tools
            .Select(tool => $"tool {tool.ToolName}@{tool.ToolVersion}")
            .Order(StringComparer.Ordinal)
            .Prepend($"node {config.NodeVersion}");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// True when <paramref name="stamp"/> (the file's text, null when there is
    /// none) records exactly what <paramref name="config"/> declares.
    /// </summary>
    public static bool Matches(string? stamp, NemConfig config) =>
        stamp != null && string.Equals(stamp.ReplaceLineEndings("\n"), For(config), StringComparison.Ordinal);
}
