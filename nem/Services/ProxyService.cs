using nem.Common;
using nem.Common.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace nem.Services;

public static class ProxyService
{
    public static bool TryInstallTool(string toolName)
    {
        try
        {
            var nemExePath = Assembly.GetExecutingAssembly().Location;
            var nemDir = Path.GetDirectoryName(nemExePath)!;
            var proxyFilesDir = Path.Combine(nemDir, "ProxyFiles");
            var systemProxyDir = IOPathManager.System.ProxyDirPath;

            // Ensure system proxy directory exists
            if (!Directory.Exists(systemProxyDir))
                Directory.CreateDirectory(systemProxyDir);

            // Copy .bat proxy (Windows)
            var batSource = Path.Combine(proxyFilesDir, toolName + ".bat");
            var batDest = Path.Combine(systemProxyDir, toolName + ".bat");
            if (File.Exists(batSource))
                File.Copy(batSource, batDest, overwrite: true);

            // Copy .ps1 proxy (Windows PowerShell)
            var ps1Source = Path.Combine(proxyFilesDir, toolName + ".ps1");
            var ps1Dest = Path.Combine(systemProxyDir, toolName + ".ps1");
            if (File.Exists(ps1Source))
                File.Copy(ps1Source, ps1Dest, overwrite: true);

            // Copy linux proxy
            var linuxSource = Path.Combine(proxyFilesDir, toolName + ".linux");
            var linuxDest = Path.Combine(systemProxyDir, toolName);
            if (File.Exists(linuxSource))
            {
                File.Copy(linuxSource, linuxDest, overwrite: true);
                // Make executable on Unix
                if (!OperatingSystem.IsWindows())
                {
                    RunCommand("chmod", $"+x \"{linuxDest}\"");
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void CallToolInEnvContext(string tool, IReadOnlyList<string> args)
    {
        bool foundEnv = IOService.TryGetContainingEnv(Directory.GetCurrentDirectory(), out var nemJsonPath);

        string? envDir = null;

        if (foundEnv && nemJsonPath != null)
        {
            // Found nem.json — use managed environment
            var configDir = Path.GetDirectoryName(nemJsonPath)!;
            envDir = IOPathManager.Local(configDir).EnvDirPath;
        }

        ExecuteTool(tool, args, envDir);
    }

    private static void ExecuteTool(string toolName, IEnumerable<string> args, string? envDir)
    {
        string? resolvedToolPath;

        if (envDir != null)
        {
            // Managed environment: resolve from .nenv directory
            resolvedToolPath = ResolveToolInEnv(envDir, toolName);
            if (resolvedToolPath == null)
            {
                AnsiConsole.MarkupLine($"[red]Error: Tool '{toolName}' not found in .nenv[/]");
                Environment.Exit(1);
            }
        }
        else
        {
            // Global fallback: find real system tool (excluding nem proxies)
            resolvedToolPath = ResolveSystemTool(toolName);
            if (resolvedToolPath == null)
            {
                AnsiConsole.MarkupLine($"[red]Error: Tool '{toolName}' not found[/]");
                Environment.Exit(1);
            }
        }

        // Call with full path
        var psi = new ProcessStartInfo
        {
            FileName = resolvedToolPath,
            UseShellExecute = false,
            Arguments = string.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg))
        };

        if (envDir != null)
        {
            // Add managed env to PATH for child processes
            psi.Environment["PATH"] = envDir + Path.PathSeparator +
                                      Environment.GetEnvironmentVariable("PATH");
            psi.Environment["NODE_PATH"] = Path.Combine(envDir, "lib", "node_modules");
        }
        // else: keep original PATH unchanged — nem proxies are still there for child processes

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Environment.Exit(process.ExitCode);
    }

    private static string? ResolveToolInEnv(string envDir, string toolName)
    {
        // Check .nenv/tool.bat (Windows)
        var batPath = Path.Combine(envDir, toolName + ".bat");
        if (File.Exists(batPath))
            return batPath;

        // Check .nenv/tool (Unix/Linux executable)
        var unixPath = Path.Combine(envDir, toolName);
        if (File.Exists(unixPath))
            return unixPath;

        return null;
    }

    private static string? ResolveSystemTool(string toolName)
    {
        var systemProxyDir = IOPathManager.System.ProxyDirPath;
        string? result = null;

        if (OperatingSystem.IsWindows())
        {
            result = RunCommand("where", toolName);
        }
        else
        {
            result = RunCommand("which", toolName);
        }

        if (string.IsNullOrWhiteSpace(result))
            return null;

        // On Windows, `where` returns multiple matches — filter out nem proxies
        var lines = result.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var realPath = lines.FirstOrDefault(line =>
            !Path.GetFullPath(line).StartsWith(Path.GetFullPath(systemProxyDir), StringComparison.OrdinalIgnoreCase));

        return realPath;
    }

    private static string? RunCommand(string command, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }
}
