using nem.Common;
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
    /// <summary>
    /// Copies the proxy script templates (bat/ps1/sh) for the given tool name into the system proxy directory.
    /// </summary>
    public static bool TryInstallTool(string toolName)
    {
        try
        {
            var nemDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var proxyFilesDir = Path.Combine(nemDir, "ProxyFiles");
            var systemProxyDir = IOPathManager.System.ProxyDirPath;

            if (!Directory.Exists(systemProxyDir))
                Directory.CreateDirectory(systemProxyDir);

            bool created = false;

            // Copy .bat proxy (Windows cmd)
            created |= TryCopyTemplate("NAME.bat", Path.Combine(systemProxyDir, toolName + ".bat"), proxyFilesDir, systemProxyDir);

            // Copy .ps1 proxy (Windows PowerShell)
            created |= TryCopyTemplate("NAME.ps1", Path.Combine(systemProxyDir, toolName + ".ps1"), proxyFilesDir, systemProxyDir);

            // Copy extensionless proxy (Unix / bash)
            created |= TryCopyTemplate("NAME", Path.Combine(systemProxyDir, toolName), proxyFilesDir, systemProxyDir);

            return created;
        }
        catch
        {
            return false;
        }
    }

    static bool TryCopyTemplate(string templateName, string destPath, string proxyFilesDir, string systemProxyDir)
    {
        var source = Path.Combine(proxyFilesDir, templateName);
        if (!File.Exists(source))
            return false;

        File.Copy(source, destPath, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            // Best effort: make executable on Unix.
            RunCapture("chmod", new[] { "+x", destPath });
        }

        return true;
    }

    /// <summary>
    /// Finds the nem env that contains the current directory and runs the tool in its context.
    /// Returns the tool's exit code.
    /// </summary>
    public static int CallToolInEnvContext(string tool, IReadOnlyList<string> args)
    {
        string? envDir = null;

        if (IOService.TryGetContainingEnv(Directory.GetCurrentDirectory(), out var nemJsonPath))
        {
            string configDir = Path.GetDirectoryName(nemJsonPath)!;
            envDir = IOPathManager.Local(configDir).EnvDirPath;
        }

        return ExecuteTool(tool, args, envDir);
    }

    static int ExecuteTool(string toolName, IEnumerable<string> args, string? envDir)
    {
        string? resolvedToolPath;

        if (envDir != null)
        {
            // Managed environment: resolve from .nenv directory
            resolvedToolPath = ResolveToolInEnv(envDir, toolName);
            if (resolvedToolPath == null)
            {
                AnsiConsole.MarkupLine($"[red]Error: Tool '{toolName}' not found in .nenv. Use [green]nem tool add {toolName}[/] to install it.[/]");
                return 1;
            }
        }
        else
        {
            // Global fallback: find real system tool (excluding nem proxies)
            resolvedToolPath = ResolveSystemTool(toolName);
            if (resolvedToolPath == null)
            {
                AnsiConsole.MarkupLine($"[red]Error: Tool '{toolName}' not found on PATH.[/]");
                return 1;
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = resolvedToolPath,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (envDir != null)
        {
            // Prepend the env (and its tool bin dir) to PATH for the child process.
            var pathEntries = new List<string>();
            if (OperatingSystem.IsWindows())
                pathEntries.Add(envDir);
            else
                pathEntries.Add(Path.Combine(envDir, "bin"));
            pathEntries.Add(envDir);
            pathEntries.Add(Environment.GetEnvironmentVariable("PATH") ?? "");

            string separator = OperatingSystem.IsWindows() ? ";" : ":";
            psi.Environment["PATH"] = string.Join(separator, pathEntries.Distinct(StringComparer.OrdinalIgnoreCase));

            // Let node require() find globally installed env packages.
            string globalModules = OperatingSystem.IsWindows()
                ? Path.Combine(envDir, "node_modules")
                : Path.Combine(envDir, "lib", "node_modules");
            psi.Environment["NODE_PATH"] = globalModules;
        }

        try
        {
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Could not start '{resolvedToolPath}': {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Resolves a tool inside a nem env directory, checking the common binary locations and extensions.
    /// </summary>
    public static string? ResolveToolInEnv(string envDir, string toolName)
    {
        string[] roots = OperatingSystem.IsWindows()
            ? [envDir, Path.Combine(envDir, "bin")]
            : [Path.Combine(envDir, "bin"), envDir];

        string[] extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : [""];

        foreach (string root in roots)
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(root, toolName + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a tool on the system PATH, skipping nem's own proxies.
    /// </summary>
    public static string? ResolveSystemTool(string toolName)
    {
        string output;
        if (OperatingSystem.IsWindows())
            output = RunCapture("where", new[] { toolName }) ?? "";
        else
            output = RunCapture("which", new[] { toolName }) ?? "";

        if (string.IsNullOrWhiteSpace(output))
            return null;

        var systemProxyDir = Path.GetFullPath(IOPathManager.System.ProxyDirPath);
        var lines = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !Path.GetFullPath(l).Equals(systemProxyDir, StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFullPath(l).StartsWith(systemProxyDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lines.Count == 0)
            return null;

        // Prefer real executable files (e.g. node.exe / npm.cmd) over extensionless bash shims.
        if (OperatingSystem.IsWindows())
        {
            string[] preferred = [".exe", ".cmd", ".bat"];
            foreach (string ext in preferred)
            {
                string? match = lines.FirstOrDefault(l => l.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }
        }

        return lines[0];
    }

    /// <summary>
    /// Runs a command and returns its standard output, or null on failure.
    /// </summary>
    public static string? RunCapture(string command, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)!;
            string result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }
}
