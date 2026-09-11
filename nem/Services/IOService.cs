using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
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

        string configPath = local.ConfigFilePath;
        if (File.Exists(configPath))
        {
            // Reconcile an existing config (e.g. one holding the partial spec "22")
            // with the fully resolved version, keeping everything else (tools) intact.
            NemConfig existing = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(configPath)) ?? new NemConfig();
            if (!string.Equals(existing.NodeVersion, version, System.StringComparison.OrdinalIgnoreCase))
            {
                existing.NodeVersion = version;
                File.WriteAllText(configPath, JsonConvert.SerializeObject(existing, Formatting.Indented));
                AnsiConsole.MarkupLine($"[yellow]Updated {local.ConfigFileName} to Node version {version}.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Skipped " + local.ConfigFileName + "-File because it already exists.[/]");
            }
        }
        else
        {
            File.WriteAllText(configPath, JsonConvert.SerializeObject(new NemConfig { NodeVersion = version }, Formatting.Indented));
        }

        string envPath = local.EnvDirPath;
        if (Directory.Exists(envPath))
            AnsiConsole.MarkupLine("[yellow]Skipped " + local.EnvDirName + "-Folder because it already exists.[/]");
        else
            Directory.CreateDirectory(envPath);

        string gitIgnorePath = Path.Combine(path, ".gitignore");
        string entry = local.EnvDirName;
        if (!File.Exists(gitIgnorePath))
            AnsiConsole.MarkupLine("[yellow]Skipped editing the .gitignore because it does not exist.[/]");
        else if (File.ReadAllText(gitIgnorePath).Contains("/" + entry))
            AnsiConsole.MarkupLine($"[yellow]Skipped editing the .gitignore because it already has an entry for /{entry}.[/]");
        else
        {
            string content = File.ReadAllText(gitIgnorePath);
            string prefix = content.Length == 0 ? "" : (content.EndsWith("\n") ? "" : "\n");
            File.WriteAllText(gitIgnorePath, content + prefix + $"#nem\n/{entry}\n");
        }
    }

    /// <summary>
    /// Ensures the nem system directories exist and that the proxy directory is on the PATH.
    /// Returns false (and prints a hint) when the PATH entry is missing.
    /// </summary>
    public static bool EnsureSystemDir()
    {
        EnsureSystemDirectories();

        if (ProxyDirIsOnPath())
            return true;

        AnsiConsole.MarkupLine("[red]The nem proxy directory is not in your PATH. Run [green]nem setup[/] first.[/]");
        return false;
    }

    /// <summary>
    /// Creates the nem system directory structure (cache, extract and proxy folders).
    /// </summary>
    public static void EnsureSystemDirectories()
    {
        if (!Directory.Exists(IOPathManager.System.DirPath)) Directory.CreateDirectory(IOPathManager.System.DirPath);
        if (!Directory.Exists(IOPathManager.System.DownloadCacheDirPath)) Directory.CreateDirectory(IOPathManager.System.DownloadCacheDirPath);
        if (!Directory.Exists(IOPathManager.System.ExtractCacheDirPath)) Directory.CreateDirectory(IOPathManager.System.ExtractCacheDirPath);
        if (!Directory.Exists(IOPathManager.System.ProxyDirPath)) Directory.CreateDirectory(IOPathManager.System.ProxyDirPath);
    }

    /// <summary>
    /// True when the nem proxy directory is listed in the PATH. Being listed is
    /// not the same as winning - see <see cref="PathPrecedence"/> for that.
    /// </summary>
    public static bool ProxyDirIsOnPath()
    {
        string proxyDir = IOPathManager.System.ProxyDirPath;
        return PathPrecedence.StoredPathDirs().Any(entry => PathPrecedence.SamePath(entry, proxyDir));
    }
}
