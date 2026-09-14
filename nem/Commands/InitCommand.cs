using nem.Common;
using nem.Common.Models;
using nem.Services;
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
        // An env already here keeps its own config file name, so 'nem init' on a
        // pre-'.jsonc' project reads and rewrites that file instead of adding a second one.
        if (IOPathManager.TryGetEnv(path, out IOPathManager.IOPathManagerEnv? existingEnv))
        {
            NemConfig existing = NemConfigFile.Read(existingEnv.ConfigFilePath);
            if (existing.NodeVersion is { } declared && !NodeDownloadingService.VersionSpecMatches(declared, version))
            {
                string shown = NodeDownloadingService.IsPartialVersionSpec(declared)
                    ? await NodeDownloadingService.ResolveNodeVersionAsync(declared)
                    : declared;
                AnsiConsole.WriteLine("");
                AnsiConsole.MarkupLine($"[red]A {existingEnv.ConfigFileName} already exists in {Markup.Escape(path)} with Node version [green]{shown}[/].[/]");
                AnsiConsole.MarkupLine("[red]Use [green]nem update[/] to change the Node version of an existing env.[/]");
                return 1;
            }
        }
        else
        {
            WarnAboutOuterEnv(path, local);
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

    /// <summary>
    /// A config shadows every env above it, so a second one below an existing env
    /// silently takes over its whole subtree. That is legitimate (a monorepo may
    /// want a different Node version per package) but almost never intended, so it
    /// is called out instead of happening quietly. Only reached when this folder
    /// holds no env yet, because re-initializing one shadows nothing new.
    /// </summary>
    static void WarnAboutOuterEnv(string path, IOPathManager.IOPathManagerLocal local)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent == null || !IOPathManager.TryFindEnv(parent, out IOPathManager.IOPathManagerEnv? outer))
            return;

        AnsiConsole.WriteLine("");
        AnsiConsole.MarkupLine($"[yellow]Note: {Markup.Escape(outer.DirPath)} already holds an env.[/]");
        AnsiConsole.MarkupLine($"[yellow]A second {Markup.Escape(local.ConfigFileName)} here shadows it, so its tools stop resolving below {Markup.Escape(path)}.[/]");
        AnsiConsole.MarkupLine($"[yellow]To use the existing env instead, run [green]nem install[/] from anywhere inside it.[/]");
    }
}
