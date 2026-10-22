using nem.Common;
using nem.Common.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nem.Services;

public static class IOService
{
    public static void InitEnv(string path, string version)
    {
        var local = IOPathManager.Local(path);

        if (IOPathManager.TryGetEnv(path, out IOPathManager.IOPathManagerEnv? existingEnv))
            ReconcileNodeVersion(existingEnv, version);
        else
            NemConfigFile.Write(local.ConfigFilePath, new NemConfig { NodeVersion = version });
    }

    /// <summary>
    /// Brings an existing config (e.g. one holding the partial spec "22") in line
    /// with the fully resolved version, keeping everything else (tools) intact.
    /// </summary>
    static void ReconcileNodeVersion(IOPathManager.IOPathManagerEnv env, string version)
    {
        NemConfig existing = NemConfigFile.Read(env.ConfigFilePath);
        if (string.Equals(existing.NodeVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[yellow]Skipped {IOPathManager.ConfigFileName} because it already exists.[/]");
            return;
        }

        existing.NodeVersion = version;
        NemConfigFile.Write(env.ConfigFilePath, existing);
        AnsiConsole.MarkupLine($"[yellow]Updated {IOPathManager.ConfigFileName} to Node version {version}.[/]");
    }

    /// <summary>
    /// Ensures the nem system directories exist and that the proxy directory is
    /// reachable: on the stored PATH (every new terminal gets it) or on this
    /// process's PATH (a shell profile put it there, or the terminal predates a
    /// 'nem setup --uninstall'). Returns false (and prints a hint) only when it is
    /// on neither, i.e. 'nem setup' never ran.
    /// </summary>
    public static bool EnsureSystemDir()
    {
        EnsureSystemDirectories();

        if (ProxyDirIsOnStoredPath() || ProxyDirIsOnProcessPath())
            return true;

        AnsiConsole.MarkupLine("[red]The nem proxy directory is not in your PATH. Run [green]nem setup[/] first.[/]");
        return false;
    }

    /// <summary>
    /// Creates the nem system directory structure (store and proxy folders).
    /// </summary>
    public static void EnsureSystemDirectories()
    {
        if (!Directory.Exists(IOPathManager.System.DirPath)) Directory.CreateDirectory(IOPathManager.System.DirPath);
        if (!Directory.Exists(IOPathManager.System.StoreDirPath)) Directory.CreateDirectory(IOPathManager.System.StoreDirPath);
        if (!Directory.Exists(IOPathManager.System.ProxyDirPath)) Directory.CreateDirectory(IOPathManager.System.ProxyDirPath);
    }

    /// <summary>
    /// True when the nem proxy directory is listed in the stored PATH, the one a
    /// newly opened terminal gets. This is what 'nem setup' writes and what its
    /// '--uninstall' removes, so it is the yardstick for both. Being listed is not
    /// the same as winning - see <see cref="PathPrecedence"/> for that.
    /// </summary>
    public static bool ProxyDirIsOnStoredPath() =>
        ContainsProxyDir(PathPrecedence.StoredPathDirs());

    /// <summary>
    /// True when the nem proxy directory is listed in the PATH this process was
    /// started with, so typing a proxied tool name in this terminal reaches nem.
    /// </summary>
    public static bool ProxyDirIsOnProcessPath() =>
        ContainsProxyDir(PathPrecedence.ProcessPathDirs());

    static bool ContainsProxyDir(IEnumerable<string> pathDirs)
    {
        string proxyDir = IOPathManager.System.ProxyDirPath;
        return pathDirs.Any(entry => PathPrecedence.SamePath(entry, proxyDir));
    }
}
