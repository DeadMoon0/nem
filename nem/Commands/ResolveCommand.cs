using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace nem.Commands;

internal class ResolveCommandSettings : CommandSettings
{
    [CommandArgument(0, "<toolName>")]
    [Description("The tool a proxy was called as, e.g. 'ng'.")]
    public required string ToolName { get; init; }

    [CommandOption("--shell <syntax>")]
    [Description("The syntax to answer in: sh, cmd or ps.")]
    public required ShellSyntax Shell { get; init; }
}

/// <summary>
/// What the proxies call: does everything 'nem run' does before starting a tool
/// (auto-install, resolving the env's copy), then prints the tool and its
/// environment for the proxy script to start - and exits. nem is not running
/// while the tool does, so a 'dotnet tool update -g nem' works at any time, and
/// stdin, stdout and stderr reach the tool straight from the shell.
/// </summary>
internal class ResolveCommand : Command<ResolveCommandSettings>
{
    protected override int Execute(CommandContext context, ResolveCommandSettings settings, CancellationToken cancellationToken)
    {
        // stdout carries only the answer the proxy reads. Everything else nem or a
        // library might print goes to stderr, where the user still sees it.
        TextWriter answer = Console.Out;
        Console.SetOut(Console.Error);
        AnsiConsole.Console = StdErr.Console;

        ToolLaunch? launch;
        int exitCode;
        try
        {
            launch = ProxyService.PrepareLaunch(settings.ToolName, Directory.GetCurrentDirectory(), EnvStore.Default, out exitCode);
        }
        catch (Exception e)
        {
            // Left to the parser's handler, a failure would be printed to stdout and
            // the proxy would try to run it.
            StdErr.Console.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
            launch = null;
            exitCode = 1;
        }
        answer.Write(launch == null
            ? ResolveOutput.FormatFailure(settings.Shell, exitCode)
            : ResolveOutput.Format(settings.Shell, launch));
        answer.Flush();
        return launch == null ? exitCode : 0;
    }
}
