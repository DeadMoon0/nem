using nem.Common;
using nem.Common.Models;
using nem.Services;
using Newtonsoft.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class UpdateCommandSettings : CommandSettings
{
    [CommandArgument(0, "[what]")]
    [Description("What to update: a Node version (e.g. '22' or '18.12.0'), a tool package (e.g. 'typescript' or '@angular/cli@22'), or 'all' for the Node version and every tool. Omit to review and update interactively.")]
    public string? What { get; init; }

    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The folder path where the env is located.")]
    public required string Path { get; init; }

    [CommandOption("-t|--tools")]
    [Description("When updating the Node version, also update all tools to the newest versions it supports.")]
    public bool Tools { get; init; }
}

internal class UpdateCommand : AsyncCommand<UpdateCommandSettings>
{
    // A bare Node version spec: "22", "22.0", "22.23.2", with an optional leading
    // "v" and optional trailing ".". Anything else is treated as a tool package.
    private static readonly Regex NodeVersionSpec =
        new(@"^[vV]?\d+([.]\d+){0,2}\.?$", RegexOptions.Compiled);

    protected override async Task<int> ExecuteAsync(CommandContext context, UpdateCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!IOService.EnsureSystemDir())
            return 1;

        string path = Path.GetFullPath(settings.Path);
        var local = IOPathManager.Local(path);
        if (!File.Exists(local.ConfigFilePath))
        {
            AnsiConsole.MarkupLine($"[red]No {local.ConfigFileName} found in {Markup.Escape(path)}. Run [green]nem init[/] first.[/]");
            return 1;
        }

        NemConfig config = LoadConfig(local);
        string envDir = local.EnsureEnvDirPath();
        bool interactive = AnsiConsole.Profile.Capabilities.Interactive;

        string what = settings.What?.Trim() ?? "";
        if (what.Length == 0)
            return await UpdateEverythingPromptedAsync(config, local, envDir, interactive);
        if (what.Equals("all", StringComparison.OrdinalIgnoreCase))
            return await UpdateEverythingAsync(config, local, envDir);
        if (NodeVersionSpec.IsMatch(what))
            return await UpdateNodeAsync(config, local, envDir, what, settings.Tools, interactive);
        return await UpdateToolAsync(config, local, envDir, what);
    }

    /// <summary>
    /// 'nem update' without arguments: report what could be updated. In an
    /// interactive terminal each newer item is confirmed individually; otherwise
    /// the report is printed with the exact commands to apply the updates.
    /// </summary>
    static async Task<int> UpdateEverythingPromptedAsync(
        NemConfig config, IOPathManager.IOPathManagerLocal local, string envDir, bool interactive)
    {
        UpdatePlan plan = await UpdatePlanner.CreateAsync(config, envDir);
        RenderPlan(plan);

        if (!interactive)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Nothing was changed. To apply updates, run:");
            AnsiConsole.MarkupLine($"  [green]nem update {plan.LatestNodeVersion ?? "<version>"}[/]  - update the Node version");
            AnsiConsole.MarkupLine("  [green]nem update all[/]           - update the Node version and every tool");
            foreach (ToolUpdateEntry tool in plan.Tools.Where(t => t.HasUpdate))
                AnsiConsole.MarkupLine($"  [green]nem update {tool.Name}@{tool.LatestVersion}[/]   - update just that tool");
            AnsiConsole.MarkupLine("Run [green]nem update[/] in an interactive terminal to choose item by item.");
            return 0;
        }

        bool nodeUpdated = false;
        string? newNode = null;
        if (plan.HasNodeUpdate)
        {
            string latestNode = plan.LatestNodeVersion ?? "";
            newNode = latestNode;
            nodeUpdated = AnsiConsole.Confirm(
                $"Update Node {Markup.Escape(plan.DeclaredNodeVersion)} to {Markup.Escape(latestNode)}?",
                defaultValue: true);
        }
        else if (plan.LatestNodeVersion == null)
        {
            AnsiConsole.MarkupLine("[yellow]Could not determine the newest Node version (nodejs.org unreachable?).[/]");
        }

        // Tool "latest" versions depend on the Node version that will be in use.
        UpdatePlan toolPlan = nodeUpdated
            ? await UpdatePlanner.CreateAsync(config, envDir, nodeReference: newNode)
            : plan;

        bool toolsUpdated = false;
        foreach (ToolUpdateEntry tool in toolPlan.Tools)
        {
            if (!tool.HasUpdate)
                continue;

            bool yes = AnsiConsole.Confirm(
                $"Update {Markup.Escape(tool.Name)} from {Markup.Escape(tool.DeclaredVersion)} to {Markup.Escape(tool.LatestVersion!)}?",
                defaultValue: true);
            if (yes)
            {
                NemToolConfig cfgTool = config.Tools.First(t =>
                    string.Equals(t.ToolName, tool.Name, StringComparison.OrdinalIgnoreCase));
                cfgTool.ToolVersion = tool.LatestVersion!;
                toolsUpdated = true;
            }
        }

        if (!nodeUpdated && !toolsUpdated)
        {
            AnsiConsole.MarkupLine("[green]Nothing to update.[/]");
            return 0;
        }

        SaveConfig(local, config);
        AnsiConsole.MarkupLine("[gray]Installing...[/]");
        return await EnvironmentInstaller.InstallAsync(config, envDir, clean: false);
    }

    /// <summary>
    /// 'nem update all': set Node to the newest stable release and every tool to
    /// the newest version that release supports, then install.
    /// </summary>
    static async Task<int> UpdateEverythingAsync(NemConfig config, IOPathManager.IOPathManagerLocal local, string envDir)
    {
        string version;
        try
        {
            version = await NodeDownloadingService.GetLatestStableNodeVersionAsync();
        }
        catch (InvalidOperationException e)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
            return 1;
        }

        if (!string.Equals(config.NodeVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"Node: {config.NodeVersion ?? "(not set)"} -> [green]{version}[/]");
            config.NodeVersion = version;
        }
        else
        {
            AnsiConsole.MarkupLine($"Node: already [green]{version}[/]");
        }

        ApplyLatestTools(config, version, envDir);

        SaveConfig(local, config);
        AnsiConsole.MarkupLine("[gray]Installing...[/]");
        return await EnvironmentInstaller.InstallAsync(config, envDir, clean: false);
    }

    /// <summary>
    /// 'nem update &lt;nodeVersion&gt;': resolve the spec and install it. Tools are
    /// only touched with --tools (or an interactive confirmation).
    /// </summary>
    static async Task<int> UpdateNodeAsync(
        NemConfig config, IOPathManager.IOPathManagerLocal local, string envDir,
        string spec, bool toolsFlag, bool interactive)
    {
        string version;
        try
        {
            version = await NodeDownloadingService.ResolveNodeVersionAsync(spec);
        }
        catch (InvalidOperationException e)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
            return 1;
        }

        string requested = NodeDownloadingService.NormalizeVersion(spec);
        if (!requested.Equals(version, StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[gray]Resolved '{Markup.Escape(requested)}' to Node.js [green]{version}[/] (newest matching release).[/]");

        bool updateTools = toolsFlag;
        if (!updateTools && interactive && config.Tools.Count > 0)
            updateTools = AnsiConsole.Confirm(
                $"Also update all {config.Tools.Count} tool(s) to the newest versions supported by Node {version}?",
                defaultValue: true);

        if (updateTools)
            ApplyLatestTools(config, version, envDir);

        if (!string.Equals(config.NodeVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            config.NodeVersion = version;
            AnsiConsole.MarkupLine($"[gray]Updated {local.ConfigFileName} to Node version [green]{version}[/].[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[gray]The env already declares Node version [green]{version}[/].[/]");
        }

        SaveConfig(local, config);
        AnsiConsole.MarkupLine("[gray]Installing...[/]");
        return await EnvironmentInstaller.InstallAsync(config, envDir, clean: false);
    }

    /// <summary>
    /// 'nem update &lt;package&gt;[@&lt;version&gt;]': update (or add) one tool to the
    /// given version, or to the newest version supported by the declared Node.
    /// </summary>
    static async Task<int> UpdateToolAsync(
        NemConfig config, IOPathManager.IOPathManagerLocal local, string envDir, string spec)
    {
        if (!ToolService.TryParsePackageSpec(spec, out string? name, out string? version) ||
            name == null || !ToolService.IsValidPackageName(name))
        {
            AnsiConsole.MarkupLine(
                $"[red]Invalid tool spec:[/] {Markup.Escape(spec)}. " +
                "Expected a package name, e.g. [green]typescript[@version][/] or [green]@scope/name[@version][/].");
            return 1;
        }

        string? resolved = ToolService.ResolveVersion(name, version, config.NodeVersion ?? "", envDir, out string? error);
        if (resolved == null)
        {
            AnsiConsole.MarkupLine($"[red]Could not update {Markup.Escape(name)}: {Markup.Escape(error ?? "no version could be resolved")}.[/]");
            return 1;
        }

        NemToolConfig? existing = config.Tools.FirstOrDefault(t =>
            string.Equals(t.ToolName, name, StringComparison.OrdinalIgnoreCase));
        string from = existing?.ToolVersion ?? "(not added)";
        AnsiConsole.MarkupLine($"[gray]{Markup.Escape(name)}: {Markup.Escape(from)} -> [green]{resolved}[/][/]");

        if (existing != null)
            existing.ToolVersion = resolved;
        else
            config.Tools.Add(new NemToolConfig { ToolName = name, ToolVersion = resolved });

        SaveConfig(local, config);
        AnsiConsole.MarkupLine("[gray]Installing...[/]");
        return await EnvironmentInstaller.InstallAsync(config, envDir, clean: false);
    }

    /// <summary>
    /// Updates every tool in the config to the newest version supported by
    /// nodeVersion (kept on failure so a bad range never clobbers a working env).
    /// </summary>
    static void ApplyLatestTools(NemConfig config, string nodeVersion, string envDir)
    {
        foreach (NemToolConfig tool in config.Tools)
        {
            string? latest = ToolService.ResolveVersion(tool.ToolName, range: null, nodeVersion, envDir, out string? error);
            if (latest == null)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Keeping {tool.ToolName}@{tool.ToolVersion} ({Markup.Escape(error ?? "no version could be resolved")}).[/]");
                continue;
            }

            if (!string.Equals(latest, tool.ToolVersion, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[gray]{tool.ToolName}: {tool.ToolVersion} -> [green]{latest}[/][/]");
                tool.ToolVersion = latest;
            }
        }
    }

    static void RenderPlan(UpdatePlan plan)
    {
        var table = new Table()
            .AddColumn("[bold]Item[/]")
            .AddColumn("[bold]Declared[/]")
            .AddColumn("[bold]Installed[/]")
            .AddColumn("[bold]Latest supported[/]");

        table.AddRow(
            "[bold]Node[/]",
            plan.DeclaredNodeVersion == "" ? "(not set)" : plan.DeclaredNodeVersion,
            plan.InstalledNodeVersion ?? "[yellow]not installed[/]",
            LatestCell(plan.HasNodeUpdate, plan.LatestNodeVersion));

        foreach (ToolUpdateEntry tool in plan.Tools)
            table.AddRow(
                tool.Name,
                tool.DeclaredVersion,
                tool.InstalledVersion ?? "[yellow]not installed[/]",
                LatestCell(tool.HasUpdate, tool.LatestVersion));

        AnsiConsole.Write(table);
    }

    static string LatestCell(bool hasUpdate, string? latest)
    {
        if (latest == null)
            return "[yellow]unknown[/]";
        return hasUpdate ? $"[red]{latest}[/]" : $"[green]{latest}[/]";
    }

    static NemConfig LoadConfig(IOPathManager.IOPathManagerLocal local) =>
        JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(local.ConfigFilePath)) ?? new NemConfig();

    static void SaveConfig(IOPathManager.IOPathManagerLocal local, NemConfig config) =>
        File.WriteAllText(local.ConfigFilePath,
            JsonConvert.SerializeObject(config, Formatting.Indented) + Environment.NewLine);
}
