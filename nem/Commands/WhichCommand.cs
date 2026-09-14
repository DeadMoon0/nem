using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace nem.Commands;

internal class WhichCommandSettings : CommandSettings
{
    [CommandArgument(0, "<toolName>")]
    [Description("The tool to explain, e.g. 'ng'.")]
    public required string ToolName { get; init; }
}

/// <summary>
/// Answers "what does this name run, and why". Prints the chain
/// <see cref="ToolResolver"/> walked, so a surprising version can be traced to the
/// step that caused it instead of guessed at.
/// </summary>
internal class WhichCommand : Command<WhichCommandSettings>
{
    protected override int Execute(CommandContext context, WhichCommandSettings settings, CancellationToken cancellationToken)
    {
        ToolResolver.ToolResolution resolution = ToolResolver.Resolve(Directory.GetCurrentDirectory(), settings.ToolName);

        AnsiConsole.MarkupLine($"{Markup.Escape(resolution.ToolName)}  ->  {Headline(resolution)}");
        if (resolution.ExecutablePath != null)
            AnsiConsole.MarkupLine($"[gray]{Markup.Escape(resolution.ExecutablePath)}[/]");

        AnsiConsole.WriteLine();

        // A grid so a long path wraps inside its own column instead of pushing the
        // question text around.
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(1));
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn(new GridColumn());

        for (int i = 0; i < resolution.Chain.Count; i++)
        {
            ToolResolver.ResolutionStep step = resolution.Chain[i];
            grid.AddRow(
                step.Decided ? "[yellow]>[/]" : " ",
                $"{i + 1}  {Markup.Escape(step.Question)}",
                step.Decided ? $"[yellow]{Markup.Escape(step.Finding)}[/]" : $"[gray]{Markup.Escape(step.Finding)}[/]");
        }

        AnsiConsole.Write(grid);

        if (resolution.Source == ToolResolver.ToolSource.Local)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]nem starts the env copy. A project copy this near the folder is used by[/]");
            AnsiConsole.MarkupLine("[yellow]'npm run' scripts, and CLIs that hand over to a local install (the Angular[/]");
            AnsiConsole.MarkupLine("[yellow]CLI does) will run it instead - so the version you see may be the project's.[/]");
        }

        return resolution.Source == ToolResolver.ToolSource.Unavailable ? 1 : 0;
    }

    static string Headline(ToolResolver.ToolResolution resolution) => resolution.Source switch
    {
        ToolResolver.ToolSource.Global => "[green]global[/] (system tool, no env in play)",
        ToolResolver.ToolSource.Env => "[green]env[/]",
        ToolResolver.ToolSource.Local => $"[yellow]env, with a project copy {Markup.Escape(resolution.LocalVersion ?? "present")} nearer the folder[/]",
        _ => "[red]nothing - no env copy and no system copy[/]",
    };
}
