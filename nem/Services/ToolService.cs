using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace nem.Services;

/// <summary>
/// Installs npm packages into a nem env (npm -g --prefix &lt;env&gt;) and keeps nem.json and the proxy directory in sync.
/// </summary>
public static class ToolService
{
    /// <summary>
    /// Installs a package (name or name@version) globally into the env of the current directory.
    /// </summary>
    public static int Add(string packageSpec)
    {
        if (!TryGetEnvContext(out string nemJsonPath, out string envDir))
            return NotInEnv();

        if (!File.Exists(NodeDownloadingService.PrimaryNodeBinary(envDir)) || !File.Exists(NpmBinary(envDir)))
        {
            AnsiConsole.MarkupLine("[red]The env has no Node installation yet. Run [green]nem install[/] first.[/]");
            return 1;
        }

        if (!TryParsePackageSpec(packageSpec, out string packageName, out string? version))
        {
            AnsiConsole.MarkupLine($"[red]Invalid package '{Markup.Escape(packageSpec)}'. Expected 'name' or 'name@version'.[/]");
            return 1;
        }

        string npmBin = NpmBinary(envDir);
        if (version == null)
        {
            version = ProxyService.RunCapture(npmBin, new[] { "view", packageName, "version" });
            if (string.IsNullOrWhiteSpace(version))
            {
                AnsiConsole.MarkupLine($"[red]Could not resolve the latest version of '{Markup.Escape(packageName)}'. Check the package name.[/]");
                return 1;
            }
            version = version.Trim();
        }

        string spec = $"{packageName}@{version}";
        string binRoot = BinRoot(envDir);
        var before = Snapshot(binRoot);

        AnsiConsole.MarkupLine($"[gray]Installing {Markup.Escape(spec)} into {Markup.Escape(envDir)} ...[/]");
        int exit = RunNpm(npmBin, envDir, new[] { "install", "-g", spec, "--prefix", envDir });
        if (exit != 0)
        {
            AnsiConsole.MarkupLine("[red]npm install failed.[/]");
            return exit;
        }

        // Remember the version npm actually resolved (e.g. '15.2' -> '15.2.11').
        version = ReadInstalledVersion(envDir, packageName) ?? version;

        // Create proxies for all new shims (a package can expose several binaries).
        var newTools = new HashSet<string>();
        foreach (string file in Snapshot(binRoot).Except(before))
            AddToolName(newTools, file);

        foreach (string toolName in newTools)
            ProxyService.TryInstallTool(toolName);

        var tools = ToolsOf(nemJsonPath).Where(t => t.ToolName != packageName).ToList();
        tools.Add(new NemToolConfig { ToolName = packageName, ToolVersion = version });
        SaveTools(nemJsonPath, tools);

        AnsiConsole.MarkupLine($"[green]Installed {Markup.Escape(packageName)}@{Markup.Escape(version)}.[/]");
        foreach (string toolName in newTools)
            AnsiConsole.MarkupLine($"[gray]Proxy created for '{Markup.Escape(toolName)}'.[/]");

        return 0;
    }

    /// <summary>
    /// Removes a package from the env and from nem.json, and removes its proxies.
    /// </summary>
    public static int Remove(string packageName)
    {
        if (!TryGetEnvContext(out string nemJsonPath, out string envDir))
            return NotInEnv();

        var tools = ToolsOf(nemJsonPath);
        if (!tools.Any(t => t.ToolName == packageName))
        {
            AnsiConsole.MarkupLine($"[red]Tool '{Markup.Escape(packageName)}' is not in {Markup.Escape(IOPathManager.Local(Path.GetDirectoryName(nemJsonPath)!).ConfigFileName)}. Use [green]nem tool list[/].[/]");
            return 1;
        }

        string npmBin = NpmBinary(envDir);
        string binRoot = BinRoot(envDir);
        var before = Snapshot(binRoot);

        if (File.Exists(npmBin))
        {
            int exit = RunNpm(npmBin, envDir, new[] { "uninstall", "-g", packageName, "--prefix", envDir });
            if (exit != 0)
                AnsiConsole.MarkupLine("[yellow]npm uninstall reported an error, continuing...[/]");
        }

        var removedTools = new HashSet<string>();
        foreach (string file in before.Except(Snapshot(binRoot)))
            AddToolName(removedTools, file);

        foreach (string toolName in removedTools)
            DeleteProxy(toolName);

        SaveTools(nemJsonPath, tools.Where(t => t.ToolName != packageName).ToList());
        AnsiConsole.MarkupLine($"[green]Removed {Markup.Escape(packageName)} from the env.[/]");
        return 0;
    }

    /// <summary>
    /// Lists the tools configured in the env's nem.json.
    /// </summary>
    public static int List()
    {
        if (!TryGetEnvContext(out string nemJsonPath, out _))
            return NotInEnv();

        var tools = ToolsOf(nemJsonPath);
        if (tools.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tools are configured. Use [green]nem tool add &lt;package&gt;[/] to add one.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn(new TableColumn("Tool"));
        table.AddColumn(new TableColumn("Version"));
        foreach (NemToolConfig tool in tools)
            table.AddRow(tool.ToolName, tool.ToolVersion);

        AnsiConsole.Write(table);
        return 0;
    }

    static bool TryGetEnvContext(out string nemJsonPath, out string envDir)
    {
        if (IOService.TryGetContainingEnv(Directory.GetCurrentDirectory(), out string? json))
        {
            nemJsonPath = json!;
            envDir = IOPathManager.Local(Path.GetDirectoryName(nemJsonPath)!).EnvDirPath;
            return true;
        }

        nemJsonPath = "";
        envDir = "";
        return false;
    }

    static int NotInEnv()
    {
        AnsiConsole.MarkupLine("[red]No nem env found in this directory or any parent. Run [green]nem init[/] first.[/]");
        return 1;
    }

    static string NpmBinary(string envDir)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(envDir, "npm.cmd")
            : Path.Combine(envDir, "bin", "npm");
    }

    static string BinRoot(string envDir)
    {
        return OperatingSystem.IsWindows()
            ? envDir
            : Path.Combine(envDir, "bin");
    }

    /// <summary>
    /// Parses 'name' or 'name@version' (handles scoped names like @scope/name@1.0.0).
    /// </summary>
    static bool TryParsePackageSpec(string spec, out string name, out string? version)
    {
        int index = spec.LastIndexOf('@');
        if (index > 0 && index < spec.Length - 1)
        {
            name = spec.Substring(0, index);
            version = spec.Substring(index + 1);
            return true;
        }

        if (index == spec.Length - 1)
        {
            name = spec;
            version = null;
            return false;
        }

        name = spec;
        version = null;
        return true;
    }

    static void AddToolName(HashSet<string> names, string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string extension = Path.GetExtension(fileName);
        if (extension is ".cmd" or ".ps1" or ".bat")
            fileName = Path.GetFileNameWithoutExtension(fileName);
        if (fileName.Length > 0)
            names.Add(fileName);
    }

    static List<string> Snapshot(string dir)
    {
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir).ToList() : new List<string>();
    }

    /// <summary>
    /// Reads the version that npm actually resolved, from the installed package.json.
    /// </summary>
    static string? ReadInstalledVersion(string envDir, string packageName)
    {
        string moduleDir = OperatingSystem.IsWindows()
            ? Path.Combine(envDir, "node_modules", packageName)
            : Path.Combine(envDir, "lib", "node_modules", packageName);
        string packageJson = Path.Combine(moduleDir, "package.json");
        if (!File.Exists(packageJson))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packageJson));
            return doc.RootElement.TryGetProperty("version", out var element) ? element.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static void DeleteProxy(string toolName)
    {
        string proxyDir = IOPathManager.System.ProxyDirPath;
        foreach (string name in new[] { toolName, toolName + ".bat", toolName + ".ps1" })
        {
            string path = Path.Combine(proxyDir, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static int RunNpm(string npmBin, string cwd, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = npmBin, UseShellExecute = false, WorkingDirectory = cwd };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        // Make sure the env's node is used for npm's own script shims.
        psi.Environment["PATH"] = cwd + (OperatingSystem.IsWindows() ? ";" + (Environment.GetEnvironmentVariable("PATH") ?? "") : ":" + (Environment.GetEnvironmentVariable("PATH") ?? ""));

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    static List<NemToolConfig> ToolsOf(string nemJsonPath)
    {
        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(nemJsonPath))!;
        return config.Tools.ToList();
    }

    static void SaveTools(string nemJsonPath, List<NemToolConfig> tools)
    {
        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(nemJsonPath))!;
        config.Tools = tools;
        File.WriteAllText(nemJsonPath, JsonConvert.SerializeObject(config, Formatting.Indented));
    }
}
