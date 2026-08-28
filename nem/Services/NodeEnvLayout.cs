using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nem.Services;

/// <summary>
/// The on-disk layout of a nem env for the current platform. Windows keeps the
/// Windows convention (node.exe/npm.cmd in the env root, node_modules for
/// globals); Unix uses the standard Node.js prefix layout (bin/ for
/// executables, lib/node_modules for global packages). This type is the single
/// source of truth for those differences, so services do not each re-derive
/// the paths from the OS check.
/// </summary>
public sealed class NodeEnvLayout
{
    private NodeEnvLayout(string envDir, bool isWindows)
    {
        EnvDir = envDir;
        IsWindows = isWindows;
        BinDir = isWindows ? envDir : Path.Combine(envDir, "bin");
        NodeBinary = Path.Combine(BinDir, isWindows ? "node.exe" : "node");
        NpmEntry = Path.Combine(BinDir, isWindows ? "npm.cmd" : "npm");
        ModulesRoot = isWindows
            ? Path.Combine(envDir, "node_modules")
            : Path.Combine(envDir, "lib", "node_modules");
    }

    /// <summary>Creates the layout for the current OS.</summary>
    public static NodeEnvLayout Create(string envDir) => new(envDir, OperatingSystem.IsWindows());

    /// <summary>
    /// Creates the layout for the env directory at <paramref name="envDir"/> for
    /// a specific target platform (useful for cross-platform checks and tests).
    /// </summary>
    public static NodeEnvLayout Create(string envDir, bool isWindows)
    {
        return new NodeEnvLayout(envDir, isWindows);
    }

    /// <summary>The env root (.nenv directory).</summary>
    public string EnvDir { get; }

    public bool IsWindows { get; }

    /// <summary>Where node and the tool shims live (the npm --prefix bin dir).</summary>
    public string BinDir { get; }

    /// <summary>The node binary used to run env scripts and audits.</summary>
    public string NodeBinary { get; }

    /// <summary>The npm entry point of the env.</summary>
    public string NpmEntry { get; }

    /// <summary>Where 'npm -g' installs packages (and where NODE_PATH points).</summary>
    public string ModulesRoot { get; }

    /// <summary>
    /// Builds a PATH value that resolves the env's executables before anything
    /// else (highest priority first).
    /// </summary>
    public string BuildPathVariable(string? existingPath)
    {
        var entries = new List<string> { BinDir, EnvDir };
        if (!string.IsNullOrEmpty(existingPath))
            entries.Add(existingPath);
        return string.Join(Path.PathSeparator, entries.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
