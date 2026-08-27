using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace nem.Commands;

internal class InitCommandSettings : CommandSettings
{
    [CommandArgument(0, "<nodeVersion>")]
    [Description("The version of Node the env is initialized for.")]
    public required string NodeVersion { get; init; }

    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The path of the folder the env is created in.")]
    public required string Path { get; init; }
}

internal class InitCommand : Command<InitCommandSettings>
{
    protected override int Execute(CommandContext context, InitCommandSettings settings, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(settings.Path);
        AnsiConsole.MarkupLine($"[gray]Init new Env in: {Markup.Escape(path)}[/]");
        AnsiConsole.MarkupLine($"[gray]Node Version: {Markup.Escape(settings.NodeVersion)}[/]");
        AnsiConsole.WriteLine("");
        IOService.InitEnv(path, settings.NodeVersion);
        AnsiConsole.WriteLine("");
        AnsiConsole.MarkupLine("[Green1]Success[/] [Gray50]The env was initialized.[/]");
        AnsiConsole.WriteLine("");
        AnsiConsole.MarkupLine("[Gray50]To install the Node version use:[/] nem install");
        AnsiConsole.MarkupLine("[Gray50]To manage tools use:[/] nem tool");
        return 0;
    }
}
