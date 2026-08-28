using nem.Common;
using nem.Common.Models;
using nem.Services;
using Newtonsoft.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class InstallCommandSettings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [DefaultValue(".")]
    [Description("The folder path where the env is located.")]
    public required string Path { get; init; }

    [CommandOption("-c|--clean")]
    [DefaultValue(false)]
    [Description("If set, removes the cached download and the env folder before installing.")]
    public required bool Clean { get; init; }
}

internal class InstallCommand : AsyncCommand<InstallCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, InstallCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!IOService.EnsureSystemDir())
            return 1;

        string path = Path.GetFullPath(settings.Path);
        var local = IOPathManager.Local(path);

        if (!File.Exists(local.ConfigFilePath))
        {
            AnsiConsole.MarkupLine($"[red]No {local.ConfigFileName} found in {Markup.Escape(path)}. Run [green]nem init[/] first.[/]");
            return 1;
        }

        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(local.ConfigFilePath))!;
        if (string.IsNullOrWhiteSpace(config.NodeVersion))
        {
            AnsiConsole.MarkupLine($"[red]{local.ConfigFileName} does not specify a 'NodeVersion'.[/]");
            return 1;
        }

        string envDir = local.EnsureEnvDirPath();
        await NodeDownloadingService.InstallNodeAsync(config.NodeVersion, envDir, settings.Clean);

        int exit = ToolService.InstallMissing(config, envDir);

        ProxyService.TryInstallTool("npm");
        ProxyService.TryInstallTool("npx");

        if (exit == 0)
            AuditService.AuditAndReport(envDir);

        return exit;
    }
}
