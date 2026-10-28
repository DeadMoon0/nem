using nem.Common;
using nem.Services.Diagnostics;
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
    /// <summary>The proxy script templates nem ships, next to its own assembly.</summary>
    internal static string ShippedTemplatesDir =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "ProxyFiles");

    /// <summary>
    /// Copies the proxy script templates (bat/ps1/sh) for the given tool name into the system proxy directory.
    /// </summary>
    public static bool TryInstallTool(string toolName) =>
        TryInstallTool(toolName, ShippedTemplatesDir, IOPathManager.System.ProxyDirPath);

    /// <summary>
    /// Copies the proxy script templates from <paramref name="proxyFilesDir"/>
    /// (NAME, NAME.bat, NAME.ps1) into <paramref name="targetDir"/> under the tool
    /// name. Returns true when at least one proxy file was written.
    /// </summary>
    public static bool TryInstallTool(string toolName, string proxyFilesDir, string targetDir)
    {
        try
        {
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            bool created = false;

            // Copy .bat proxy (Windows cmd)
            created |= TryCopyTemplate("NAME.bat", Path.Combine(targetDir, toolName + ".bat"), proxyFilesDir, targetDir);

            // Copy .ps1 proxy (Windows PowerShell)
            created |= TryCopyTemplate("NAME.ps1", Path.Combine(targetDir, toolName + ".ps1"), proxyFilesDir, targetDir);

            // Copy extensionless proxy (Unix / bash)
            created |= TryCopyTemplate("NAME", Path.Combine(targetDir, toolName), proxyFilesDir, targetDir);

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
    /// Rewrites every proxy in <paramref name="targetDir"/> that differs from its
    /// template, so proxies an older nem wrote start tools the current way. The
    /// templates are generic (they take the tool name from their own file name), so
    /// one comparison per file is enough. The proxies of <paramref name="runningTool"/>
    /// are left alone: cmd and sh read a script while it runs, and a rewrite would
    /// make them continue mid-line in the new text. Returns true when no stale
    /// proxy is left.
    /// </summary>
    internal static bool RefreshProxies(string proxyFilesDir, string targetDir, string? runningTool)
    {
        bool complete = true;
        foreach (string proxy in Directory.EnumerateFiles(targetDir))
        {
            string? template = Path.GetExtension(proxy).ToLowerInvariant() switch
            {
                ".bat" => "NAME.bat",
                ".ps1" => "NAME.ps1",
                "" => "NAME",
                _ => null,
            };
            string? templatePath = template == null ? null : Path.Combine(proxyFilesDir, template);
            if (templatePath == null || !File.Exists(templatePath))
                continue;

            try
            {
                if (File.ReadAllBytes(proxy).AsSpan().SequenceEqual(File.ReadAllBytes(templatePath)))
                    continue;

                if (string.Equals(Path.GetFileNameWithoutExtension(proxy), runningTool, StringComparison.OrdinalIgnoreCase))
                    complete = false;
                else
                    complete &= TryCopyTemplate(template!, proxy, proxyFilesDir, targetDir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Refreshed by a later call.
                complete = false;
            }
        }
        return complete;
    }

    /// <summary>
    /// Finds the nem env that contains the current directory, installs what the
    /// store lacks of it, and runs the tool in its context as a child process.
    /// Returns the tool's exit code.
    /// </summary>
    public static int CallToolInEnvContext(string tool, IReadOnlyList<string> args) =>
        CallToolInEnvContext(tool, args, Directory.GetCurrentDirectory(), EnvStore.Default);

    internal static int CallToolInEnvContext(string tool, IReadOnlyList<string> args, string cwd, EnvStore store)
    {
        ToolLaunch? launch = PrepareLaunch(tool, cwd, store, out int exitCode);
        return launch == null ? exitCode : Start(launch, args);
    }

    /// <summary>
    /// Everything up to starting a tool: installs what the store lacks of the env,
    /// then resolves the tool the way 'nem which' explains it. Null, with the reason
    /// on stderr and its exit code in <paramref name="exitCode"/>, when nothing can start.
    /// </summary>
    internal static ToolLaunch? PrepareLaunch(string tool, string cwd, EnvStore store, out int exitCode)
    {
        if (!AutoInstaller.TryPrepare(cwd, tool, store, out exitCode))
            return null;

        ToolResolver.ToolResolution resolution = ToolResolver.Resolve(cwd, tool, store);
        NemLog.Info("tool.resolved", new
        {
            tool,
            source = resolution.Source.ToString(),
            executable = resolution.ExecutablePath,
            node = resolution.Env?.NodeVersion,
            decidedBy = resolution.Chain.LastOrDefault(step => step.Decided)?.Finding,
        });
        if (resolution.ExecutablePath == null)
        {
            if (resolution.Env == null)
            {
                StdErr.Console.MarkupLine($"[red]Error: Tool '{Markup.Escape(tool)}' not found on PATH.[/]");
            }
            else
            {
                StdErr.Console.MarkupLine($"[red]Error: Tool '{Markup.Escape(tool)}' is not available in the env.[/]");
                StdErr.Console.MarkupLine($"Declare it with [green]nem tool add {Markup.Escape(tool)}[/], or install it into the env directly.");
            }
            exitCode = 1;
            return null;
        }

        exitCode = 0;
        var launch = new ToolLaunch(
            resolution.ExecutablePath,
            resolution.Env == null ? [] : ToolLaunch.EnvironmentFor(resolution.Env, Environment.GetEnvironmentVariable("PATH")));
        NemLog.Debug("tool.environment", new { variables = launch.Environment.ToDictionary(v => v.Key, v => v.Value) });
        return launch;
    }

    /// <summary>Starts the tool with nem's own stdin, stdout and stderr, and waits for it.</summary>
    static int Start(ToolLaunch launch, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = launch.ExecutablePath,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        foreach ((string name, string value) in launch.Environment)
            psi.Environment[name] = value;

        try
        {
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            StdErr.Console.MarkupLine($"[red]Error: Could not start '{Markup.Escape(launch.ExecutablePath)}': {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
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
