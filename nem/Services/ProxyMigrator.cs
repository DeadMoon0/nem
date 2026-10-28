using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using nem.Common;
using nem.Services.Diagnostics;

namespace nem.Services;

/// <summary>
/// Brings the proxies up to date on the first nem call after nem changed version.
/// The proxy directory remembers which nem last did that (<see cref="MarkerFileName"/>);
/// as long as it is this one, a call costs one small file read. Otherwise a proxy an
/// older nem wrote could keep starting tools in a way this nem no longer expects.
/// </summary>
public static class ProxyMigrator
{
    public const string MarkerFileName = ".nem-version";

    /// <summary>This nem's version, including the commit it was built from.</summary>
    public static string CurrentVersion { get; } =
        typeof(ProxyMigrator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Runs before every command. Best effort: whatever goes wrong here, the
    /// command itself still runs, and a later call tries again.
    /// </summary>
    public static void Run(IReadOnlyList<string> nemArguments)
    {
        if (!AppliesTo(nemArguments))
            return;
        try
        {
            Migrate(ProxyService.ShippedTemplatesDir, IOPathManager.System.ProxyDirPath, CurrentVersion, RunningProxyOf(nemArguments));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A missing or read-only nem folder; the command reports what it needs.
        }
    }

    /// <summary>
    /// Every command but 'nem setup --uninstall', which is about to delete the
    /// proxies, and 'nem report', which has to show them as they were.
    /// </summary>
    internal static bool AppliesTo(IReadOnlyList<string> nemArguments) =>
        nemArguments.Count == 0
        || !(nemArguments[0] == "report" || (nemArguments[0] == "setup" && nemArguments.Contains("--uninstall")));

    /// <summary>
    /// The tool whose proxy started this nem: 'nem resolve' and 'nem run' are what
    /// the proxies call, with the tool name right behind the command.
    /// </summary>
    internal static string? RunningProxyOf(IReadOnlyList<string> nemArguments) =>
        nemArguments.Count >= 2 && nemArguments[0] is "resolve" or "run" ? nemArguments[1] : null;

    internal static void Migrate(string proxyFilesDir, string proxyDir, string version, string? runningTool)
    {
        if (!Directory.Exists(proxyDir))
            return;

        string marker = Path.Combine(proxyDir, MarkerFileName);
        string? markedVersion = File.Exists(marker) ? File.ReadAllText(marker) : null;
        if (markedVersion == version)
            return;

        // Another nem is refreshing right now; it does the same work.
        using InstallLock? refreshLock = InstallLock.AcquireFile(marker + ".lock", TimeSpan.Zero, onWait: null);
        if (refreshLock == null)
            return;

        // The marker only moves on once nothing is left over - the running tool's
        // proxy included, which the next call for another tool refreshes.
        bool complete = ProxyService.RefreshProxies(proxyFilesDir, proxyDir, runningTool);
        if (complete)
            File.WriteAllText(marker, version);
        NemLog.Info("proxies.refreshed", new { from = markedVersion, to = version, complete, skipped = complete ? null : runningTool });
    }
}
