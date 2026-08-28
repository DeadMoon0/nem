using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace nem.Services;

/// <summary>
/// Runs a security audit over the env's installed tool tree using the npm
/// registry's bulk advisory endpoint, and renders the result. Best effort:
/// any failure is reported as a gray note and never fails the install.
/// </summary>
public static class AuditService
{
    // severity -> (rank, color). Higher rank = worse.
    private static readonly Dictionary<string, (int Rank, string Color)> Severities = new()
    {
        ["critical"] = (5, "red"),
        ["high"] = (4, "red"),
        ["moderate"] = (3, "yellow"),
        ["low"] = (2, "dark_yellow"),
        ["info"] = (1, "gray"),
    };

    private static int RankOf(string severity) =>
        Severities.TryGetValue(severity, out var s) ? s.Rank : 0;

    private static string ColorOf(string severity) =>
        Severities.TryGetValue(severity, out var s) ? s.Color : "gray";

    private static string? Str(JToken? token) =>
        token is JValue value ? value.ToString() : null;

    /// <summary>
    /// Audits the env and prints the report. Does nothing when no tools are installed.
    /// </summary>
    public static void AuditAndReport(string envDir)
    {
        string modulesRoot = ToolService.ToolModulesRoot(envDir);
        if (!Directory.Exists(modulesRoot))
            return;

        string registry = ToolService.RunNpmCapture(envDir, new[] { "config", "get", "registry" })
                           ?? "https://registry.npmjs.org";
        if (!Uri.TryCreate(registry, UriKind.Absolute, out Uri? uri) || uri.IsFile)
            registry = "https://registry.npmjs.org";

        string? report = RunAuditScript(envDir, modulesRoot, registry);
        if (report == null)
        {
            AnsiConsole.MarkupLine("[gray]Security audit skipped (registry unreachable or no node available).[/]");
            return;
        }

        JToken? root;
        try
        {
            root = JToken.Parse(report);
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine("[gray]Security audit skipped (invalid response from registry).[/]");
            return;
        }

        int packages = (int)(root?["packages"] ?? 0);
        if (packages == 0)
            return;

        var advisories = (root?["advisories"] as JArray)?.ToList() ?? new List<JToken>();
        if (advisories.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]Security audit passed: no known vulnerabilities ({packages} packages audited).[/]");
            return;
        }

        var ordered = advisories
            .Select(t => (
                Severity: Str(t?["severity"]) ?? "unknown",
                Name: Str(t?["name"]) ?? "?",
                Versions: (t?["versions"] as JArray)?.Select(v => v.ToString() ?? string.Empty).ToList() ?? new List<string>(),
                Title: Str(t?["title"]) ?? "Unknown advisory",
                Url: Str(t?["url"]) ?? string.Empty))
            .OrderByDescending(a => RankOf(a.Severity))
            .ThenBy(a => a.Name)
            .ToList();

        string color = ordered.Any(a => a.Severity == "critical" || a.Severity == "high") ? "red" : "yellow";
        string countText = string.Join(", ",
            Severities.Where(s => ordered.Any(a => a.Severity == s.Key))
                .OrderByDescending(s => s.Value.Rank)
                .Select(s => $"{ordered.Count(a => a.Severity == s.Key)} {s.Key}"));
        AnsiConsole.MarkupLine($"[{color}]Security audit found {ordered.Count} known vulnerability(ies) ({countText}) in {packages} packages.[/]");

        var table = new Table { Border = TableBorder.Rounded, ShowHeaders = false };
        table.AddColumn(new TableColumn("Severity"));
        table.AddColumn(new TableColumn("Package"));
        table.AddColumn(new TableColumn("Installed"));
        table.AddColumn(new TableColumn("Advisory"));

        foreach (var advisory in ordered.Take(15))
        {
            string sevColor = ColorOf(advisory.Severity);
            table.AddRow(
                $"[{sevColor}]{advisory.Severity}[/]",
                advisory.Name,
                string.Join(", ", advisory.Versions),
                advisory.Title);
        }
        AnsiConsole.Write(table);

        if (ordered.Count > 15)
            AnsiConsole.MarkupLine($"[gray]... and {ordered.Count - 15} more advisory(ies).[/]");

        foreach (string url in ordered.Select(a => a.Url).Where(u => !string.IsNullOrEmpty(u)).Distinct().Take(5))
            AnsiConsole.MarkupLine($"[gray]{url}[/]");
    }

    /// <summary>
    /// Runs Resources/AuditEnv.js under the env's node; returns the JSON report or null.
    /// </summary>
    private static string? RunAuditScript(string envDir, string modulesRoot, string registry)
    {
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
        psi.ArgumentList.Add(ToolService.LoadResourceScript("AuditEnv.js"));
        psi.ArgumentList.Add(modulesRoot);
        psi.ArgumentList.Add(registry);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return null;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(120_000))
            {
                process.Kill();
                return null;
            }
            if (process.ExitCode != 0)
                return null;
            return output.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
