using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace nem.Commands;

internal class RunCommandSettings : CommandSettings
{
    [CommandArgument(0, "<toolName>")]
    [Description("The tool to run. Pass its arguments after a '--' separator.")]
    public required string ToolName { get; init; }
}

internal class RunCommand : Command<RunCommandSettings>
{
    protected override int Execute(CommandContext context, RunCommandSettings settings, CancellationToken cancellationToken)
    {
        // Only what follows '--' reaches the tool, and Parsed holds every option the
        // parser saw - those after the separator as well. So the giveaway is options
        // parsed while nothing at all followed the separator: 'nem run npm
        // --version' would run a bare npm and print its usage, as if that is what
        // was asked for. Losing an argument in silence is the worst of the options,
        // so say which one and how to pass it.
        if (context.Remaining.Raw.Count == 0)
        {
            string dropped = FormatDropped(context.Remaining.Parsed);
            if (dropped.Length > 0)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(dropped)} would not reach '{Markup.Escape(settings.ToolName)}'.[/]");
                AnsiConsole.MarkupLine($"The tool's arguments go after a '--': [green]nem run {Markup.Escape(settings.ToolName)} -- {Markup.Escape(dropped)}[/]");
                return 1;
            }
        }

        // Deliberately not gated on 'nem setup': running a tool needs only the env
        // (or the system fallback), not the proxies. This is the escape hatch the
        // PATH warnings point to, so it has to work when the PATH is not right.
        return ProxyService.CallToolInEnvContext(settings.ToolName, context.Remaining.Raw);
    }

    /// <summary>
    /// The options the parser took for nem's own, written the way they were typed,
    /// or an empty string when there were none. The parser keeps the leading dashes
    /// on the name it reports, so they are only added when it did not.
    /// </summary>
    internal static string FormatDropped(ILookup<string, string?> parsed)
    {
        IEnumerable<string> options = parsed.SelectMany(option => option.Select(value =>
        {
            string name = option.Key.StartsWith('-') ? option.Key : $"--{option.Key}";
            return value == null ? name : $"{name} {value}";
        }));

        return string.Join(" ", options);
    }
}
