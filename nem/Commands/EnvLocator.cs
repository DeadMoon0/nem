using nem.Common;
using Spectre.Console;
using System;
using System.Diagnostics.CodeAnalysis;

namespace nem.Commands;

/// <summary>
/// Locates the env a command should act on and reports the outcome. Every command
/// that *uses* an env goes through here, so 'nem install' run from a subfolder
/// finds the same env that 'nem run' and 'nem tool' find. Only 'nem init' picks
/// its folder directly, because it creates an env instead of using one.
/// </summary>
internal static class EnvLocator
{
    public static bool TryLocate(string startDir, [NotNullWhen(true)] out IOPathManager.IOPathManagerEnv? env)
    {
        if (!IOPathManager.TryFindEnv(startDir, out env))
        {
            AnsiConsole.MarkupLine($"[red]No {Markup.Escape(IOPathManager.ConfigFileNamesText)} found in {Markup.Escape(startDir)} or any folder above it.[/]");
            AnsiConsole.MarkupLine("Run [green]nem init <nodeVersion>[/] in your project root to create an env.");
            return false;
        }

        // Say which env was picked whenever it is not the folder that was asked for,
        // so a walk-up never happens behind the user's back.
        if (!string.Equals(env.DirPath, startDir, StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[gray]Using the env at {Markup.Escape(env.DirPath)}.[/]");

        return true;
    }
}
