using nem.Common;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nem.Services;

/// <summary>
/// Whether nem's proxies actually win when a tool name is typed. Having the proxy
/// directory somewhere *on* the PATH is not enough: the shell takes the first
/// match, so any earlier entry carrying the same command name answers instead and
/// nem never runs - the env is bypassed without a single error message.
/// </summary>
public static class PathPrecedence
{
    /// <summary>Why typing a tool's name does not reach its nem proxy.</summary>
    public enum Unreachable
    {
        /// <summary>An earlier PATH entry carries the same command name and answers first.</summary>
        Shadowed,
        /// <summary>The proxy directory has no proxy for this tool at all.</summary>
        NoProxy,
    }

    /// <summary>A tool of the env that a typed command name does not reach.</summary>
    public sealed record Shadow(string ToolName, string ShadowingDir)
    {
        public Unreachable Reason { get; init; } = Unreachable.Shadowed;
    }

    /// <summary>One PATH entry and the command names it provides (file names without extension).</summary>
    public sealed record PathEntry(string Dir, IReadOnlyCollection<string> Commands);

    /// <summary>
    /// The proxied tools answered by an entry that comes before
    /// <paramref name="proxyDir"/>. Entries are consumed in resolution order and
    /// only until the proxy directory is reached, so everything after it is
    /// irrelevant by construction. A tool is reported once, for the first entry
    /// that shadows it.
    /// </summary>
    public static IReadOnlyList<Shadow> FindShadows(
        IEnumerable<PathEntry> pathEntriesInOrder, string proxyDir, IEnumerable<string> proxiedTools)
    {
        var pending = new HashSet<string>(proxiedTools, NameComparer);
        var shadows = new List<Shadow>();

        foreach (PathEntry entry in pathEntriesInOrder)
        {
            if (pending.Count == 0)
                break;

            // Compared here rather than through the caller's collection, so the
            // result never depends on which comparer that collection happens to use.
            var provided = new HashSet<string>(entry.Commands, NameComparer);

            if (SamePath(entry.Dir, proxyDir))
            {
                // The proxy directory has its turn: whatever it cannot answer for
                // has no proxy, and nothing after it can shadow anything.
                foreach (string tool in pending.Where(tool => !provided.Contains(tool)).OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase))
                    shadows.Add(new Shadow(tool, proxyDir) { Reason = Unreachable.NoProxy });
                break;
            }

            foreach (string tool in pending.Where(provided.Contains).ToList())
            {
                shadows.Add(new Shadow(tool, entry.Dir));
                pending.Remove(tool);
            }
        }

        return shadows;
    }

    /// <summary>
    /// The proxied tools shadowed on the stored PATH - what a newly opened
    /// terminal resolves. Empty when the proxy directory is not on the PATH at
    /// all; that is <see cref="IOService.ProxyDirIsOnStoredPath"/>'s question.
    /// </summary>
    public static IReadOnlyList<Shadow> FindShadowsOnStoredPath(IEnumerable<string>? expectedTools = null) =>
        FindShadowsOn(StoredPathDirs(), expectedTools);

    /// <summary>
    /// The proxied tools shadowed in *this* terminal. Differs from the stored PATH
    /// when a shell profile prepends entries at startup (conda, nvm, a wrapper
    /// script), which no stored-PATH check can see.
    /// </summary>
    public static IReadOnlyList<Shadow> FindShadowsOnProcessPath(IEnumerable<string>? expectedTools = null) =>
        FindShadowsOn(ProcessPathDirs(), expectedTools);

    /// <summary>The PATH this process was started with.</summary>
    internal static IEnumerable<string> ProcessPathDirs() =>
        SplitPath(Environment.GetEnvironmentVariable("PATH") ?? "");

    /// <summary>
    /// The command names the proxy directory currently answers for, as this
    /// process sees them. Reported by 'nem tool list' so a surprising verdict can
    /// be checked against what nem actually found on disk.
    /// </summary>
    public static IReadOnlyCollection<string> ProxiedCommandNames() =>
        CommandNamesIn(IOPathManager.System.ProxyDirPath);

    /// <summary>
    /// <paramref name="expectedTools"/> are the commands the env expects to own.
    /// Callers that know the env pass its tool bins, so a proxy that is *missing*
    /// is reported too; without them only the proxies that exist can be checked,
    /// and a deleted one looks like nothing at all.
    /// </summary>
    static IReadOnlyList<Shadow> FindShadowsOn(IEnumerable<string> pathDirs, IEnumerable<string>? expectedTools)
    {
        string proxyDir = IOPathManager.System.ProxyDirPath;
        List<string> dirs = pathDirs.ToList();

        // Without the proxy directory on the PATH there is no position to be
        // before, so every entry would look like a shadow.
        if (!dirs.Any(dir => SamePath(dir, proxyDir)))
            return Array.Empty<Shadow>();

        IEnumerable<string> tools = expectedTools ?? CommandNamesIn(proxyDir);
        return FindShadows(dirs.Select(dir => new PathEntry(dir, CommandNamesIn(dir))), proxyDir, tools);
    }

    /// <summary>
    /// Reports whatever stands between a typed tool name and the nem proxies in
    /// *this* terminal: the terminal predates the setup, a proxy is missing, or an
    /// earlier PATH entry answers first. Says nothing when the tools are reachable.
    /// </summary>
    public static void ReportProxyReachability(IEnumerable<string>? expectedTools = null)
    {
        List<string> storedDirs = StoredPathDirs().ToList();
        List<string> processDirs = ProcessPathDirs().ToList();
        string proxyDir = IOPathManager.System.ProxyDirPath;

        if (IsStaleTerminal(storedDirs, processDirs, proxyDir))
        {
            ReportStaleTerminal();
            return;
        }

        // The proxies are reachable here, so the shadow check below still applies;
        // the note only says that the next terminal will not be so lucky.
        if (IsUnstoredTerminal(storedDirs, processDirs, proxyDir))
            ReportUnstoredTerminal();

        ReportShadows(FindShadowsOnProcessPath(expectedTools));
    }

    /// <summary>
    /// True when the proxy directory is on the stored PATH but not on the one this
    /// process was started with: nem is set up, but this terminal predates it.
    /// </summary>
    internal static bool IsStaleTerminal(IEnumerable<string> storedDirs, IEnumerable<string> processDirs, string proxyDir)
    {
        return storedDirs.Any(dir => SamePath(dir, proxyDir))
            && !processDirs.Any(dir => SamePath(dir, proxyDir));
    }

    /// <summary>
    /// The reverse of <see cref="IsStaleTerminal"/>: this terminal has the proxy
    /// directory on its PATH but the stored PATH does not, so a new terminal will
    /// not. That is what a shell profile entry looks like, and what the terminal
    /// 'nem setup --uninstall' ran in looks like afterwards.
    /// </summary>
    internal static bool IsUnstoredTerminal(IEnumerable<string> storedDirs, IEnumerable<string> processDirs, string proxyDir)
    {
        return !storedDirs.Any(dir => SamePath(dir, proxyDir))
            && processDirs.Any(dir => SamePath(dir, proxyDir));
    }

    static void ReportUnstoredTerminal()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Note: the nem proxy directory is on this terminal's PATH but not on the stored[/]");
        AnsiConsole.MarkupLine("[yellow]PATH, so a new terminal will not find the proxies.[/]");
        AnsiConsole.MarkupLine("[yellow]Run [green]nem setup[/] to make it permanent.[/]");
    }

    static void ReportStaleTerminal()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Note: nem is set up, but this terminal was started before that happened -[/]");
        AnsiConsole.MarkupLine("[yellow]its PATH has no proxy directory, so typing a tool name here bypasses the env.[/]");
        AnsiConsole.MarkupLine("[yellow]Open a new terminal.[/]");
        if (OperatingSystem.IsWindows())
        {
            // A new window inherits its environment from its parent, so it only
            // helps once that parent (Explorer, or a running Terminal) has picked
            // the change up.
            AnsiConsole.MarkupLine("[yellow]If a brand new one still misses it, restart Windows Explorer or sign out and back in.[/]");
        }
        AnsiConsole.MarkupLine("[yellow]Until then, [green]nem run <tool>[/] uses the env regardless.[/]");
    }

    /// <summary>
    /// Prints a warning per shadowed tool. Nothing is printed when the proxies win.
    /// </summary>
    public static void ReportShadows(IReadOnlyList<Shadow> shadows)
    {
        if (shadows.Count == 0)
            return;

        AnsiConsole.WriteLine();
        foreach (Shadow shadow in shadows)
        {
            AnsiConsole.MarkupLine(shadow.Reason == Unreachable.NoProxy
                ? $"[yellow]Warning: '{Markup.Escape(shadow.ToolName)}' has no proxy, so typing it runs whatever else is on the PATH.[/]"
                : $"[yellow]Warning: '{Markup.Escape(shadow.ToolName)}' is answered by {Markup.Escape(shadow.ShadowingDir)} before the nem proxy.[/]");
        }

        if (shadows.Any(shadow => shadow.Reason == Unreachable.NoProxy))
            AnsiConsole.MarkupLine("[yellow]Re-run [green]nem install[/] to recreate the missing proxies.[/]");
        if (shadows.Any(shadow => shadow.Reason == Unreachable.Shadowed))
        {
            AnsiConsole.MarkupLine("[yellow]Remove the other copy, or move its PATH entry after[/]");
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(IOPathManager.System.ProxyDirPath)}.[/]");
        }

        AnsiConsole.MarkupLine("[yellow]Until then, [green]nem run <tool>[/] still uses the env.[/]");
    }

    /// <summary>
    /// The PATH a newly opened terminal gets. On Windows that is the machine PATH
    /// followed by the user PATH, which is the order the OS composes them in; the
    /// stored values are read rather than the process environment, so a setup that
    /// ran after this terminal started is still seen. Unix has no such store, so
    /// the process PATH is the best available answer.
    /// </summary>
    internal static IEnumerable<string> StoredPathDirs()
    {
        if (!OperatingSystem.IsWindows())
            return SplitPath(Environment.GetEnvironmentVariable("PATH") ?? "");

        string machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        return SplitPath(machinePath).Concat(SplitPath(userPath));
    }

    internal static IEnumerable<string> SplitPath(string path)
    {
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                   .Select(entry => entry.Trim())
                   .Where(entry => entry.Length > 0);
    }

    /// <summary>
    /// The command names a directory provides: file names without their extension,
    /// so 'ng.cmd', 'ng.ps1' and 'ng' all count as 'ng'. Unreadable directories
    /// provide nothing.
    /// </summary>
    static IReadOnlyCollection<string> CommandNamesIn(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToHashSet(NameComparer)!;
        }
        catch (Exception)
        {
            // A PATH entry we may not list (permissions, a dead network share)
            // cannot be proven to shadow anything.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// How the platform compares command names: Windows resolves them
    /// case-insensitively, Unix treats 'NG' and 'ng' as different commands.
    /// </summary>
    internal static StringComparer NameComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static bool SamePath(string a, string b)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Trim(a), Trim(b), comparison);

        static string Trim(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
