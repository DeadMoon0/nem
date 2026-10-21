using nem.Common;
using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace nem.Commands;

internal class AuditCommandSettings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [DefaultValue(".")]
    [Description("A folder inside the env. The nem config is looked up from there upwards.")]
    public required string Path { get; init; }
}

internal class AuditCommand : Command<AuditCommandSettings>
{
    protected override int Execute(CommandContext context, AuditCommandSettings settings, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(settings.Path);
        if (!EnvLocator.TryLocate(path, out IOPathManager.IOPathManagerEnv? local))
            return 1;

        if (!ToolService.TryCompose(NemConfigFile.Read(local.ConfigFilePath), out EnvComposition? env))
            return 1;
        if (!env.IsInstalled)
        {
            AnsiConsole.MarkupLine($"[yellow]Not installed yet: {Markup.Escape(string.Join(", ", env.Missing))}. Run [green]nem install[/] first.[/]");
            return 1;
        }

        AuditService.ReportDetails(env);
        return 0;
    }
}
