using System;
using System.Text;

namespace nem.Services;

/// <summary>The shell syntax 'nem resolve' answers in, one per proxy script.</summary>
public enum ShellSyntax
{
    /// <summary>POSIX sh, for the extensionless proxy: lines to eval.</summary>
    Sh,
    /// <summary>cmd.exe, for the .bat proxy: 'set' lines a 'for /f' runs.</summary>
    Cmd,
    /// <summary>PowerShell, for the .ps1 proxy: plain NAME=value lines it parses itself.</summary>
    Ps,
}

/// <summary>
/// Writes 'nem resolve's answer: the launch's variables and the tool as
/// NEM_TOOL, each value escaped for the shell that reads it.
/// </summary>
public static class ResolveOutput
{
    /// <summary>The variable the proxy scripts start the tool from.</summary>
    public const string ToolVariable = "NEM_TOOL";

    /// <summary>The variable that carries a failure's exit code to the .bat proxy, which cannot read one otherwise.</summary>
    public const string ExitVariable = "NEM_EXIT";

    public static string Format(ShellSyntax shell, ToolLaunch launch)
    {
        var lines = new StringBuilder();
        foreach ((string name, string value) in launch.Environment)
            lines.Append(Line(shell, name, value, export: true));
        lines.Append(Line(shell, ToolVariable, launch.ExecutablePath, export: false));
        return lines.ToString();
    }

    /// <summary>
    /// The answer when no tool can start: nothing for sh and PowerShell, which read
    /// nem's exit code; the exit code as a variable for cmd, whose 'for /f' cannot.
    /// </summary>
    public static string FormatFailure(ShellSyntax shell, int exitCode) =>
        shell == ShellSyntax.Cmd ? Line(shell, ExitVariable, exitCode.ToString(), export: false) : "";

    static string Line(ShellSyntax shell, string name, string value, bool export) => shell switch
    {
        // Single quotes keep everything literal; a quote inside is closed, escaped and reopened.
        ShellSyntax.Sh => $"{(export ? "export " : "")}{name}='{value.Replace("'", "'\\''")}'\n",
        // set "NAME=value" takes everything up to the last quote; 'for /f' runs the
        // line after %-expansion, so % and ! in the value stay as they are.
        ShellSyntax.Cmd => $"set \"{name}={value}\"\r\n",
        // The .ps1 proxy splits on the first '=' and never evaluates the value.
        ShellSyntax.Ps => $"{name}={value}\n",
        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, null),
    };
}
