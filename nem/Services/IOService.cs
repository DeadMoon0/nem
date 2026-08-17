using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
using Spectre.Console;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace nem.Services;

public static class IOService
{
    public static void InitEnv(string path, string version)
    {
        string configPath = IOPathManager.Local(path).ConfigFilePath;
        if (File.Exists(configPath)) AnsiConsole.MarkupLine("[yellow]Skipped " + IOPathManager.Local(path).ConfigFileName + "-File because it already exists.[/]");
        else File.WriteAllText(configPath, JsonConvert.SerializeObject(new NemConfig { NodeVersion = version }, Formatting.Indented));

        string envPath = IOPathManager.Local(path).EnvDirPath;
        if (Directory.Exists(envPath)) AnsiConsole.MarkupLine("[yellow]Skipped " + IOPathManager.Local(path).EnvDirName + "-Folder because it already exists.[/]");
        else Directory.CreateDirectory(envPath);

        string gitIgnorePath = Path.Combine(path, ".gitignore");
        if (!File.Exists(gitIgnorePath)) AnsiConsole.MarkupLine("[yellow]Skipped editing the .gitignore because it does not exists.[/]");
        else if (File.ReadAllText(gitIgnorePath).Contains("/" + IOPathManager.Local(path).EnvDirName)) AnsiConsole.MarkupLine("[yellow]Skipped editing the .gitignore because it has already an entry for \"/" + IOPathManager.Local(path).EnvDirName + "\".[/]");
        else File.AppendAllLines(gitIgnorePath, [(File.ReadAllText(gitIgnorePath).EndsWith('\n') ? "" : "\n"), "#nem", "/" + IOPathManager.Local(path).EnvDirName]);
    }

    public static void EnsureSystemDir()
    {
        if (!Directory.Exists(IOPathManager.System.DirPath)) Directory.CreateDirectory(IOPathManager.System.DirPath);
        if (!Directory.Exists(IOPathManager.System.DownloadCacheDirPath)) Directory.CreateDirectory(IOPathManager.System.DownloadCacheDirPath);
        if (!Directory.Exists(IOPathManager.System.ExtractCacheDirPath)) Directory.CreateDirectory(IOPathManager.System.ExtractCacheDirPath);
        if (!Directory.Exists(IOPathManager.System.ProxyDirPath)) Directory.CreateDirectory(IOPathManager.System.ProxyDirPath);

        string currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        if (!currentPath.Contains(IOPathManager.System.ProxyDirPath))
        {
            string newPath = IOPathManager.System.ProxyDirPath + ";" + currentPath.TrimStart(';');
            Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.User);
        }
    }

    public static bool TryGetContainingEnv(string searchDirPath, [NotNullWhen(true)] out string? foundPath)
    {
        var local = IOPathManager.Local(searchDirPath);

        if (File.Exists(local.ConfigFilePath))
        {
            foundPath = local.ConfigFilePath;
            return true;
        }
        string? parent = Path.GetDirectoryName(searchDirPath);
        if (String.IsNullOrEmpty(parent))
        {
            foundPath = null;
            return false;
        }
        return TryGetContainingEnv(parent, out foundPath);
    }
}