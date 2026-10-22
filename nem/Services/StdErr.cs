using System;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// The console for everything nem says while a tool is about to run: errors and
/// auto-install progress. stdout belongs to the tool - and for 'nem resolve' to
/// the proxy that reads its answer - so nem's own words never go there.
/// </summary>
public static class StdErr
{
    public static IAnsiConsole Console { get; } = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(System.Console.Error),
    });
}
