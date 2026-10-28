using nem.Common;
using nem.Common.Models;
using nem.Services.Diagnostics;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace nem.Services;

/// <summary>
/// The shared 'nem install' pipeline: put the env's Node version and every declared
/// tool into the <see cref="EnvStore"/> (whatever is missing there), refresh the
/// proxies, audit the result, and prune store entries nobody used for long.
/// </summary>
public static class EnvironmentInstaller
{
    /// <summary>
    /// Set for everything an install starts. A package script that calls a proxied
    /// name would otherwise reach 'nem run', find the env not installed (the install
    /// is not finished) and start a second install that waits for this one's lock.
    /// </summary>
    public const string InstallingVariable = "NEM_INSTALLING";

    /// <summary>Store entries no tool start used for this long are removed after an install.</summary>
    public static readonly TimeSpan PruneAfter = TimeSpan.FromDays(180);

    public static async Task<int> InstallAsync(NemConfig config, IOPathManager.IOPathManagerEnv env, bool reinstall, bool audit)
    {
        Environment.SetEnvironmentVariable(InstallingVariable, env.DirPath);
        EnvStore store = EnvStore.Default;

        EnvComposition? composition = await ComposeAsync(config, store);
        if (composition == null)
            return 1;

        try
        {
            await NodeDownloadingService.EnsureNodeEntryAsync(composition.NodeVersion, store, reinstall);
        }
        catch (Exception ex)
        {
            NemLog.Error(ex, "install.node");
            AnsiConsole.MarkupLine($"[red]Installing the Node version failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        NemLog.Info("install", new { env = env.DirPath, node = composition.NodeVersion, tools = composition.Tools.Select(t => $"{t.Name}@{t.Version}"), missing = composition.Missing, reinstall });
        int exit = ToolService.InstallTools(composition, store, reinstall);
        NemLog.Info("install.done", new { exitCode = exit });

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
        PathPrecedence.ReportProxyReachability(ExpectedCommands(composition));

        if (exit == 0 && audit)
            AuditService.ReportSummary(composition);

        DateTime now = DateTime.UtcNow;
        foreach (string entryDir in composition.EntryDirs.Where(Directory.Exists))
            EnvStore.Touch(entryDir, now);

        PruneStore(store, now, PruneAfter, quiet: true);
        return exit;
    }

    /// <summary>
    /// The env's composition, with a partial Node spec ("22") the store has no
    /// match for resolved against nodejs.org. Null (with the reason printed) when
    /// the config cannot be composed.
    /// </summary>
    static async Task<EnvComposition?> ComposeAsync(NemConfig config, EnvStore store)
    {
        EnvComposition.Result result = EnvComposition.Compose(config, store);
        if (result is EnvComposition.Result.Ok ok && NodeDownloadingService.IsPartialVersionSpec(ok.Env.NodeVersion))
        {
            string resolved;
            try
            {
                resolved = await NodeDownloadingService.ResolveNodeVersionAsync(ok.Env.NodeVersion);
            }
            catch (InvalidOperationException e)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
                return null;
            }
            result = EnvComposition.Compose(new NemConfig { NodeVersion = resolved, Tools = config.Tools }, store);
        }

        switch (result)
        {
            case EnvComposition.Result.Ok composed:
                return composed.Env;
            case EnvComposition.Result.Invalid invalid:
                AnsiConsole.MarkupLine($"[red]Cannot install the env: {Markup.Escape(invalid.Reason)}.[/]");
                return null;
            default:
                throw new InvalidOperationException("EnvComposition.Result has exactly two cases.");
        }
    }

    /// <summary>
    /// Removes store entries nobody used within <paramref name="maxAge"/>.
    /// <paramref name="quiet"/> only reports what was actually removed.
    /// </summary>
    public static void PruneStore(EnvStore store, DateTime now, TimeSpan maxAge, bool quiet)
    {
        EnvStore.PruneResult result = store.Prune(now, maxAge);
        if (result.Removed.Count > 0 || result.Busy.Count > 0)
            NemLog.Info("store.pruned", new { removed = result.Removed.Select(e => e.Name), busy = result.Busy.Select(e => e.Name) });
        foreach (EnvStore.Entry entry in result.Removed)
            AnsiConsole.MarkupLine($"[gray]Removed {entry.Kind} {Markup.Escape(entry.Name)} from the store (last used {entry.LastUsed.ToLocalTime():yyyy-MM-dd}).[/]");
        foreach (EnvStore.Entry entry in result.Busy)
            AnsiConsole.MarkupLine($"[yellow]Kept {entry.Kind} {Markup.Escape(entry.Name)}: it is in use right now.[/]");

        if (!quiet && result.Removed.Count == 0 && result.Busy.Count == 0)
            AnsiConsole.MarkupLine($"[green]Nothing to prune: every store entry was used within the last {maxAge.TotalDays:0} days.[/]");
    }

    /// <summary>
    /// Every command name this env expects to own: the bins of each declared tool
    /// that is in the store, plus npm and npx, which come with the Node distribution.
    /// </summary>
    internal static IReadOnlyList<string> ExpectedCommands(EnvComposition env)
    {
        var commands = new List<string> { "npm", "npx" };
        foreach (EnvComposition.ToolEntry tool in env.Tools.Where(t => EnvStore.IsComplete(t.Dir)))
            commands.AddRange(ToolService.ReadToolBins(tool.Dir, tool.Name));

        return commands.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
