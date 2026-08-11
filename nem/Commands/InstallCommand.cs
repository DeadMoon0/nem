using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.Threading;

namespace nem.Commands;

internal class InstallCommandSettings : CommandSettings
{
    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The folder path to where the env is in.")]
    public required string Path { get; init; }
}

internal class InstallCommand : Command<InstallCommandSettings>
{
    protected override int Execute(CommandContext context, InstallCommandSettings settings, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}