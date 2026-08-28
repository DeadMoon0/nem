using nem.Common;
using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class SetupCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        if (!OperatingSystem.IsWindows())
            return SetupUnixShellPath();

        if (!IsRunAsAdmin())
        {
            // Relaunch elevated so we can patch the machine PATH.
            // Verb is only honored when UseShellExecute is true.
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the path of the running executable."),
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                Verb = "runas"
            };

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                // The user declined (or the shell refused) the elevation prompt.
                AnsiConsole.MarkupLine($"[red]Could not launch the elevated process: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine("[red]Run [green]nem setup[/] from an elevated terminal and try again.[/]");
                return 1;
            }

            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to launch the elevated process. Run [green]nem setup[/] as an administrator.[/]");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }

        // Create the nem system directory structure
        IOService.EnsureSystemDir();

        string proxyPath = IOPathManager.System.ProxyDirPath;
        AnsiConsole.MarkupLine($"[gray]Setup nem proxies: {proxyPath}[/]");

        // Prepend the proxy directory to the machine PATH, removing any stale entries first.
        string currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var entries = currentPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => !string.Equals(e, proxyPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Insert(0, proxyPath);

        Environment.SetEnvironmentVariable("Path", string.Join(";", entries), EnvironmentVariableTarget.Machine);

        AnsiConsole.MarkupLine("[green]Successfully updated the machine PATH.[/]");
        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
        return 0;
    }

    /// <summary>
    /// Unix: no elevation is available or needed, so the proxy directory is
    /// appended to the user's shell rc files instead of a system PATH variable.
    /// </summary>
    static int SetupUnixShellPath()
    {
        IOService.EnsureSystemDir();
        string proxyDir = IOPathManager.System.ProxyDirPath;
        string marker = "# nem: prepend the nem proxy directory to the PATH";
        string line = $"export PATH=\"{proxyDir}:$PATH\"";

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var rcFiles = new List<string> { Path.Combine(home, ".profile") };
        foreach (string name in new[] { ".bashrc", ".zshrc", ".zprofile" })
        {
            string rcFile = Path.Combine(home, name);
            if (File.Exists(rcFile))
                rcFiles.Add(rcFile);
        }

        bool changed = false;
        foreach (string rcFile in rcFiles)
        {
            if (File.Exists(rcFile) && File.ReadAllText(rcFile).Contains(marker))
                continue; // already set up

            string content = File.Exists(rcFile) ? File.ReadAllText(rcFile).TrimEnd('\n', '\r') : "";
            if (content.Length > 0)
                content += "\n";
            File.WriteAllText(rcFile, content + marker + "\n" + line + "\n");
            AnsiConsole.MarkupLine($"[green]Updated {rcFile}[/]");
            changed = true;
        }

        AnsiConsole.MarkupLine($"[gray]nem proxies: {proxyDir}[/]");
        if (!changed)
            AnsiConsole.MarkupLine("[green]Your shell PATH is already set up.[/]");
        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRunAsAdmin()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
