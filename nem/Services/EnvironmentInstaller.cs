using nem.Common.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
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

        // Drop proxies for tools that are no longer installed in this env.
        ProxyService.PruneStaleProxies(InstalledToolBins(envDir));

        if (exit == 0)
            AuditService.AuditAndReport(envDir);

        return exit;
    }

    /// <summary>
    /// The bin names of every package installed in the env's global modules root
    /// (the declared tools and anything else present, scoped packages included).
    /// </summary>
    static IEnumerable<string> InstalledToolBins(string envDir)
    {
        string modulesRoot = ToolService.ToolModulesRoot(envDir);
        if (!Directory.Exists(modulesRoot))
            yield break;

        foreach (string packageDir in Directory.EnumerateDirectories(modulesRoot))
        {
            string name = Path.GetFileName(packageDir);
            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                foreach (string scopedDir in Directory.EnumerateDirectories(packageDir))
                {
                    if (File.Exists(Path.Combine(scopedDir, "package.json")))
                    {
                        foreach (string bin in ToolService.ReadToolBins(envDir, name + "/" + Path.GetFileName(scopedDir)))
                            yield return bin;
                    }
                }
            }
            else if (File.Exists(Path.Combine(packageDir, "package.json")))
            {
                foreach (string bin in ToolService.ReadToolBins(envDir, name))
                    yield return bin;
            }
        }
    }
}
