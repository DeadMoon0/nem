using nem.Common;
using nem.Common.Models;
using nem.Services;
using Newtonsoft.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class UpdateCommandSettings : CommandSettings
{
    [CommandArgument(0, "[nodeVersion]")]
    [Description("The new Node version, e.g. '22' or '18.12.0'.")]
    public string? NodeVersion { get; init; }

    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The folder path where the env is located.")]
    public required string Path { get; init; }
}

internal class UpdateCommand : AsyncCommand<UpdateCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, UpdateCommandSettings settings, CancellationToken cancellationToken)
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

        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(local.ConfigFilePath)) ?? new NemConfig();

        if (settings.NodeVersion == null)
        {
            // Report the current state.
            string? installed = NodeDownloadingService.GetInstalledNodeVersion(local.EnvDirPath);
            AnsiConsole.MarkupLine($"Node in {local.ConfigFileName}: [green]{Markup.Escape(config.NodeVersion ?? "(not set)")}[/]");
            AnsiConsole.MarkupLine("Node installed in the env: " + (installed != null ? $"[green]{Markup.Escape(installed)}[/]" : "[yellow]not installed[/]"));
            AnsiConsole.WriteLine("");
            AnsiConsole.MarkupLine("To change the Node version use: [green]nem update 18.12.0[/] (or a partial version like [green]nem update 18[/])");
            return 0;
        }

        string version;
        try
        {
            version = await NodeDownloadingService.ResolveNodeVersionAsync(settings.NodeVersion);
        }
        catch (InvalidOperationException e)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
            return 1;
        }

        string requested = NodeDownloadingService.NormalizeVersion(settings.NodeVersion);
        if (!requested.Equals(version, System.StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[gray]Resolved '{Markup.Escape(requested)}' to Node.js [green]{version}[/] (newest matching release).[/]");

        if (!string.Equals(config.NodeVersion, version, System.StringComparison.OrdinalIgnoreCase))
        {
            config.NodeVersion = version;
            File.WriteAllText(local.ConfigFilePath, JsonConvert.SerializeObject(config, Formatting.Indented) + Environment.NewLine);
            AnsiConsole.MarkupLine($"[gray]Updated {local.ConfigFileName} to Node version [green]{version}[/].[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[gray]The env already declares Node version [green]{version}[/].[/]");
        }

        string envDir = local.EnsureEnvDirPath();
        return await EnvironmentInstaller.InstallAsync(config, envDir, clean: false);
    }
}
