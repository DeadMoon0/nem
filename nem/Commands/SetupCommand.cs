using Microsoft.Win32;
using nem.Common;
using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
            // Without this the elevated process runs with no arguments: it prints
            // the help screen, exits 0, and setup silently does nothing.
            psi.ArgumentList.Add("setup");

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

            // The elevated run is a separate process in its own window, so its exit
            // code is the only thing we see of it. Confirm the PATH really changed
            // rather than reporting success on its word.
            if (IOService.ProxyDirIsOnPath())
            {
                AnsiConsole.MarkupLine("[green]Successfully updated the machine PATH.[/]");
                PathPrecedence.ReportShadows(PathPrecedence.FindShadowsOnStoredPath());
                AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
                AnsiConsole.MarkupLine("[yellow]A new window inherits its PATH from whatever started it, so if it still misses[/]");
                AnsiConsole.MarkupLine("[yellow]the proxies, restart Windows Explorer or sign out and back in.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]The elevated run ended with exit code {process.ExitCode}, but the proxy directory is still not in the PATH.[/]");
            AnsiConsole.MarkupLine("[red]Run [green]nem setup[/] from an elevated terminal to see what went wrong.[/]");
            return process.ExitCode == 0 ? 1 : process.ExitCode;
        }

        // Create the nem system directory structure
        IOService.EnsureSystemDirectories();

        string proxyPath = IOPathManager.System.ProxyDirPath;
        AnsiConsole.MarkupLine($"[gray]Setup nem proxies: {proxyPath}[/]");

        if (!TryPrependToMachinePath(proxyPath))
            return 1;

        AnsiConsole.MarkupLine("[green]Successfully updated the machine PATH.[/]");
        PathPrecedence.ReportShadows(PathPrecedence.FindShadowsOnStoredPath());
        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
        AnsiConsole.MarkupLine("[yellow]A new window inherits its PATH from whatever started it, so if it still misses[/]");
        AnsiConsole.MarkupLine("[yellow]the proxies, restart Windows Explorer or sign out and back in.[/]");
        return 0;
    }

    /// <summary>
    /// Unix: no elevation is available or needed, so the proxy directory is
    /// appended to the user's shell rc files instead of a system PATH variable.
    /// </summary>
    static int SetupUnixShellPath()
    {
        IOService.EnsureSystemDirectories();
        string proxyDir = IOPathManager.System.ProxyDirPath;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        List<string> updated = UpdateShellRcFiles(home, proxyDir);
        foreach (string rcFile in updated)
            AnsiConsole.MarkupLine($"[green]Updated {rcFile}[/]");

        AnsiConsole.MarkupLine($"[gray]nem proxies: {proxyDir}[/]");
        if (updated.Count == 0)
            AnsiConsole.MarkupLine("[green]Your shell PATH is already set up.[/]");
        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
        return 0;
    }

    /// <summary>
    /// Appends the marker-guarded <c>export PATH=...&lt;proxyDir&gt;:$PATH</c>
    /// line to <c>.profile</c> (always) and to <c>.bashrc</c>/<c>.zshrc</c>/
    /// <c>.zprofile</c> if they exist. Idempotent: files already carrying the
    /// marker are skipped. Returns the files that were written.
    /// </summary>
    internal static List<string> UpdateShellRcFiles(string homeDir, string proxyDir)
    {
        string marker = "# nem: prepend the nem proxy directory to the PATH";
        string line = $"export PATH=\"{proxyDir}:$PATH\"";

        var rcFiles = new List<string> { Path.Combine(homeDir, ".profile") };
        foreach (string name in new[] { ".bashrc", ".zshrc", ".zprofile" })
        {
            string rcFile = Path.Combine(homeDir, name);
            if (File.Exists(rcFile))
                rcFiles.Add(rcFile);
        }

        var updated = new List<string>();
        foreach (string rcFile in rcFiles)
        {
            if (File.Exists(rcFile) && File.ReadAllText(rcFile).Contains(marker))
                continue; // already set up

            string content = File.Exists(rcFile) ? File.ReadAllText(rcFile).TrimEnd('\n', '\r') : "";
            if (content.Length > 0)
                content += "\n";
            File.WriteAllText(rcFile, content + marker + "\n" + line + "\n");
            updated.Add(rcFile);
        }
        return updated;
    }

    /// <summary>
    /// Prepends the proxy directory to the machine PATH, writing the registry
    /// value directly.
    /// <para>
    /// <c>Environment.SetEnvironmentVariable</c> would be shorter but damages the
    /// value twice: it reads PATH back already expanded, so entries like
    /// <c>%SystemRoot%\system32</c> would be written back resolved and lose their
    /// indirection for good, and it stores the result as REG_SZ where Windows
    /// keeps PATH as REG_EXPAND_SZ.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    static bool TryPrependToMachinePath(string proxyPath)
    {
        const string EnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(EnvironmentKey, writable: true);
            if (key == null)
            {
                AnsiConsole.MarkupLine("[red]Could not open the machine environment key in the registry.[/]");
                return false;
            }

            // DoNotExpandEnvironmentNames keeps %VAR% entries as they were written.
            string current = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";

            key.SetValue("Path", PrependPathEntry(current, proxyPath), RegistryValueKind.ExpandString);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Could not update the machine PATH: {Markup.Escape(ex.Message)}[/]");
            return false;
        }

        BroadcastEnvironmentChange();
        return true;
    }

    /// <summary>
    /// Puts <paramref name="entry"/> at the front of a ';'-separated PATH value and
    /// drops any earlier copy of it, so the entry wins and re-running setup does
    /// not stack duplicates. The remaining entries are passed through untouched -
    /// including unexpanded ones like <c>%SystemRoot%\system32</c>.
    /// </summary>
    internal static string PrependPathEntry(string currentPath, string entry)
    {
        var entries = currentPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(existing => existing.Trim())
            .Where(existing => existing.Length > 0 && !string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Insert(0, entry);

        return string.Join(";", entries);
    }

    /// <summary>
    /// Tells running programs to re-read the environment. Writing the registry
    /// directly means nothing announces the change for us, and without this every
    /// terminal keeps the old PATH until the next sign-in.
    /// </summary>
    [SupportedOSPlatform("windows")]
    static void BroadcastEnvironmentChange()
    {
        const int HwndBroadcast = 0xFFFF;
        const int WmSettingChange = 0x001A;
        const int SmtoAbortIfHung = 0x0002;

        SendMessageTimeout((IntPtr)HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 5000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeoutMs, out IntPtr result);

    [SupportedOSPlatform("windows")]
    private static bool IsRunAsAdmin()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
