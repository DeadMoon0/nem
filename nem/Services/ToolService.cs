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
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// Manages the Tools section of the nem config. The 'nem tool' commands are purely
/// declarative (they only edit the config, except for 'remove' which also cleans the env). The env
/// itself is materialized by 'nem install' via <see cref="InstallMissing"/>.
/// </summary>
public static class ToolService
{
    // Validates npm package names before they are used to build registry URLs.
    private static readonly Regex ValidPackageName = new(
        "^(@[a-z0-9][\\w.-]*\\/)?[a-z0-9][\\w.-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Declares a tool in the nem config (no install). Resolves the version to an exact one:
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
        ResolvedTool? resolved = null;
        string? resolveError = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Resolving {name} ...", _ =>
            {
                resolved = ResolveTool(name, version, config.NodeVersion ?? string.Empty, out resolveError);
            });

        if (resolved == null)
        {
            AnsiConsole.MarkupLine($"[red]Could not resolve a version for {Markup.Escape(name)}: {Markup.Escape(resolveError ?? "no version could be resolved")}.[/]");
            return 1;
        }

        var existing = config.Tools.FirstOrDefault(t => string.Equals(t.ToolName, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.ToolVersion = resolved.Version;
        else
            config.Tools.Add(new NemToolConfig { ToolName = name, ToolVersion = resolved.Version });

        NemConfigFile.Write(env.ConfigFilePath, config);
        AnsiConsole.MarkupLine($"[green]Added[/] {name}@{resolved.Version} to {Markup.Escape(env.ConfigFilePath)}.");

        // The proxies go in now, so typing a command reaches nem straight away -
        // which then installs the tool on first use. Before, the name went to
        // whatever else was on the PATH until someone ran 'nem install'.
        var proxied = resolved.Bins.Where(ProxyService.TryInstallTool).ToList();
        if (proxied.Count > 0)
            AnsiConsole.MarkupLine($"Commands: [green]{Markup.Escape(string.Join(", ", proxied))}[/] - the first use installs it, or run [green]nem install[/] now.");
        else
            AnsiConsole.MarkupLine("Run [green]nem install[/] to install it into the env.");
        return 0;
    }

    /// <summary>
    /// Removes a tool from the nem config. If the env has it installed, uninstalls it and
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

        // The commands come from the store entry the env used, read before the
        // declaration goes. The entry itself stays: other envs may use it, and
        // 'nem store prune' removes it once nobody does.
        List<string> bins = EnvComposition.Compose(config, EnvStore.Default) is EnvComposition.Result.Ok ok
            && ok.Env.Tools.FirstOrDefault(t => t.Name == tool.ToolName) is { } entry && EnvStore.IsComplete(entry.Dir)
            ? ReadToolBins(entry.Dir, entry.Name)
            : [];

        config.Tools.Remove(tool);
        NemConfigFile.Write(env.ConfigFilePath, config);
        AnsiConsole.MarkupLine($"Removed {packageName} from {Markup.Escape(env.ConfigFilePath)}.");

        foreach (string bin in bins)
            DeleteProxy(bin);
        return 0;
    }

    public static int List()
    {
        if (!TryGetEnvContext(out NemConfig? config, out IOPathManager.IOPathManagerEnv? env) || config == null || env == null)
            return NotInEnv();

        if (!TryCompose(config, out EnvComposition? composition))
            return 1;

        // Which of this env's commands a typed name actually reaches. Installed is
        // not the same as reachable: a missing or shadowed proxy sends the name
        // somewhere else entirely, which is invisible without asking.
        var unreachable = PathPrecedence
            .FindShadowsOnProcessPath(EnvironmentInstaller.ExpectedCommands(composition))
            .ToDictionary(shadow => shadow.ToolName, StringComparer.OrdinalIgnoreCase);

        var table = new Table();
        table.AddColumn(new TableColumn("Tool"));
        table.AddColumn(new TableColumn("Version"));
        table.AddColumn(new TableColumn("Status"));
        table.AddColumn(new TableColumn("Command"));

        string cwd = Directory.GetCurrentDirectory();
        bool anyLocal = false;

        foreach (EnvComposition.ToolEntry tool in composition.Tools)
        {
            bool installed = EnvStore.IsComplete(tool.Dir);
            string? localVersion = FindLocalToolVersion(cwd, tool.Name);
            anyLocal |= localVersion != null && !string.Equals(localVersion, tool.Version, StringComparison.OrdinalIgnoreCase);

            table.AddRow(
                tool.Name,
                tool.Version,
                installed ? "[green]installed[/]" : "[yellow]not installed[/]",
                installed ? DescribeReach(ReadToolBins(tool.Dir, tool.Name), unreachable, localVersion, tool.Version) : "-");
        }

        AnsiConsole.Write(table);

        if (anyLocal)
        {
            // Deliberately states what exists, not what wins: 'npm run' always uses
            // the project copy, and a CLI that hands over to a local install (the
            // Angular CLI does) will too - but a tool like tsc just runs the env
            // copy. Claiming it always takes over would be wrong for those.
            AnsiConsole.MarkupLine("[yellow]A project node_modules nearer this folder carries its own copy of the tool.[/]");
            AnsiConsole.MarkupLine("[yellow]'npm run' scripts use that copy, and CLIs that hand over to a local install do too.[/]");
            AnsiConsole.MarkupLine("[yellow]Run [green]nem which <tool>[/] to see which copy a typed name reaches.[/]");
        }

        // Name the directories the Command column is talking about, and what nem
        // actually found in the proxy one, so a surprising verdict can be checked
        // rather than guessed at.
        var proxied = PathPrecedence.ProxiedCommandNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        AnsiConsole.MarkupLine($"[gray]node     : {Markup.Escape(composition.NodeVersion)} in {Markup.Escape(composition.NodeDir)}[/]");
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
    /// The nem config only governs the env copy. Reported rather than fought: overriding
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
            // "local copy", not "local": nem still starts the env copy. Whether the
            // project one takes over is the tool's business, not something nem can
            // promise - see the note printed under the table.
            return localVersion != null && !string.Equals(localVersion, declaredVersion, StringComparison.OrdinalIgnoreCase)
                ? $"[green]via nem[/] [yellow](local copy {Markup.Escape(localVersion)})[/]"
                : "[green]via nem[/]";
        }

        return string.Join(", ", problems.Select(problem => problem.Reason == PathPrecedence.Unreachable.NoProxy
            ? $"[red]{Markup.Escape(problem.ToolName)}: no proxy[/]"
            : $"[red]{Markup.Escape(problem.ToolName)}: from {Markup.Escape(problem.ShadowingDir)}[/]"));
    }

    /// <summary>
    /// Puts every declared tool the store lacks into it, then (re)creates the
    /// proxies for all declared tools. Called by 'nem install'; the Node entry must
    /// be complete, since its npm does the installing.
    /// </summary>
    public static int InstallTools(EnvComposition env, EnvStore store, bool reinstall)
    {
        int exit = 0;
        int installed = 0;
        foreach (EnvComposition.ToolEntry tool in env.Tools)
        {
            int result = EnsureToolEntry(tool, env, store, reinstall, out bool didInstall);
            if (result != 0)
                exit = result;
            if (didInstall)
                installed++;
        }

        if (installed == 0 && exit == 0)
            AnsiConsole.MarkupLine(env.Tools.Count == 0 ? "[gray]No tools declared.[/]" : "[gray]All tools are in the store.[/]");

        foreach (EnvComposition.ToolEntry tool in env.Tools.Where(t => EnvStore.IsComplete(t.Dir)))
        {
            string binDir = NodeEnvLayout.Create(tool.Dir).BinDir;
            foreach (string bin in ReadToolBins(tool.Dir, tool.Name))
            {
                // Only proxy bins that actually exist (avoids ghosts for packages
                // without a bin entry).
                if (EnvComposition.FindCommand([binDir], bin) != null)
                    ProxyService.TryInstallTool(bin);
            }
        }

        return exit;
    }

    /// <summary>
    /// Makes sure the store holds one tool version, installed with the env's Node.
    /// npm installs straight into the entry; the completion marker, written last,
    /// is what makes it count - a failed or interrupted install is removed again.
    /// </summary>
    static int EnsureToolEntry(EnvComposition.ToolEntry tool, EnvComposition env, EnvStore store, bool reinstall, out bool didInstall)
    {
        didInstall = false;
        // Complete entries never change, so this needs no lock.
        if (EnvStore.IsComplete(tool.Dir) && !reinstall)
            return 0;

        using InstallLock? entryLock = store.Lock(tool.Dir, EnvStore.InstallLockTimeout,
            onWait: () => AnsiConsole.MarkupLine($"[gray]Another install is putting {Markup.Escape(tool.Name)}@{Markup.Escape(tool.Version)} into the store, waiting for it...[/]"));
        if (entryLock == null)
        {
            AnsiConsole.MarkupLine($"[red]Gave up waiting for the other install of {Markup.Escape(tool.Name)}@{Markup.Escape(tool.Version)}.[/]");
            return 1;
        }

        if (EnvStore.IsComplete(tool.Dir) && !reinstall)
            return 0;

        if (Directory.Exists(tool.Dir) && !store.TryDelete(tool.Dir))
        {
            AnsiConsole.MarkupLine($"[red]Could not remove the incomplete {Markup.Escape(tool.Dir)}.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"Installing [green]{Markup.Escape(tool.Name)}@{Markup.Escape(tool.Version)}[/] into the store ...");
        // --no-audit: npm's inline audit summary is re-emitted (with details) by
        // AuditService after the install, so it stays the single source of truth.
        int result = RunNpm(env.NodeLayout, tool.Dir, ["install", "-g", "--no-audit", $"{tool.Name}@{tool.Version}"], capture: false, out _);
        if (result == 0 && !IsToolInstalled(tool.Dir, tool.Name))
        {
            AnsiConsole.MarkupLine($"[red]npm finished, but {Markup.Escape(tool.Name)} is not in {Markup.Escape(tool.Dir)}.[/]");
            result = 1;
        }

        Diagnostics.NemLog.Info("store.tool", new { tool = $"{tool.Name}@{tool.Version}", entry = tool.Dir, npmExitCode = result });
        if (result != 0)
        {
            store.TryDelete(tool.Dir);
            return result;
        }

        EnvStore.MarkComplete(tool.Dir, DateTime.UtcNow);
        didInstall = true;
        return 0;
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
    /// Where 'npm -g --prefix <paramref name="prefixDir"/>' puts packages - the
    /// modules folder of a store entry.
    /// </summary>
    public static string ToolModulesRoot(string prefixDir) =>
        NodeEnvLayout.Create(prefixDir).ModulesRoot;

    public static bool IsToolInstalled(string prefixDir, string packageName) =>
        File.Exists(Path.Combine(ToolModulesRoot(prefixDir), packageName, "package.json"));

    /// <summary>
    /// Reads the bin entries of a package installed under a prefix; falls back to a name guess.
    /// </summary>
    public static List<string> ReadToolBins(string prefixDir, string packageName)
    {
        var bins = new List<string>();
        string packageJson = Path.Combine(ToolModulesRoot(prefixDir), packageName, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                bins.AddRange(ToolBins.FromBinField(packageName, JObject.Parse(File.ReadAllText(packageJson))["bin"]));
            }
            catch (Exception)
            {
                // Malformed package.json: fall back to the name guess below.
            }
        }

        if (bins.Count == 0)
            bins.Add(ToolBins.DefaultBinName(packageName));

        return bins.Distinct().ToList();
    }

    /// <summary>
    /// Runs a Node entry's npm and returns its (trimmed) stdout, or null on failure.
    /// </summary>
    internal static string? RunNpmCapture(NodeEnvLayout node, string[] args)
    {
        int exit = RunNpm(node, node.EnvDir, args, capture: true, out string? stdout);
        string? result = stdout?.Trim();
        return exit == 0 && !string.IsNullOrWhiteSpace(result) ? result : null;
    }

    /// <summary>
    /// Runs the npm of a Node entry with --prefix <paramref name="prefixDir"/>, so
    /// 'npm install -g' lands in that folder (a tool's store entry) together with
    /// its bin shims.
    /// </summary>
    private static int RunNpm(NodeEnvLayout node, string prefixDir, string[] args, bool capture, out string? stdout)
    {
        stdout = null;
        string npm = node.NpmEntry;
        if (!File.Exists(npm))
        {
            AnsiConsole.MarkupLine($"[red]npm not found at {Markup.Escape(npm)}. Run [green]nem install[/] first.[/]");
            return 1;
        }

        var psi = new ProcessStartInfo(npm)
        {
            UseShellExecute = false,
            // The prefix may not exist yet; npm creates it.
            WorkingDirectory = node.EnvDir,
        };
        if (capture)
        {
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
        }
        psi.ArgumentList.Add("--prefix");
        psi.ArgumentList.Add(prefixDir);
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        // Make sure the entry's node is first on PATH for anything npm spawns.
        psi.Environment["PATH"] = node.BuildPathVariable(Environment.GetEnvironmentVariable("PATH"));
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
    /// Resolves a package to an exact version against the registry (npm_config_registry,
    /// or the public registry): see <see cref="ToolVersionPicker"/> for the rules.
    /// Returns null with <paramref name="error"/> set to the reason when nothing
    /// could be resolved. Needs no Node: neither the env's nor one on the PATH.
    /// </summary>
    public static string? ResolveVersion(string packageName, string? range, string nodeVersion, out string? error) =>
        ResolveTool(packageName, range, nodeVersion, out error)?.Version;

    /// <summary>An exact tool version and the commands that version ships.</summary>
    public sealed record ResolvedTool(string Version, IReadOnlyList<string> Bins);

    /// <summary>
    /// Like <see cref="ResolveVersion"/>, plus the commands of the chosen version,
    /// read from the same registry document - so its proxies can exist before it
    /// is installed.
    /// </summary>
    public static ResolvedTool? ResolveTool(string packageName, string? range, string nodeVersion, out string? error)
    {
        var registry = new NpmRegistryClient(Environment.GetEnvironmentVariable("npm_config_registry"));
        (ResolvedTool? tool, string? reason) = registry.FetchPackument(packageName).Match<(ResolvedTool?, string?)>(
            ok: packument => ToolVersionPicker.Pick(packageName, packument, nodeVersion, range) switch
            {
                VersionPick.Picked picked => (new ResolvedTool(picked.Version, packument.Versions.GetValueOrDefault(picked.Version)?.Bins ?? []), null),
                VersionPick.Unresolved unresolved => (null, unresolved.Reason),
                _ => throw new InvalidOperationException("VersionPick has exactly two cases."),
            },
            failed: (_, message) => (null, message));

        error = reason;
        return tool;
    }

    private static int NotInEnv()
    {
        AnsiConsole.MarkupLine($"[red]No {IOPathManager.ConfigFileName} found in the current directory or any parent.[/]");
        AnsiConsole.MarkupLine($"Run [green]nem init <nodeVersion>[/] in your project root first.");
        return 1;
    }

    /// <summary>The env's composition in the default store; prints why when there is none.</summary>
    internal static bool TryCompose(NemConfig config, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EnvComposition? composition)
    {
        switch (EnvComposition.Compose(config, EnvStore.Default))
        {
            case EnvComposition.Result.Ok ok:
                composition = ok.Env;
                return true;
            case EnvComposition.Result.Invalid invalid:
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(invalid.Reason)}.[/]");
                break;
        }
        composition = null;
        return false;
    }

    private static bool TryGetEnvContext(out NemConfig? config, out IOPathManager.IOPathManagerEnv? env)
    {
        config = null;
        if (!IOPathManager.TryFindEnv(Directory.GetCurrentDirectory(), out env))
            return false;

        try
        {
            config = NemConfigFile.Read(env.ConfigFilePath);
        }
        catch (Exception e) when (e is not UnsupportedConfigVersionException)
        {
            // A config nem cannot read at all reads as "no env here", but one from a
            // newer nem is a different problem and has to say so.
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
