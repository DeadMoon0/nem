using nem.Common.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace nem.Services;

/// <summary>
/// The shared 'nem install' pipeline: install the Node version from nem.json,
/// install every declared tool that is missing, refresh the npm/npx proxies,
/// prune proxies that are stale for this env, and audit the result.
/// </summary>
public static class EnvironmentInstaller
{
    public static async Task<int> InstallAsync(NemConfig config, string envDir, bool clean)
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

        if (exit == 0)
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
