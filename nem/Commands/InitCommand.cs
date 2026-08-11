using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace nem.Commands;

internal class InitCommandSettings : CommandSettings
{
    [CommandArgument(0, "<nodeVersion>")]
    [Description("The Version of Node, you want to Init the Env for.")]
    public required string NodeVersion { get; init; }

    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The Path to the Folder you want to setup the Env in.")]
    public required string Path { get; init; }
}

internal class InitCommand : Command<InitCommandSettings>
{
    protected override int Execute(CommandContext context, InitCommandSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[gray]Init new Env in: " + Path.GetFullPath(settings.Path) + "[/]");
        IOService.InitEnv(Path.GetFullPath(settings.Path), settings.NodeVersion);
        return 0;
    }
}