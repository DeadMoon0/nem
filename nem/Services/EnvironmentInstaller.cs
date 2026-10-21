using nem.Common.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace nem.Services;

/// <summary>
/// The shared 'nem install' pipeline: install the Node version from the nem config,
/// install every declared tool that is missing, refresh the npm/npx proxies,
/// audit the result and record it in the <see cref="InstallStamp"/>. One install
/// per env at a time (<see cref="InstallLock"/>).
/// </summary>
public static class EnvironmentInstaller
{
    /// <summary>
    /// Set for everything an install starts. A package script that calls a proxied
    /// name would otherwise reach 'nem run', find the env out of date (the install
    /// is not finished) and start a second install that waits for this one's lock.
    /// </summary>
    public const string InstallingVariable = "NEM_INSTALLING";

    // A large tool set over a slow connection takes minutes; waiting longer than
    // this means the other install hangs.
    static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(15);

    public static async Task<int> InstallAsync(NemConfig config, string envDir, bool clean, bool audit)
    {
        using InstallLock? installLock = InstallLock.Acquire(envDir, LockTimeout,
            onWait: () => AnsiConsole.MarkupLine("[gray]Another install is running in this env, waiting for it to finish...[/]"));
        if (installLock == null)
        {
            AnsiConsole.MarkupLine($"[red]Gave up waiting for the other install in {Markup.Escape(envDir)} after {LockTimeout.TotalMinutes} minutes.[/]");
            return 1;
        }

        Environment.SetEnvironmentVariable(InstallingVariable, envDir);

        // The stamp says "the last install completed for this config". It goes first
        // and only comes back when this install succeeds, so a half-done env is
        // never taken for a finished one.
        string stampPath = Path.Combine(envDir, InstallStamp.FileName);
        if (File.Exists(stampPath))
            File.Delete(stampPath);

        int exit = await InstallUnlockedAsync(config, envDir, clean, audit);
        if (exit == 0)
            File.WriteAllText(stampPath, InstallStamp.For(config));
        return exit;
    }

    static async Task<int> InstallUnlockedAsync(NemConfig config, string envDir, bool clean, bool audit)
    {
        try
        {
            await NodeDownloadingService.InstallNodeAsync(config.NodeVersion ?? string.Empty, envDir, clean);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Installing the Node version failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        int exit = ToolService.InstallMissing(config, envDir);

        // npm/npx are part of every Node distribution, not declared tools; keep
        // their proxies up to date.
        ProxyService.TryInstallTool("npm");
        ProxyService.TryInstallTool("npx");

        // Proxies of tools this env does not have are deliberately left alone. The
        // proxy directory is machine-wide, so deleting "stale" ones here would
        // delete the proxies another project still needs, and that project's tool
        // would silently fall through to whatever else is on the PATH. A surplus
        // proxy costs nothing: inside an env without the tool it says so, and
        // outside any env it forwards to the system tool.

        // The proxies exist now, so this is the moment to say whether typing their
        // names will actually reach them from this terminal. The env's own tool
        // bins are the yardstick, so a proxy that went missing is reported as
        // such instead of going unnoticed.
        PathPrecedence.ReportProxyReachability(ExpectedCommands(config, envDir));

        if (exit == 0 && audit)
            AuditService.ReportSummary(envDir);

        return exit;
    }

    /// <summary>
    /// Every command name this env expects to own: the bins of each declared tool
    /// that is installed, plus npm and npx, which come with the Node distribution.
    /// </summary>
    internal static IReadOnlyList<string> ExpectedCommands(NemConfig config, string envDir)
    {
        var commands = new List<string> { "npm", "npx" };
        foreach (NemToolConfig tool in config.Tools)
        {
            if (ToolService.IsToolInstalled(envDir, tool.ToolName))
                commands.AddRange(ToolService.ReadToolBins(envDir, tool.ToolName));
        }

        return commands.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
