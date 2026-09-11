using Microsoft.Win32;
using nem.Common;
using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class SetupCommandSettings : CommandSettings
{
    [CommandOption("-u|--uninstall")]
    [DefaultValue(false)]
    [Description("Undoes the setup: takes the nem proxy directory back out of your PATH and deletes the proxies. The env folders and caches are left alone.")]
    public required bool Uninstall { get; init; }
}

internal class SetupCommand : AsyncCommand<SetupCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SetupCommandSettings settings, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        if (!OperatingSystem.IsWindows())
            return settings.Uninstall ? RemoveUnixShellPath() : SetupUnixShellPath();

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
            if (settings.Uninstall)
                psi.ArgumentList.Add("--uninstall");

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
            bool onPath = IOService.ProxyDirIsOnPath();
            if (settings.Uninstall ? !onPath : onPath)
            {
                ReportPathChanged(settings.Uninstall);
                return 0;
            }

            string expected = settings.Uninstall ? "is still in the PATH" : "is still not in the PATH";
            AnsiConsole.MarkupLine($"[red]The elevated run ended with exit code {process.ExitCode}, but the proxy directory {expected}.[/]");
            AnsiConsole.MarkupLine("[red]Run the same command from an elevated terminal to see what went wrong.[/]");
            return process.ExitCode == 0 ? 1 : process.ExitCode;
        }

        string proxyPath = IOPathManager.System.ProxyDirPath;

        if (settings.Uninstall)
            return UninstallWindows(proxyPath);

        // Create the nem system directory structure
        IOService.EnsureSystemDirectories();
        AnsiConsole.MarkupLine($"[gray]Setup nem proxies: {proxyPath}[/]");

        if (!TryWriteMachinePath(current => PrependPathEntry(current, proxyPath)))
            return 1;

        ReportPathChanged(uninstall: false);
        return 0;
    }

    /// <summary>
    /// Takes the proxy directory back out of the machine PATH and deletes the
    /// proxies, which do nothing once the directory is off the PATH. The env
    /// folders and the download caches are left alone: they belong to projects,
    /// not to the PATH entry this command owns.
    /// </summary>
    [SupportedOSPlatform("windows")]
    static int UninstallWindows(string proxyPath)
    {
        AnsiConsole.MarkupLine($"[gray]Removing nem proxies: {proxyPath}[/]");

        if (!TryWriteMachinePath(current => RemovePathEntry(current, proxyPath)))
            return 1;

        DeleteProxyDirectory(proxyPath);
        ReportPathChanged(uninstall: true);
        AnsiConsole.MarkupLine($"[gray]Kept the caches and env folders. Remove {Markup.Escape(IOPathManager.System.DirPath)} by hand for a full clean.[/]");
        return 0;
    }

    static void DeleteProxyDirectory(string proxyPath)
    {
        try
        {
            if (Directory.Exists(proxyPath))
                Directory.Delete(proxyPath, recursive: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Could not delete {Markup.Escape(proxyPath)}: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("[yellow]It is off the PATH either way, so the proxies no longer answer.[/]");
        }
    }

    static void ReportPathChanged(bool uninstall)
    {
        AnsiConsole.MarkupLine(uninstall
            ? "[green]Removed the nem proxy directory from the machine PATH.[/]"
            : "[green]Successfully updated the machine PATH.[/]");

        if (!uninstall)
            PathPrecedence.ReportShadows(PathPrecedence.FindShadowsOnStoredPath());

        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
        AnsiConsole.MarkupLine("[yellow]A new window inherits its PATH from whatever started it, so if it still misses[/]");
        AnsiConsole.MarkupLine("[yellow]the change, restart Windows Explorer or sign out and back in.[/]");
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
    /// Unix: takes the marker-guarded lines back out of the shell rc files and
    /// deletes the proxies. The caches and env folders are left alone.
    /// </summary>
    static int RemoveUnixShellPath()
    {
        string proxyDir = IOPathManager.System.ProxyDirPath;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        List<string> updated = RemoveFromShellRcFiles(home);
        foreach (string rcFile in updated)
            AnsiConsole.MarkupLine($"[green]Cleaned {rcFile}[/]");

        if (updated.Count == 0)
            AnsiConsole.MarkupLine("[green]No rc file carried the nem PATH line.[/]");

        DeleteProxyDirectory(proxyDir);
        AnsiConsole.MarkupLine($"[gray]Kept the caches and env folders. Remove {Markup.Escape(IOPathManager.System.DirPath)} by hand for a full clean.[/]");
        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for the changes to take effect.[/]");
        return 0;
    }

    const string RcMarker = "# nem: prepend the nem proxy directory to the PATH";

    /// <summary>
    /// Appends the marker-guarded <c>export PATH=...&lt;proxyDir&gt;:$PATH</c>
    /// line to <c>.profile</c> (always) and to <c>.bashrc</c>/<c>.zshrc</c>/
    /// <c>.zprofile</c> if they exist. Idempotent: files already carrying the
    /// marker are skipped. Returns the files that were written.
    /// </summary>
    internal static List<string> UpdateShellRcFiles(string homeDir, string proxyDir)
    {
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
            if (File.Exists(rcFile) && File.ReadAllText(rcFile).Contains(RcMarker))
                continue; // already set up

            string content = File.Exists(rcFile) ? File.ReadAllText(rcFile).TrimEnd('\n', '\r') : "";
            if (content.Length > 0)
                content += "\n";
            File.WriteAllText(rcFile, content + RcMarker + "\n" + line + "\n");
            updated.Add(rcFile);
        }
        return updated;
    }

    /// <summary>
    /// Removes the marker line and the export under it from every rc file that
    /// carries them. Returns the files that were rewritten.
    /// </summary>
    internal static List<string> RemoveFromShellRcFiles(string homeDir)
    {
        var updated = new List<string>();
        foreach (string name in new[] { ".profile", ".bashrc", ".zshrc", ".zprofile" })
        {
            string rcFile = Path.Combine(homeDir, name);
            if (!File.Exists(rcFile))
                continue;

            string content = File.ReadAllText(rcFile);
            if (!content.Contains(RcMarker))
                continue;

            File.WriteAllText(rcFile, StripRcBlock(content));
            updated.Add(rcFile);
        }
        return updated;
    }

    /// <summary>
    /// Drops the marker line and the line after it, which is the export the marker
    /// introduces. Everything the user wrote around it is kept as it was.
    /// </summary>
    internal static string StripRcBlock(string content)
    {
        var kept = new List<string>();
        string[] lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').Trim() == RcMarker)
            {
                i++; // also drop the export line the marker introduces
                continue;
            }
            kept.Add(lines[i]);
        }

        return string.Join("\n", kept);
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
    static bool TryWriteMachinePath(Func<string, string> change)
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

            key.SetValue("Path", change(current), RegistryValueKind.ExpandString);
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
        var entries = SplitKeeping(currentPath, entry);
        entries.Insert(0, entry);

        return string.Join(";", entries);
    }

    /// <summary>
    /// Drops every copy of <paramref name="entry"/> from a ';'-separated PATH
    /// value, leaving the remaining entries and their order untouched.
    /// </summary>
    internal static string RemovePathEntry(string currentPath, string entry) =>
        string.Join(";", SplitKeeping(currentPath, entry));

    /// <summary>
    /// The PATH entries except <paramref name="entry"/>, trimmed and with blanks
    /// dropped. A trailing separator does not make an entry a different one.
    /// </summary>
    static List<string> SplitKeeping(string currentPath, string entry) =>
        currentPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(existing => existing.Trim())
            .Where(existing => existing.Length > 0 && !PathPrecedence.SamePath(existing, entry))
            .ToList();

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
