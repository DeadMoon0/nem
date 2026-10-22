using System;
using System.Diagnostics;
using System.IO;
using nem.Common;
using nem.Common.Models;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// Makes sure the store holds what an env declares before a tool runs in it.
/// After a pull or a branch switch changed the Node version or a tool, the next
/// tool start installs what is missing instead of failing or running something else.
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

    /// <summary>How an env's config maps onto the store.</summary>
    public abstract record EnvState
    {
        private EnvState() { }

        /// <summary>The config maps onto store entries; <see cref="EnvComposition.IsInstalled"/> says whether they all exist.</summary>
        public sealed record Composed(EnvComposition Env) : EnvState;

        /// <summary>The config cannot be read or names something the store cannot hold.</summary>
        public sealed record Invalid(string Reason) : EnvState;
    }

    static readonly IAnsiConsole Error = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error),
    });

    /// <summary>Maps the env's config onto the store. Reads the config and checks a few files; starts nothing.</summary>
    public static EnvState Inspect(IOPathManager.IOPathManagerEnv env, EnvStore store)
    {
        NemConfig config;
        try
        {
            config = NemConfigFile.Read(env.ConfigFilePath);
        }
        catch (Exception e)
        {
            return new EnvState.Invalid($"{IOPathManager.ConfigFileName} cannot be read: {e.Message}");
        }

        return EnvComposition.Compose(config, store) switch
        {
            EnvComposition.Result.Ok ok => new EnvState.Composed(ok.Env),
            EnvComposition.Result.Invalid invalid => new EnvState.Invalid(invalid.Reason),
            _ => throw new InvalidOperationException("EnvComposition.Result has exactly two cases."),
        };
    }

    /// <summary>What 'nem which' says about the env's state, and what the next tool start does about it.</summary>
    public static string Describe(EnvState state, bool optedOut) => state switch
    {
        EnvState.Invalid invalid => invalid.Reason,
        EnvState.Composed { Env.IsInstalled: true } => "installed in the store",
        EnvState.Composed composed when optedOut =>
            $"not in the store: {string.Join(", ", composed.Env.Missing)} - auto-install is off ({OptOutVariable}), run 'nem install'",
        EnvState.Composed composed => $"not in the store: {string.Join(", ", composed.Env.Missing)} - the next run installs it",
        _ => throw new InvalidOperationException("EnvState has exactly two cases."),
    };

    /// <summary>True when <paramref name="value"/> of <see cref="OptOutVariable"/> turns auto-install off.</summary>
    public static bool IsOptedOut(string? value) =>
        value?.Trim().ToLowerInvariant() is "0" or "false" or "no" or "off";

    /// <summary>
    /// Installs what the env above <paramref name="cwd"/> lacks in the store, and
    /// records that its entries are in use. Returns false when
    /// <paramref name="toolName"/> must not start; <paramref name="exitCode"/> then
    /// holds the reason's exit code.
    /// </summary>
    public static bool TryPrepare(string cwd, string toolName, EnvStore store, out int exitCode)
    {
        exitCode = 0;

        // Inside an install (a package script calling a proxied name) the env is
        // incomplete by definition; installing again would wait for our own lock.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentInstaller.InstallingVariable)))
            return true;

        if (!IOPathManager.TryFindEnv(cwd, out IOPathManager.IOPathManagerEnv? env))
            return true;

        EnvComposition composition;
        switch (Inspect(env, store))
        {
            case EnvState.Composed composed:
                composition = composed.Env;
                break;
            case EnvState.Invalid invalid:
                Error.MarkupLine($"[red]Cannot run '{Markup.Escape(toolName)}' in {Markup.Escape(env.DirPath)}: {Markup.Escape(invalid.Reason)}.[/]");
                exitCode = 1;
                return false;
            default:
                throw new InvalidOperationException("EnvState has exactly two cases.");
        }

        if (!composition.IsInstalled)
        {
            if (IsOptedOut(Environment.GetEnvironmentVariable(OptOutVariable)))
            {
                Error.MarkupLine($"[yellow]Not in the store: {Markup.Escape(string.Join(", ", composition.Missing))}; run [green]nem install[/] (auto-install is off: {OptOutVariable}).[/]");
                return true;
            }

            Error.MarkupLine($"[gray]Not in the store yet: {Markup.Escape(string.Join(", ", composition.Missing))} - installing before '{Markup.Escape(toolName)}' runs...[/]");
            exitCode = RunInstall(env);
            if (exitCode != 0)
            {
                Error.MarkupLine($"[red]The install failed, so '{Markup.Escape(toolName)}' was not started. Fix the error above, or run [green]nem install[/].[/]");
                return false;
            }
            Error.MarkupLine("[gray]Installed. Run [green]nem audit[/] to check it for known vulnerabilities.[/]");
        }

        DateTime now = DateTime.UtcNow;
        foreach (string entryDir in composition.EntryDirs)
            EnvStore.Touch(entryDir, now);
        return true;
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
