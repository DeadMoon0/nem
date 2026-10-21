using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nem.Common.Models;
using Semver;

namespace nem.Services;

/// <summary>
/// What an env's nem.jsonc means in the <see cref="EnvStore"/>: the entries it
/// uses, which of them are missing, and how a tool runs with them - the tools'
/// folders on the PATH first, then the Node version's.
/// </summary>
public sealed class EnvComposition
{
    /// <summary>One declared tool and its store entry.</summary>
    public sealed record ToolEntry(string Name, string Version, string Dir);

    /// <summary>A composition, or why the config cannot be composed.</summary>
    public abstract record Result
    {
        private Result() { }
        public sealed record Ok(EnvComposition Env) : Result;
        public sealed record Invalid(string Reason) : Result;
    }

    EnvComposition(string nodeVersion, int nodeMajor, string nodeDir, IReadOnlyList<ToolEntry> tools, string globalsDir)
    {
        NodeVersion = nodeVersion;
        NodeMajor = nodeMajor;
        NodeDir = nodeDir;
        Tools = tools;
        GlobalsDir = globalsDir;
    }

    /// <summary>The exact Node version; a partial spec ("22") the store has no match for stays as written.</summary>
    public string NodeVersion { get; }
    public int NodeMajor { get; }
    public string NodeDir { get; }
    public IReadOnlyList<ToolEntry> Tools { get; }

    /// <summary>The npm prefix for 'npm install -g' in this env (shared per Node major).</summary>
    public string GlobalsDir { get; }

    /// <summary>The Node entry first, then every tool entry.</summary>
    public IEnumerable<string> EntryDirs => Tools.Select(t => t.Dir).Prepend(NodeDir);

    /// <summary>What the store still lacks, as the user would name it ("Node 22.23.3", "typescript@5.9.3").</summary>
    public IReadOnlyList<string> Missing =>
        Tools.Where(t => !EnvStore.IsComplete(t.Dir)).Select(t => $"{t.Name}@{t.Version}")
            .Prepend(EnvStore.IsComplete(NodeDir) ? null : $"Node {NodeVersion}")
            .OfType<string>()
            .ToList();

    public bool IsInstalled => Missing.Count == 0;

    public NodeEnvLayout NodeLayout => NodeEnvLayout.Create(NodeDir);

    /// <summary>
    /// Where the env's commands live, in the order they win: declared tools (in
    /// declaration order), then 'npm install -g' globals, then Node itself - so a
    /// declared npm or corepack beats the one Node ships.
    /// </summary>
    public IReadOnlyList<string> BinDirs =>
        Tools.Select(t => NodeEnvLayout.Create(t.Dir).BinDir)
            .Append(NodeEnvLayout.Create(GlobalsDir).BinDir)
            .Append(NodeLayout.BinDir)
            .ToList();

    /// <summary>The env's commands ahead of <paramref name="existingPath"/>.</summary>
    public string BuildPathVariable(string? existingPath) =>
        string.Join(Path.PathSeparator, BinDirs.Append(existingPath ?? "").Where(entry => entry.Length > 0));

    /// <summary>NODE_PATH: every tool's modules folder, so require() finds the env's packages.</summary>
    public string NodePath =>
        string.Join(Path.PathSeparator, Tools.Select(t => NodeEnvLayout.Create(t.Dir).ModulesRoot)
            .Append(NodeEnvLayout.Create(GlobalsDir).ModulesRoot));

    /// <summary>The file a command name runs in this env, or null when no entry ships it.</summary>
    public string? ResolveCommand(string command) => FindCommand(BinDirs, command);

    /// <summary>The first file in <paramref name="dirs"/> that runs as <paramref name="command"/>.</summary>
    public static string? FindCommand(IEnumerable<string> dirs, string command)
    {
        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        foreach (string dir in dirs)
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(dir, command + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>The declared tool that ships <paramref name="command"/>, or null.</summary>
    public ToolEntry? ToolProviding(string command) =>
        Tools.FirstOrDefault(tool => EnvStore.IsComplete(tool.Dir)
            && ToolService.ReadToolBins(tool.Dir, tool.Name).Contains(command, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Maps a config onto the store. Starts nothing and needs no network: a partial
    /// Node spec ("22") takes the newest matching version the store already has.
    /// </summary>
    public static Result Compose(NemConfig config, EnvStore store)
    {
        string spec = NodeDownloadingService.NormalizeVersion(config.NodeVersion ?? "");
        if (spec.Length == 0)
            return new Result.Invalid("the config declares no NodeVersion");

        string nodeVersion;
        if (NodeDownloadingService.IsPartialVersionSpec(spec))
            nodeVersion = NewestMatching(spec, store.CompleteNodeVersions()) ?? spec;
        else if (NpmSemver.ParseVersion(spec) != null)
            nodeVersion = spec;
        else
            return new Result.Invalid($"'{config.NodeVersion}' is not a Node version");

        int nodeMajor = int.Parse(nodeVersion.Split('.')[0]);

        var tools = new List<ToolEntry>();
        foreach (NemToolConfig tool in config.Tools)
        {
            // The entry is named after the version, so it must be one version: a range
            // ("^17") would mean a different package as new releases come out.
            if (NpmSemver.ParseVersion(tool.ToolVersion) == null)
                return new Result.Invalid(
                    $"{tool.ToolName} is declared as '{tool.ToolVersion}', but the config needs an exact version - 'nem tool add {tool.ToolName}@{tool.ToolVersion}' resolves one");
            tools.Add(new ToolEntry(tool.ToolName, tool.ToolVersion, store.ToolEntryDir(tool.ToolName, tool.ToolVersion, nodeMajor)));
        }

        return new Result.Ok(new EnvComposition(
            nodeVersion, nodeMajor, store.NodeEntryDir(nodeVersion), tools, store.GlobalsDirFor(nodeMajor)));
    }

    internal static string? NewestMatching(string partialSpec, IEnumerable<string> versions) =>
        versions
            .Where(v => NodeDownloadingService.VersionSpecMatches(partialSpec, v))
            .Select(v => (Text: v, Version: NpmSemver.ParseVersion(v)))
            .Where(v => v.Version != null)
            .OrderByDescending(v => v.Version!, SemVersion.PrecedenceComparer)
            .Select(v => v.Text)
            .FirstOrDefault();
}
