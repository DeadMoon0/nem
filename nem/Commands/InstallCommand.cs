using nem.Common;
using nem.Common.Models;
using nem.Services;
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
    [Description("A folder inside the env. The nem config is looked up from there upwards.")]
    public required string Path { get; init; }

    [CommandOption("-c|--clean")]
    [DefaultValue(false)]
    [Description("Reinstalls the Node version and the tools this env uses, even when the store already has them.")]
    public required bool Clean { get; init; }

    [CommandOption("--no-audit")]
    [DefaultValue(false)]
    [Description("Skips the security audit after installing (it asks the npm registry).")]
    public required bool NoAudit { get; init; }
}

internal class InstallCommand : AsyncCommand<InstallCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, InstallCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!IOService.EnsureSystemDir())
            return 1;

        string path = Path.GetFullPath(settings.Path);
        if (!EnvLocator.TryLocate(path, out IOPathManager.IOPathManagerEnv? local))
            return 1;

        NemConfig config = NemConfigFile.Read(local.ConfigFilePath);
        if (string.IsNullOrWhiteSpace(config.NodeVersion))
        {
            AnsiConsole.MarkupLine($"[red]{local.ConfigFileName} does not specify a 'NodeVersion'.[/]");
            return 1;
        }

        return await EnvironmentInstaller.InstallAsync(config, local, reinstall: settings.Clean, audit: !settings.NoAudit);
    }
}
