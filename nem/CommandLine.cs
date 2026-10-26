using System;
using System.Collections.Generic;

namespace nem;

/// <summary>
/// nem's command line split at the first '--': the part nem parses, and the tool's
/// arguments, which are handed on exactly as given. The parser never sees the
/// tool's part - it tokenizes past '--' and rejects arguments that are fine for the
/// tool, such as npm's per-scope '--@scope:registry=...'.
/// </summary>
internal sealed record CommandLine(string[] NemArguments, ToolArguments ToolArguments)
{
    public const string Separator = "--";

    public static CommandLine Split(string[] args)
    {
        int separator = Array.IndexOf(args, Separator);
        return separator < 0
            ? new CommandLine(args, new ToolArguments([]))
            : new CommandLine(args[..separator], new ToolArguments(args[(separator + 1)..]));
    }
}

/// <summary>The arguments after '--', for the tool 'nem run' starts.</summary>
internal sealed record ToolArguments(IReadOnlyList<string> Args);
