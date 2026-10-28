using nem.Common;
using nem.Services;
using nem.Services.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace nem.Commands;

internal class ReportCommandSettings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [DefaultValue(".")]
    [Description("The folder the problem happens in; its env is part of the report.")]
    public required string Path { get; init; }

    [CommandOption("-t|--tool <name>")]
    [Description("A tool to look at in detail, e.g. 'ng': what it runs and with which environment.")]
    public string? Tool { get; init; }

    [CommandOption("--days <days>")]
    [DefaultValue(LogWriter.KeepDays)]
    [Description("How many days of nem's log go into the report.")]
    public int Days { get; init; }

    [CommandOption("-o|--output <folder>")]
    [Description("Where the zip goes. Defaults to the current folder.")]
    public string? Output { get; init; }
}

/// <summary>
/// Puts everything needed to understand a problem on this machine into one zip to
/// send: a summary, nem's log and snapshots of the system, proxies, store and env.
/// It only looks, so the report shows the machine as it was.
/// </summary>
internal class ReportCommand : Command<ReportCommandSettings>
{
    protected override int Execute(CommandContext context, ReportCommandSettings settings, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        string cwd = System.IO.Path.GetFullPath(settings.Path);
        var inputs = new ReportInputs(
            cwd,
            IOPathManager.System.ProxyDirPath,
            ProxyService.ShippedTemplatesDir,
            // Not EnvStore.Default, which would migrate the store first.
            new EnvStore(IOPathManager.System.StoreDirPath, IOPathManager.System.GlobalsDirPath),
            NemLog.LogDir,
            settings.Days,
            settings.Tool,
            now);

        var files = DiagnosticReport.Collect(inputs);
        string zipPath = System.IO.Path.Combine(System.IO.Path.GetFullPath(settings.Output ?? "."), DiagnosticReport.ZipFileName(now));
        DiagnosticReport.WriteZip(files, zipPath);

        AnsiConsole.MarkupLine($"[green]Report written:[/] {Markup.Escape(zipPath)}");
        foreach (var file in files)
            AnsiConsole.MarkupLine($"[gray]  {Markup.Escape(file.Path)}[/]");
        AnsiConsole.MarkupLine("Secrets (npm auth tokens, passwords, credentials in URLs) are removed; the files are plain text if you want to check before sending.");
        return 0;
    }
}
