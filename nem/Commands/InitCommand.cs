using nem.Common;
using nem.Common.Models;
using nem.Services;
using Newtonsoft.Json;
using Spectre.Console;
using System;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class InitCommandSettings : CommandSettings
{
    [CommandArgument(0, "<nodeVersion>")]
    [Description("The Node version the env is initialized for. Partial versions (\"22\", \"18.12\") resolve to the newest matching release.")]
    public required string NodeVersion { get; init; }

    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The path of the folder the env is created in.")]
    public required string Path { get; init; }
}

internal class InitCommand : AsyncCommand<InitCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, InitCommandSettings settings, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(settings.Path);
        AnsiConsole.MarkupLine($"[gray]Init new Env in: {Markup.Escape(path)}[/]");

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
        AnsiConsole.MarkupLine($"[gray]Node Version: [green]{version}[/][/]");

        var local = IOPathManager.Local(path);
        if (File.Exists(local.ConfigFilePath))
        {
            NemConfig? existing = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(local.ConfigFilePath));
            if (existing is { NodeVersion: { } } && !NodeDownloadingService.VersionSpecMatches(existing.NodeVersion, version))
            {
                string shown = NodeDownloadingService.IsPartialVersionSpec(existing.NodeVersion)
                    ? await NodeDownloadingService.ResolveNodeVersionAsync(existing.NodeVersion)
                    : existing.NodeVersion;
                AnsiConsole.WriteLine("");
                AnsiConsole.MarkupLine($"[red]A {local.ConfigFileName} already exists in {Markup.Escape(path)} with Node version [green]{shown}[/].[/]");
                AnsiConsole.MarkupLine("[red]Use [green]nem update[/] to change the Node version of an existing env.[/]");
                return 1;
            }
        }

        AnsiConsole.WriteLine("");

        Directory.CreateDirectory(path);
        IOService.InitEnv(path, version);
        AnsiConsole.WriteLine("");
        AnsiConsole.MarkupLine("[Green1]Success[/] [Gray50]The env was initialized.[/]");
        AnsiConsole.WriteLine("");
        AnsiConsole.MarkupLine("[Gray50]To install the Node version use:[/] nem install");
        AnsiConsole.MarkupLine("[Gray50]To manage tools use:[/] nem tool");
        return 0;
    }
}
