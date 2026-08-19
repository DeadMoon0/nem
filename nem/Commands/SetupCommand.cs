using nem.Common;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class SetupCommandSettings : CommandSettings
{

}

internal class SetupCommand : AsyncCommand<SetupCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SetupCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!IsRunAsAdmin())
        {
            AnsiConsole.MarkupLine("[gray]Requesting Admin ...[/]");
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.UseShellExecute = true;
            startInfo.WorkingDirectory = Environment.CurrentDirectory;
            startInfo.FileName = Environment.ProcessPath;
            startInfo.Verb = "runas";
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(arg);
            }
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not restart as Admin.");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }

        string currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        AnsiConsole.MarkupLine("[gray]Setting up PATH ...[/]");
        string newPath = IOPathManager.System.ProxyDirPath + ";" + currentPath.Replace(IOPathManager.System.ProxyDirPath + ";", "").TrimStart(';');
        Environment.SetEnvironmentVariable("Path", newPath, EnvironmentVariableTarget.Machine);
        AnsiConsole.MarkupLine("[green]Success.[/]");
        return 0;
    }

    private bool IsRunAsAdmin()
    {
        if (!OperatingSystem.IsWindows()) throw new NotImplementedException();
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}