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
    private static string LoadResolveScript()
    {
        if (_resolveScript == null)
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string scriptPath = Path.Combine(dir, "Resources", "ResolveVersion.js");
            _resolveScript = File.ReadAllText(scriptPath);
        }
        return _resolveScript;
    }

    private static string? _resolveScript;

    /// <summary>
    /// Declares a tool in nem.json (no install). Resolves the version to an exact one:
    /// user version as-is (validated), or the newest version that supports the env's
    /// Node version when none is given.
    /// </summary>
    public static int Add(string packageSpec)
    {
        if (!TryGetEnvContext(out NemConfig? config, out string nemJsonPath) || config == null)
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
        string envDir = EnvDirOf(nemJsonPath);
        string? resolved = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Resolving {name} ...", _ =>
            {
                resolved = ResolveVersion(name, version, config.NodeVersion ?? string.Empty, envDir);
            });

        if (resolved == null)
        {
            AnsiConsole.MarkupLine($"[red]Could not resolve a version for {Markup.Escape(name)}. Check the package name and your network connection.[/]");
            return 1;
        }

        var existing = config.Tools.FirstOrDefault(t => string.Equals(t.ToolName, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.ToolVersion = resolved;
        else
            config.Tools.Add(new NemToolConfig { ToolName = name, ToolVersion = resolved });

        File.WriteAllText(nemJsonPath, JsonConvert.SerializeObject(config, Formatting.Indented) + Environment.NewLine);
        AnsiConsole.MarkupLine($"[green]Added[/] {name}@{resolved} to {Markup.Escape(Path.GetFileName(nemJsonPath))}.");
        AnsiConsole.MarkupLine($"Run [green]nem install[/] to install it into the env.");
        return 0;
    }

    /// <summary>
    /// Removes a tool from nem.json. If the env has it installed, uninstalls it and
    /// deletes its proxies.
    /// </summary>
    public static int Remove(string packageName)
    {
        if (!TryGetEnvContext(out NemConfig? config, out string nemJsonPath) || config == null)
            return NotInEnv();

        var tool = config.Tools.FirstOrDefault(t => string.Equals(t.ToolName, packageName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            AnsiConsole.MarkupLine($"[red]Tool '{Markup.Escape(packageName)}' is not listed in {Markup.Escape(Path.GetFileName(nemJsonPath))}.[/]");
            return 1;
        }

        config.Tools.Remove(tool);
        string nemJsonText = JsonConvert.SerializeObject(config, Formatting.Indented) + Environment.NewLine;
        File.WriteAllText(nemJsonPath, nemJsonText);
        AnsiConsole.MarkupLine($"Removed {packageName} from {Markup.Escape(Path.GetFileName(nemJsonPath))}.");

        string envDir = EnvDirOf(nemJsonPath);
        if (!IsToolInstalled(envDir, packageName))
            return 0;

        List<string> bins = ReadToolBins(envDir, packageName);
        int exit = RunNpm(envDir, new[] { "uninstall", "-g", packageName }, capture: false);
        foreach (string bin in bins)
            DeleteProxy(bin);

        AnsiConsole.MarkupLine($"Uninstalled {packageName} from the env.");
        return exit;
    }

    public static int List()
    {
        if (!TryGetEnvContext(out NemConfig? config, out string nemJsonPath) || config == null)
            return NotInEnv();

        string envDir = EnvDirOf(nemJsonPath);
        var table = new Table();
        table.AddColumn(new TableColumn("Tool"));
        table.AddColumn(new TableColumn("Version"));
        table.AddColumn(new TableColumn("Status"));

        foreach (var tool in config.Tools)
        {
            bool installed = IsToolInstalled(envDir, tool.ToolName);
            table.AddRow(tool.ToolName, tool.ToolVersion, installed ? "[green]installed[/]" : "[yellow]not installed[/]");
        }

        AnsiConsole.Write(table);
        return 0;
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
                continue;
            anythingMissing = true;
            AnsiConsole.MarkupLine($"Installing [green]{tool.ToolName}@{tool.ToolVersion}[/] ...");
            int result = RunNpm(envDir, new[] { "install", "-g", $"{tool.ToolName}@{tool.ToolVersion}" }, capture: false);
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
    /// The global modules root of the env (where 'npm -g' installs packages).
    /// </summary>
    public static string ToolModulesRoot(string envDir) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(envDir, "node_modules")
            : Path.Combine(envDir, "lib", "node_modules");

    public static bool IsToolInstalled(string envDir, string packageName) =>
        File.Exists(Path.Combine(ToolModulesRoot(envDir), packageName, "package.json"));

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
                        bins.Add(Path.GetFileName(bin.ToString()));
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
    /// Runs the env's npm with --prefix <envDir> (the env's node install IS the npm
    /// prefix; the flag is required on Windows to make npm create the bin shims).
    /// </summary>
    private static int RunNpm(string envDir, string[] args, bool capture)
    {
        string npm = OperatingSystem.IsWindows() ? Path.Combine(envDir, "npm.cmd") : Path.Combine(envDir, "npm");
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
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = envDir + Path.PathSeparator + pathVar;
        psi.Environment["npm_config_update_notifier"] = "false";

        using var process = Process.Start(psi);
        if (process == null)
            return 1;

        if (capture)
        {
            // stdout is piped and discarded; stderr stays inherited so problems are visible.
            process.StandardOutput.ReadToEnd();
        }
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Runs the env's node (or the system node) with the resolver script.
    /// Returns the resolved version or null.
    /// </summary>
    private static string? ResolveVersion(string packageName, string? range, string nodeVersion, string envDir)
    {
        // Prefer the env's node (it always bundles npm's semver); fall back to a node on PATH.
        string node = Path.Combine(envDir, "node.exe");
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
                return null;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return null;

            string? version = output.Trim().Split('\n', '\r').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim().Trim('"');
        }
        catch (Exception)
        {
            // No node available at all (not installed, not on PATH).
            return null;
        }
    }

    private static string EnvDirOf(string nemJsonPath) =>
        IOPathManager.Local(Path.GetDirectoryName(nemJsonPath)!).EnvDirPath;

    private static int NotInEnv()
    {
        AnsiConsole.MarkupLine($"[red]No {Markup.Escape(IOPathManager.Local(Directory.GetCurrentDirectory()).ConfigFileName)} found in the current directory or any parent.[/]");
        AnsiConsole.MarkupLine($"Run [green]nem init <nodeVersion>[/] first.");
        return 1;
    }

    private static bool TryGetEnvContext(out NemConfig? config, out string nemJsonPath)
    {
        config = null;
        nemJsonPath = string.Empty;
        if (!IOService.TryGetContainingEnv(Directory.GetCurrentDirectory(), out string? found))
            return false;

        nemJsonPath = found;
        try
        {
            config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(nemJsonPath));
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
