using nem.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace nem.Services;

/// <summary>
/// Decides which copy of a tool a command name reaches, and records why.
/// <para>
/// This is the one place that answers the question. <see cref="ProxyService"/>
/// launches what it returns and 'nem which' prints the reasoning behind it, so the
/// explanation cannot drift from the behaviour: they are the same decision, not
/// two implementations of one rule.
/// </para>
/// </summary>
public static class ToolResolver
{
    /// <summary>Which copy of a tool answers to the typed name.</summary>
    public enum ToolSource
    {
        /// <summary>The name never reaches nem, or there is no env: the system tool runs.</summary>
        Global,
        /// <summary>The env's own copy runs.</summary>
        Env,
        /// <summary>The env's copy runs, but a project copy nearer the cwd may take over.</summary>
        Local,
        /// <summary>Nothing runs: no env copy and no system copy to fall back to.</summary>
        Unavailable,
    }

    /// <summary>One question in the chain, and what answering it found.</summary>
    public sealed record ResolutionStep(string Question, string Finding, bool Decided = false);

    /// <summary>
    /// What a typed tool name resolves to, and the chain that got there.
    /// <see cref="ExecutablePath"/> is what to start; it is null only when
    /// <see cref="Source"/> is <see cref="ToolSource.Unavailable"/>.
    /// </summary>
    public sealed record ToolResolution(
        string ToolName,
        ToolSource Source,
        string? ExecutablePath,
        string? EnvDir,
        string? LocalPackageDir,
        string? LocalVersion,
        IReadOnlyList<ResolutionStep> Chain);

    /// <summary>
    /// Walks the precedence chain for <paramref name="toolName"/> as it applies in
    /// <paramref name="cwd"/>. Never throws and never starts anything: it only
    /// looks, so callers can report the outcome or act on it.
    /// </summary>
    public static ToolResolution Resolve(string cwd, string toolName)
    {
        var chain = new List<ResolutionStep>();

        // 1. Does the typed name reach nem at all? A missing or shadowed proxy means
        //    the shell hands the name to something else and nem never runs - but
        //    'nem run <tool>' still arrives here, so this is reported, not decisive.
        chain.Add(DescribeProxyReach(toolName));

        // 2. Is there an env above the cwd?
        if (!IOPathManager.TryFindEnv(cwd, out IOPathManager.IOPathManagerEnv? env))
        {
            chain.Add(new ResolutionStep("env", $"no {IOPathManager.ConfigFileNamesText} in this folder or above"));
            string? system = ProxyService.ResolveSystemTool(toolName);
            chain.Add(new ResolutionStep("system tool", system ?? "not found on the PATH", Decided: true));

            return new ToolResolution(
                toolName,
                system == null ? ToolSource.Unavailable : ToolSource.Global,
                system, null, null, null, chain);
        }

        chain.Add(new ResolutionStep("env", env.DirPath));

        // 3. Does the env carry the tool?
        string? envTool = ProxyService.ResolveToolInEnv(env.EnvDirPath, toolName);
        if (envTool == null)
        {
            chain.Add(new ResolutionStep("env copy", "not installed in this env", Decided: true));
            return new ToolResolution(toolName, ToolSource.Unavailable, null, env.EnvDirPath, null, null, chain);
        }

        chain.Add(new ResolutionStep("env copy", envTool));

        // 4. A copy nearer the cwd may take over once the env's copy is running -
        //    npm scripts always use it, and some CLIs hand over to it themselves.
        //    The command name is not the package name ('ng' ships in @angular/cli),
        //    so the env's own declaration is what says where to look.
        string packageName = PackageProviding(env, envDir: env.EnvDirPath, toolName);
        string? localDir = FindLocalPackageDir(cwd, packageName);
        string? localVersion = localDir == null ? null : ReadPackageVersion(localDir);
        if (localDir != null)
        {
            chain.Add(new ResolutionStep("project copy", localVersion == null ? localDir : $"{localVersion} in {localDir}", Decided: true));
            return new ToolResolution(toolName, ToolSource.Local, envTool, env.EnvDirPath, localDir, localVersion, chain);
        }

        chain.Add(new ResolutionStep("project copy", "none above this folder", Decided: true));
        return new ToolResolution(toolName, ToolSource.Env, envTool, env.EnvDirPath, null, null, chain);
    }

    /// <summary>
    /// Whether a typed name reaches the nem proxy, and what answers instead when it
    /// does not. Informational: it describes the shell's behaviour, not nem's.
    /// </summary>
    static ResolutionStep DescribeProxyReach(string toolName)
    {
        if (!IOService.ProxyDirIsOnProcessPath())
            return new ResolutionStep("typed name", "the proxy directory is not on this terminal's PATH");

        var shadows = PathPrecedence.FindShadowsOnProcessPath([toolName]);
        if (shadows.Count == 0)
            return new ResolutionStep("typed name", Path.Combine(IOPathManager.System.ProxyDirPath, toolName));

        PathPrecedence.Shadow shadow = shadows[0];
        return new ResolutionStep("typed name", shadow.Reason == PathPrecedence.Unreachable.NoProxy
            ? "no proxy - the name runs whatever else is on the PATH"
            : $"answered by {shadow.ShadowingDir} before the nem proxy");
    }

    /// <summary>
    /// The package that ships <paramref name="toolName"/> as a binary, according to
    /// what the env declares and installed. A command rarely matches its package
    /// name - 'ng' comes from @angular/cli, 'tsc' from typescript - so looking for
    /// the command under node_modules would find nothing. Falls back to the command
    /// name for a tool the env does not declare, which is the best guess available.
    /// </summary>
    static string PackageProviding(IOPathManager.IOPathManagerEnv env, string envDir, string toolName)
    {
        try
        {
            foreach (Common.Models.NemToolConfig tool in NemConfigFile.Read(env.ConfigFilePath).Tools)
            {
                foreach (string bin in ToolService.ReadToolBins(envDir, tool.ToolName))
                {
                    if (string.Equals(bin, toolName, StringComparison.OrdinalIgnoreCase))
                        return tool.ToolName;
                }
            }
        }
        catch (Exception)
        {
            // An unreadable config tells us nothing about which package owns the
            // command; the guess below is no worse than failing the whole lookup.
        }

        return toolName;
    }

    /// <summary>
    /// The nearest <c>node_modules/&lt;package&gt;</c> at or above
    /// <paramref name="startDir"/>, the way npm resolves it.
    /// </summary>
    internal static string? FindLocalPackageDir(string startDir, string packageName)
    {
        string relativePackagePath = Path.Combine(packageName.Split('/', '\\'));

        for (string? dir = Path.GetFullPath(startDir); dir != null; dir = Path.GetDirectoryName(dir))
        {
            string packageDir = Path.Combine(dir, "node_modules", relativePackagePath);
            if (File.Exists(Path.Combine(packageDir, "package.json")))
                return packageDir;
        }

        return null;
    }

    static string? ReadPackageVersion(string packageDir)
    {
        try
        {
            string? version = Newtonsoft.Json.Linq.JObject
                .Parse(File.ReadAllText(Path.Combine(packageDir, "package.json")))["version"]?.ToString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception)
        {
            // A malformed package.json tells us nothing about the version.
            return null;
        }
    }
}
