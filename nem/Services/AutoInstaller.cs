using System;
using System.Diagnostics;
using System.IO;
using nem.Common;
using nem.Common.Models;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// Brings an env in line with its nem.jsonc before a tool runs in it. After a pull
/// or a branch switch changed the Node version or a tool, the next tool start
/// installs the difference instead of quietly running the old toolchain.
/// <para>
/// The install runs as a child 'nem install' whose output goes to stderr, so it
/// never mixes into the tool's own stdout ('npm ls --json > deps.json' stays
/// valid JSON). Set <see cref="OptOutVariable"/> to 0 to only get a warning.
/// </para>
/// </summary>
public static class AutoInstaller
{
    /// <summary>Set to 0, false, no or off to turn auto-install off.</summary>
    public const string OptOutVariable = "NEM_AUTO_INSTALL";

    /// <summary>How an env compares with its config.</summary>
    public enum EnvState
    {
        /// <summary>The last completed install was for exactly this config.</summary>
        Current,
        /// <summary>Never installed, or installed for a different config.</summary>
        Outdated,
        /// <summary>The config cannot be read, so there is nothing to compare with.</summary>
        Unreadable,
    }

    static readonly IAnsiConsole Error = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error),
    });

    /// <summary>Compares the env with its config. Reads two small files; starts nothing.</summary>
    public static EnvState Inspect(IOPathManager.IOPathManagerEnv env)
    {
        NemConfig config;
        try
        {
            config = NemConfigFile.Read(env.ConfigFilePath);
        }
        catch (Exception)
        {
            return EnvState.Unreadable;
        }

        string stampPath = Path.Combine(env.EnvDirPath, InstallStamp.FileName);
        string? stamp = File.Exists(stampPath) ? File.ReadAllText(stampPath) : null;
        return InstallStamp.Matches(stamp, config) ? EnvState.Current : EnvState.Outdated;
    }

    /// <summary>What 'nem which' says about the env's state, and what the next tool start does about it.</summary>
    public static string Describe(EnvState state, bool optedOut) => state switch
    {
        EnvState.Current => "matches the config",
        EnvState.Unreadable => "config cannot be read",
        _ when optedOut => $"does not match the config - auto-install is off ({OptOutVariable}), run 'nem install'",
        _ => "does not match the config - the next run installs it first",
    };

    /// <summary>True when <paramref name="value"/> of <see cref="OptOutVariable"/> turns auto-install off.</summary>
    public static bool IsOptedOut(string? value) =>
        value?.Trim().ToLowerInvariant() is "0" or "false" or "no" or "off";

    /// <summary>
    /// Installs the env above <paramref name="cwd"/> when it does not match its
    /// config. Returns false when <paramref name="toolName"/> must not start because
    /// the install failed; <paramref name="exitCode"/> then holds the install's.
    /// </summary>
    public static bool TryPrepare(string cwd, string toolName, out int exitCode)
    {
        exitCode = 0;

        // Inside an install (a package script calling a proxied name) the env is
        // out of date by definition; installing again would wait for our own lock.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentInstaller.InstallingVariable)))
            return true;

        if (!IOPathManager.TryFindEnv(cwd, out IOPathManager.IOPathManagerEnv? env) || Inspect(env) != EnvState.Outdated)
            return true;

        if (IsOptedOut(Environment.GetEnvironmentVariable(OptOutVariable)))
        {
            Error.MarkupLine($"[yellow]The env does not match {Markup.Escape(env.ConfigFileName)}; run [green]nem install[/] (auto-install is off: {OptOutVariable}).[/]");
            return true;
        }

        Error.MarkupLine($"[gray]The env does not match {Markup.Escape(env.ConfigFileName)} - installing it before '{Markup.Escape(toolName)}' runs...[/]");
        exitCode = RunInstall(env);
        if (exitCode == 0)
        {
            Error.MarkupLine("[gray]Env installed. Run [green]nem audit[/] to check it for known vulnerabilities.[/]");
            return true;
        }

        Error.MarkupLine($"[red]The install failed, so '{Markup.Escape(toolName)}' was not started. Fix the error above, or run [green]nem install[/].[/]");
        return false;
    }

    /// <summary>Runs 'nem install --no-audit' for the env, its stdout copied to stderr.</summary>
    static int RunInstall(IOPathManager.IOPathManagerEnv env)
    {
        ProcessStartInfo psi = SelfStartInfo();
        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("--no-audit");
        psi.ArgumentList.Add(env.DirPath);
        psi.WorkingDirectory = env.DirPath;
        psi.RedirectStandardOutput = true;

        try
        {
            using var process = Process.Start(psi)!;
            // Raw bytes, so npm's progress output keeps its carriage returns.
            Stream stderr = Console.OpenStandardError();
            process.StandardOutput.BaseStream.CopyTo(stderr);
            stderr.Flush();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception e)
        {
            Error.MarkupLine($"[red]Could not start 'nem install': {Markup.Escape(e.Message)}[/]");
            return 1;
        }
    }

    /// <summary>A start info that runs this nem again, however it was started.</summary>
    internal static ProcessStartInfo SelfStartInfo()
    {
        string host = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot tell where nem itself runs from.");
        var psi = new ProcessStartInfo(host) { UseShellExecute = false };

        // Started as 'dotnet nem.dll' (a development build) the host is dotnet
        // itself, which needs the assembly to run; the installed tool is its own host.
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(AutoInstaller).Assembly.Location);

        return psi;
    }
}
