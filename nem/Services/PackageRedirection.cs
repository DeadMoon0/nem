using System;
using System.IO;
using System.Linq;
using nem.Common;
using nem.Services.Diagnostics;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// Windows redirects what a packaged (MSIX) app - and every process it starts -
/// writes under %APPDATA% into the app's own folder
/// (%LOCALAPPDATA%\Packages\&lt;app&gt;\LocalCache\Roaming). Started from such an app
/// (the Claude desktop app is one), nem's store, logs and proxies end up there,
/// invisible to every other terminal. Those processes carry no package identity,
/// so the only way to tell is to write a probe and see where it lands.
/// </summary>
/// <param name="Package">The app's package folder name, e.g. "Claude_pzs8sxrjxfjjc".</param>
/// <param name="RedirectedNemDir">Where nem's folder really ends up.</param>
public sealed record PackageRedirection(string Package, string RedirectedNemDir)
{
    /// <summary>
    /// The redirection this nem runs under, or null when its writes reach the real
    /// folder. Writes and deletes one small file; meant for commands that write
    /// anyway, not for the proxies.
    /// </summary>
    public static PackageRedirection? Detect()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return Detect(
                IOPathManager.System.DirPath,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"),
                $".nem-probe-{Guid.NewGuid():N}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static PackageRedirection? Detect(string nemDir, string roamingDir, string packagesDir, string probeName)
    {
        // Only %APPDATA% is redirected; a NEM_HOME elsewhere never is.
        string relative = Path.GetRelativePath(roamingDir, nemDir);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative) || !Directory.Exists(packagesDir))
            return null;

        var candidates = Directory.EnumerateDirectories(packagesDir)
            .Select(package => (Package: Path.GetFileName(package), Dir: Path.Combine(package, "LocalCache", "Roaming", relative)))
            .Where(candidate => Directory.Exists(candidate.Dir))
            .ToList();
        if (candidates.Count == 0)
            return null;

        Directory.CreateDirectory(nemDir);
        string probe = Path.Combine(nemDir, probeName);
        File.WriteAllText(probe, "");
        try
        {
            return candidates
                .Where(candidate => File.Exists(Path.Combine(candidate.Dir, probeName)))
                .Select(candidate => new PackageRedirection(candidate.Package, candidate.Dir))
                .FirstOrDefault();
        }
        finally
        {
            File.Delete(probe);
        }
    }

    /// <summary>
    /// Warns when <paramref name="command"/>'s results - store entries, proxies -
    /// only land in a packaged app's copy, and logs it.
    /// </summary>
    public static void WarnIfRedirected(string command)
    {
        if (Detect() is not { } redirection)
            return;
        NemLog.Info("package.redirected", new { command, redirection.Package, redirection.RedirectedNemDir });
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(redirection.Warning)}[/]");
    }

    /// <summary>The warning for commands whose results other terminals should see.</summary>
    public string Warning =>
        $"nem runs inside the packaged app '{Package}' (the Claude desktop app is one). Windows redirects what it writes "
        + $"under %APPDATA% into {RedirectedNemDir}, so the store, logs and proxies written now are only visible inside "
        + "that app. Run this command from a normal terminal to make them visible everywhere.";
}
