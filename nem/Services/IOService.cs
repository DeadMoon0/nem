using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
            AnsiConsole.MarkupLine("[yellow]Skipped " + local.ConfigFileName + "-File because it already exists.[/]");
        else
            File.WriteAllText(configPath, JsonConvert.SerializeObject(new NemConfig { NodeVersion = version }, Formatting.Indented));

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
            File.WriteAllText(gitIgnorePath, prefix + $"#nem\n/{entry}\n");
        }
    }

    /// <summary>
    /// Ensures the nem system directories exist and that the proxy directory is on the PATH.
    /// Returns false (and prints a hint) when the PATH entry is missing.
    /// </summary>
    public static bool EnsureSystemDir()
    {
        if (!Directory.Exists(IOPathManager.System.DirPath)) Directory.CreateDirectory(IOPathManager.System.DirPath);
        if (!Directory.Exists(IOPathManager.System.DownloadCacheDirPath)) Directory.CreateDirectory(IOPathManager.System.DownloadCacheDirPath);
        if (!Directory.Exists(IOPathManager.System.ExtractCacheDirPath)) Directory.CreateDirectory(IOPathManager.System.ExtractCacheDirPath);
        if (!Directory.Exists(IOPathManager.System.ProxyDirPath)) Directory.CreateDirectory(IOPathManager.System.ProxyDirPath);

        string proxyDir = IOPathManager.System.ProxyDirPath;
        bool onPath;
        if (OperatingSystem.IsWindows())
        {
            string currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            onPath = SplitPath(currentPath).Any(entry => string.Equals(entry, proxyDir, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            onPath = SplitPath(currentPath).Any(entry => string.Equals(entry, proxyDir));
        }

        if (!onPath)
        {
            AnsiConsole.MarkupLine("[red]The nem proxy directory is not in your PATH. Run [green]nem setup[/] first.[/]");
            return false;
        }

        return true;
    }

    static IEnumerable<string> SplitPath(string path)
    {
        char separator = OperatingSystem.IsWindows() ? ';' : ':';
        return path.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(p => p.Trim())
                   .Where(p => p.Length > 0);
    }

    /// <summary>
    /// Walks up from the given directory looking for a nem.json (project root marker).
    /// </summary>
    public static bool TryGetContainingEnv(string searchDirPath, [NotNullWhen(true)] out string? foundPath)
    {
        var local = IOPathManager.Local(searchDirPath);

        if (File.Exists(local.ConfigFilePath))
        {
            foundPath = local.ConfigFilePath;
            return true;
        }

        string? parent = Path.GetDirectoryName(searchDirPath);
        if (string.IsNullOrEmpty(parent) || parent == searchDirPath)
        {
            foundPath = null;
            return false;
        }

        return TryGetContainingEnv(parent, out foundPath);
    }
}
