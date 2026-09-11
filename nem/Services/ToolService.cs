using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// Manages the Tools section of nem.json. The 'nem tool' commands are purely declarative
/// (they only edit nem.json, except for 'remove' which also cleans the env). The env
/// itself is materialized by 'nem install' via <see cref="InstallMissing"/>.
/// </summary>
public static class ToolService
{
    // Validates npm package names before they are used to build registry URLs.
    private static readonly Regex ValidPackageName = new(
        "^(@[a-z0-9][\\w.-]*\\/)?[a-z0-9][\\w.-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Node script that resolves a package to an exact version. Fetches the packument
    /// from the registry and picks, using npm's bundled semver:
    ///  - range empty        -> newest stable version whose engines.node allows nodeV
    ///  - range is a version -> that version (validated)
    ///  - range is a tag     -> the tag's version
    ///  - range is a range   -> newest stable version in the range
    /// Prints the version or an empty line. Exits 0 unless the script itself faults.
    /// </summary>
    /// <summary>
    /// Lazily loads the shipped resolver script (Resources/ResolveVersion.js), which
    /// runs under the env's node so it can use npm's bundled semver implementation.
    /// </summary>
    private static string LoadResolveScript() => _resolveScript ??= LoadResourceScript("ResolveVersion.js");

    /// <summary>
    /// Reads a shipped script from the Resources folder next to the assembly.
    /// </summary>
    internal static string LoadResourceScript(string fileName)
    {
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return File.ReadAllText(Path.Combine(dir, "Resources", fileName));
    }

    private static string? _resolveScript;

    /// <summary>
    /// Declares a tool in nem.json (no install). Resolves the version to an exact one:
    /// user version as-is (validated), or the newest version that supports the env's
    /// Node version when none is given.
    /// </summary>
    public static int Add(string packageSpec)
    {
        if (!TryGetEnvContext(out NemConfig? config, out IOPathManager.IOPathManagerEnv? env) || config == null || env == null)
            return NotInEnv();

        if (!TryParsePackageSpec(packageSpec, out string? packageName, out string? version))
        {
            AnsiConsole.MarkupLine($"[red]Invalid package spec:[/] {Markup.Escape(packageSpec)}. Expected [green]<package>[@<version>][/].");
            return 1;
        }

        if (!ValidPackageName.IsMatch(packageName!))
        {
            AnsiConsole.MarkupLine($"[red]'{Markup.Escape(packageName!)}' is not a valid npm package name.[/]");
            return 1;
        }

        string name = packageName!;
        string envDir = env.EnvDirPath;
        string? resolved = null;
        string? resolveError = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Resolving {name} ...", _ =>
            {
                resolved = ResolveVersion(name, version, config.NodeVersion ?? string.Empty, envDir, out resolveError);
            });

        if (resolved == null)
        {
            AnsiConsole.MarkupLine($"[red]Could not resolve a version for {Markup.Escape(name)}.[/]");
            if (resolveError != null)
                AnsiConsole.MarkupLine($"[red]  {Markup.Escape(resolveError)}[/]");
            AnsiConsole.MarkupLine("Check the package name, the requested version, and your network connection.");
            return 1;
        }

        var existing = config.Tools.FirstOrDefault(t => string.Equals(t.ToolName, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.ToolVersion = resolved;
        else
            config.Tools.Add(new NemToolConfig { ToolName = name, ToolVersion = resolved });

        File.WriteAllText(env.ConfigFilePath, JsonConvert.SerializeObject(config, Formatting.Indented) + Environment.NewLine);
        AnsiConsole.MarkupLine($"[green]Added[/] {name}@{resolved} to {Markup.Escape(env.ConfigFilePath)}.");
        AnsiConsole.MarkupLine($"Run [green]nem install[/] to install it into the env.");
        return 0;
    }

    /// <summary>
    /// Removes a tool from nem.json. If the env has it installed, uninstalls it and
    /// deletes its proxies.
    /// </summary>
    public static int Remove(string packageName)
    {
        if (!TryGetEnvContext(out NemConfig? config, out IOPathManager.IOPathManagerEnv? env) || config == null || env == null)
            return NotInEnv();

        var tool = config.Tools.FirstOrDefault(t => string.Equals(t.ToolName, packageName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            AnsiConsole.MarkupLine($"[red]Tool '{Markup.Escape(packageName)}' is not listed in {Markup.Escape(env.ConfigFilePath)}.[/]");
            return 1;
        }

        config.Tools.Remove(tool);
        string nemJsonText = JsonConvert.SerializeObject(config, Formatting.Indented) + Environment.NewLine;
        File.WriteAllText(env.ConfigFilePath, nemJsonText);
        AnsiConsole.MarkupLine($"Removed {packageName} from {Markup.Escape(env.ConfigFilePath)}.");

        string envDir = env.EnvDirPath;
        if (!IsToolInstalled(envDir, packageName))
            return 0;

        List<string> bins = ReadToolBins(envDir, packageName);
        int exit = RunNpm(envDir, new[] { "uninstall", "-g", packageName }, capture: false, out _);
        foreach (string bin in bins)
            DeleteProxy(bin);

        AnsiConsole.MarkupLine($"Uninstalled {packageName} from the env.");
        return exit;
    }

    public static int List()
    {
        if (!TryGetEnvContext(out NemConfig? config, out IOPathManager.IOPathManagerEnv? env) || config == null || env == null)
            return NotInEnv();

        string envDir = env.EnvDirPath;

        // Which of this env's commands a typed name actually reaches. Installed is
        // not the same as reachable: a missing or shadowed proxy sends the name
        // somewhere else entirely, which is invisible without asking.
        var unreachable = PathPrecedence
            .FindShadowsOnProcessPath(EnvironmentInstaller.ExpectedCommands(config, envDir))
            .ToDictionary(shadow => shadow.ToolName, StringComparer.OrdinalIgnoreCase);

        var table = new Table();
        table.AddColumn(new TableColumn("Tool"));
        table.AddColumn(new TableColumn("Version"));
        table.AddColumn(new TableColumn("Status"));
        table.AddColumn(new TableColumn("Command"));

        string cwd = Directory.GetCurrentDirectory();
        bool anyLocal = false;

        foreach (var tool in config.Tools)
        {
            bool installed = IsToolInstalled(envDir, tool.ToolName);
            string? localVersion = FindLocalToolVersion(cwd, tool.ToolName);
            anyLocal |= localVersion != null && !string.Equals(localVersion, tool.ToolVersion, StringComparison.OrdinalIgnoreCase);

            table.AddRow(
                tool.ToolName,
                tool.ToolVersion,
                installed ? "[green]installed[/]" : "[yellow]not installed[/]",
                installed ? DescribeReach(ReadToolBins(envDir, tool.ToolName), unreachable, localVersion, tool.ToolVersion) : "-");
        }

        AnsiConsole.Write(table);

        if (anyLocal)
        {
            AnsiConsole.MarkupLine("[yellow]A project node_modules here declares the tool itself and takes over from the env.[/]");
            AnsiConsole.MarkupLine("[yellow]That is how npm tools work; nem.json governs the env copy, the project governs its own.[/]");
        }

        // Name the directories the Command column is talking about, and what nem
        // actually found in the proxy one, so a surprising verdict can be checked
        // rather than guessed at.
        var proxied = PathPrecedence.ProxiedCommandNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        AnsiConsole.MarkupLine($"[gray]env      : {Markup.Escape(envDir)}[/]");
        AnsiConsole.MarkupLine($"[gray]proxies  : {Markup.Escape(IOPathManager.System.ProxyDirPath)}[/]");
        AnsiConsole.MarkupLine($"[gray]provides : {Markup.Escape(proxied.Count == 0 ? "(nothing)" : string.Join(", ", proxied))}[/]");
        AnsiConsole.MarkupLine($"[gray]on PATH  : {(IOService.ProxyDirIsOnProcessPath() ? "yes" : "no")}[/]");
        return 0;
    }

    /// <summary>
    /// Walks up from <paramref name="startDir"/> for a project copy of the package
    /// and returns its version, or null when there is none.
    /// <para>
    /// npm CLIs resolve the nearest node_modules before anything global, so a
    /// workspace that declares the tool itself decides which version runs there -
    /// nem.json only governs the env copy. Reported rather than fought: overriding
    /// it would build with a different tool than the project's lockfile pins.
    /// </para>
    /// </summary>
    internal static string? FindLocalToolVersion(string startDir, string packageName)
    {
        string relativePackagePath = Path.Combine(packageName.Split('/', '\\'));

        for (string? dir = Path.GetFullPath(startDir); dir != null; dir = Path.GetDirectoryName(dir))
        {
            string packageJson = Path.Combine(dir, "node_modules", relativePackagePath, "package.json");
            if (!File.Exists(packageJson))
                continue;

            try
            {
                string? version = JObject.Parse(File.ReadAllText(packageJson))["version"]?.ToString();
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }
            catch (Exception)
            {
                // A malformed package.json tells us nothing about the version.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// How the tool's bins resolve when typed: through the nem proxy, from a
    /// project-local copy, or from somewhere else entirely. Reported per tool so a
    /// single unreachable bin is not hidden by its siblings.
    /// </summary>
    private static string DescribeReach(
        IReadOnlyList<string> bins,
        Dictionary<string, PathPrecedence.Shadow> unreachable,
        string? localVersion,
        string declaredVersion)
    {
        var problems = bins.Where(unreachable.ContainsKey).Select(bin => unreachable[bin]).ToList();
        if (problems.Count == 0)
        {
            // The name reaches nem; a project copy still decides what nem launches.
            return localVersion != null && !string.Equals(localVersion, declaredVersion, StringComparison.OrdinalIgnoreCase)
                ? $"[yellow]local {Markup.Escape(localVersion)}[/]"
                : "[green]via nem[/]";
        }

        return string.Join(", ", problems.Select(problem => problem.Reason == PathPrecedence.Unreachable.NoProxy
            ? $"[red]{Markup.Escape(problem.ToolName)}: no proxy[/]"
            : $"[red]{Markup.Escape(problem.ToolName)}: from {Markup.Escape(problem.ShadowingDir)}[/]"));
    }

    /// <summary>
    /// Installs every tool declared in nem.json that is missing from the env, then
    /// (re)creates the proxies for all declared tools. Called by 'nem install'.
    /// </summary>
    public static int InstallMissing(NemConfig config, string envDir)
    {
        int exit = 0;
        bool anythingMissing = false;
        foreach (var tool in config.Tools)
        {
            if (IsToolInstalled(envDir, tool.ToolName))
            {
                // Re-install when what is on disk no longer matches the declared
                // version (e.g. after 'nem update' changed nem.json).
                string? installedVersion = GetInstalledToolVersion(envDir, tool.ToolName);
                if (installedVersion != null &&
                    string.Equals(installedVersion, tool.ToolVersion, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            anythingMissing = true;
            AnsiConsole.MarkupLine($"Installing [green]{tool.ToolName}@{tool.ToolVersion}[/] ...");
            // --no-audit: npm's inline audit summary is re-emitted (with details) by
            // AuditService after the install, so it stays the single source of truth.
            int result = RunNpm(envDir, new[] { "install", "-g", "--no-audit", $"{tool.ToolName}@{tool.ToolVersion}" }, capture: false, out _);
            if (result != 0)
                exit = result;
        }

        if (!anythingMissing)
            AnsiConsole.MarkupLine("All tools up to date.");

        foreach (var tool in config.Tools)
        {
            if (!IsToolInstalled(envDir, tool.ToolName))
                continue;
            foreach (string bin in ReadToolBins(envDir, tool.ToolName))
            {
                // Only proxy bins that actually exist in the env (avoids ghosts for
                // packages without a bin entry).
                if (ProxyService.ResolveToolInEnv(envDir, bin) != null)
                    ProxyService.TryInstallTool(bin);
            }
        }

        return exit;
    }

    // ---------- helpers ----------

    public static bool TryParsePackageSpec(string input, out string? packageName, out string? version)
    {
        packageName = null;
        version = null;
        input = input.Trim();

        // Use the last '@' so scoped names like @scope/pkg@version parse correctly.
        int atIndex = input.LastIndexOf('@');
        if (atIndex > 0 && atIndex < input.Length - 1)
        {
            packageName = input[..atIndex];
            version = input[(atIndex + 1)..];
        }
        else
        {
            packageName = input;
        }

        if (string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            version = null;
        }

        return true;
    }

    /// <summary>
    /// Validates npm package names before they are used to build registry URLs.
    /// </summary>
    public static bool IsValidPackageName(string name) =>
        !string.IsNullOrWhiteSpace(name) && ValidPackageName.IsMatch(name);

    /// <summary>
    /// The global modules root of the env (where 'npm -g' installs packages).
    /// </summary>
    public static string ToolModulesRoot(string envDir) =>
        NodeEnvLayout.Create(envDir).ModulesRoot;

    public static bool IsToolInstalled(string envDir, string packageName) =>
        File.Exists(Path.Combine(ToolModulesRoot(envDir), packageName, "package.json"));

    /// <summary>
    /// The version of the package currently on disk in the env, or null.
    /// </summary>
    public static string? GetInstalledToolVersion(string envDir, string packageName)
    {
        string packageJson = Path.Combine(ToolModulesRoot(envDir), packageName, "package.json");
        if (!File.Exists(packageJson))
            return null;
        try
        {
            var doc = JObject.Parse(File.ReadAllText(packageJson));
            string? version = doc["version"]?.ToString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the bin entries of an installed package; falls back to a name guess.
    /// </summary>
    public static List<string> ReadToolBins(string envDir, string packageName)
    {
        var bins = new List<string>();
        string packageJson = Path.Combine(ToolModulesRoot(envDir), packageName, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                var doc = JObject.Parse(File.ReadAllText(packageJson));
                var bin = doc["bin"];
                if (bin != null)
                {
                    if (bin.Type == JTokenType.String && !string.IsNullOrWhiteSpace(bin.ToString()))
                        // A string bin value means 'the executable is named after the package'.
                        bins.Add(DefaultBinName(packageName));
                    else if (bin.Type == JTokenType.Object)
                        bins.AddRange(((JObject)bin).Properties().Select(p => p.Name));
                }
            }
            catch (Exception)
            {
                // Malformed package.json: fall back to the name guess below.
            }
        }

        if (bins.Count == 0)
            bins.Add(DefaultBinName(packageName));

        return bins.Distinct().ToList();
    }

    private static string DefaultBinName(string packageName) =>
        packageName.Contains('/') ? packageName[(packageName.LastIndexOf('/') + 1)..] : packageName;

    /// <summary>
    /// Runs the env's npm and returns its (trimmed) stdout, or null on failure.
    /// </summary>
    internal static string? RunNpmCapture(string envDir, string[] args)
    {
        int exit = RunNpm(envDir, args, capture: true, out string? stdout);
        string? result = stdout?.Trim();
        return exit == 0 && !string.IsNullOrWhiteSpace(result) ? result : null;
    }

    /// <summary>
    /// Runs the env's npm with --prefix <envDir> (the env's node install IS the npm
    /// prefix; the flag is required on Windows to make npm create the bin shims).
    /// </summary>
    private static int RunNpm(string envDir, string[] args, bool capture, out string? stdout)
    {
        stdout = null;
        NodeEnvLayout layout = NodeEnvLayout.Create(envDir);
        string npm = layout.NpmEntry;
        if (!File.Exists(npm))
        {
            AnsiConsole.MarkupLine($"[red]npm not found in the env at {Markup.Escape(npm)}. Run [green]nem install[/] first.[/]");
            return 1;
        }

        var psi = new ProcessStartInfo(npm)
        {
            UseShellExecute = false,
            WorkingDirectory = envDir,
        };
        if (capture)
        {
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
        }
        psi.ArgumentList.Add("--prefix");
        psi.ArgumentList.Add(envDir);
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        // Make sure the env's node is first on PATH for anything npm spawns.
        psi.Environment["PATH"] = layout.BuildPathVariable(Environment.GetEnvironmentVariable("PATH"));
        psi.Environment["npm_config_update_notifier"] = "false";

        using var process = Process.Start(psi);
        if (process == null)
            return 1;

        if (capture)
        {
            // stderr stays inherited so problems are visible.
            stdout = process.StandardOutput.ReadToEnd();
        }
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Runs the env's node (or the system node) with the resolver script.
    /// Returns the resolved version or null (with <paramref name="error"/> set to a
    /// reason when the resolver reported one).
    /// </summary>
    public static string? ResolveVersion(string packageName, string? range, string nodeVersion, string envDir, out string? error)
    {
        error = null;
        // Prefer the env's node (it always bundles npm's semver); fall back to a node on PATH.
        string node = NodeEnvLayout.Create(envDir).NodeBinary;
        if (!File.Exists(node))
            node = "node";

        var psi = new ProcessStartInfo(node)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = envDir,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(LoadResolveScript());
        psi.ArgumentList.Add(packageName);
        psi.ArgumentList.Add(nodeVersion ?? "");
        psi.ArgumentList.Add(range ?? "");

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                error = "no node runtime available (the env node is not installed and no node is on the PATH).";
                return null;
            }
            string output = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? "the version resolver found no match." : stderr.Trim();
                return null;
            }

            string? version = output.Trim().Split('\n', '\r').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim().Trim('"');
        }
        catch (Exception)
        {
            // No node available at all (not installed, not on PATH).
            error = "no node runtime available (the env node is not installed and no node is on the PATH).";
            return null;
        }
    }

    private static int NotInEnv()
    {
        AnsiConsole.MarkupLine($"[red]No {Markup.Escape(IOPathManager.Local(Directory.GetCurrentDirectory()).ConfigFileName)} found in the current directory or any parent.[/]");
        AnsiConsole.MarkupLine($"Run [green]nem init <nodeVersion>[/] in your project root first.");
        return 1;
    }

    private static bool TryGetEnvContext(out NemConfig? config, out IOPathManager.IOPathManagerEnv? env)
    {
        config = null;
        if (!IOPathManager.TryFindEnv(Directory.GetCurrentDirectory(), out env))
            return false;

        try
        {
            config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(env.ConfigFilePath));
        }
        catch (Exception)
        {
            config = null;
        }
        return config != null;
    }

    private static void DeleteProxy(string toolName)
    {
        string proxyDir = IOPathManager.System.ProxyDirPath;
        foreach (string name in new[] { toolName, toolName + ".bat", toolName + ".ps1" })
        {
            string path = Path.Combine(proxyDir, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
