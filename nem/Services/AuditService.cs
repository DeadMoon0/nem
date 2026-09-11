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

    /// <summary>One advisory from the registry, for one installed package.</summary>
    public sealed record Advisory(string Severity, string Name, IReadOnlyList<string> Versions, string Title, string Url);

    /// <summary>Why an audit produced no advisory list.</summary>
    public enum AuditOutcome
    {
        /// <summary>The env has no installed tools to audit.</summary>
        NothingToAudit,
        /// <summary>The registry could not be reached, or no node was available.</summary>
        Unavailable,
        /// <summary>The registry answered with something that is not a report.</summary>
        InvalidResponse,
        /// <summary>The audit ran; <see cref="AuditReport.Advisories"/> holds the result.</summary>
        Completed,
    }

    /// <summary>The result of auditing an env, worst advisories first.</summary>
    public sealed record AuditReport(AuditOutcome Outcome, int Packages, IReadOnlyList<Advisory> Advisories);

    /// <summary>
    /// Audits the env and prints the headline only: how many vulnerabilities of
    /// which severity, plus where to see them. Used by 'nem install', where a full
    /// advisory table would bury the install's own output.
    /// </summary>
    public static void ReportSummary(string envDir)
    {
        AuditReport report = Audit(envDir);
        ReportHeadline(report);

        if (report.Outcome == AuditOutcome.Completed && report.Advisories.Count > 0)
            AnsiConsole.MarkupLine("Run [green]nem audit[/] to see them.");
    }

    /// <summary>
    /// Audits the env and prints the headline plus every advisory. Used by
    /// 'nem audit', where the details are the point.
    /// </summary>
    public static void ReportDetails(string envDir)
    {
        AuditReport report = Audit(envDir);
        ReportHeadline(report);

        if (report.Outcome != AuditOutcome.Completed || report.Advisories.Count == 0)
            return;

        var table = new Table { Border = TableBorder.Rounded, ShowHeaders = false };
        table.AddColumn(new TableColumn("Severity"));
        table.AddColumn(new TableColumn("Package"));
        table.AddColumn(new TableColumn("Installed"));
        table.AddColumn(new TableColumn("Advisory"));

        foreach (Advisory advisory in report.Advisories)
        {
            table.AddRow(
                $"[{ColorOf(advisory.Severity)}]{advisory.Severity}[/]",
                advisory.Name,
                string.Join(", ", advisory.Versions),
                advisory.Title);
        }
        AnsiConsole.Write(table);

        foreach (string url in report.Advisories.Select(a => a.Url).Where(u => !string.IsNullOrEmpty(u)).Distinct())
            AnsiConsole.MarkupLine($"[gray]{url}[/]");
    }

    /// <summary>
    /// The one-line verdict shared by both reports.
    /// </summary>
    static void ReportHeadline(AuditReport report)
    {
        switch (report.Outcome)
        {
            case AuditOutcome.NothingToAudit:
                return;
            case AuditOutcome.Unavailable:
                AnsiConsole.MarkupLine("[gray]Security audit skipped (registry unreachable or no node available).[/]");
                return;
            case AuditOutcome.InvalidResponse:
                AnsiConsole.MarkupLine("[gray]Security audit skipped (invalid response from registry).[/]");
                return;
        }

        if (report.Advisories.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]Security audit passed: no known vulnerabilities ({report.Packages} packages audited).[/]");
            return;
        }

        string color = report.Advisories.Any(a => a.Severity == "critical" || a.Severity == "high") ? "red" : "yellow";
        string countText = string.Join(", ",
            Severities.Where(s => report.Advisories.Any(a => a.Severity == s.Key))
                .OrderByDescending(s => s.Value.Rank)
                .Select(s => $"{report.Advisories.Count(a => a.Severity == s.Key)} {s.Key}"));
        AnsiConsole.MarkupLine(
            $"[{color}]Security audit found {report.Advisories.Count} known vulnerability(ies) ({countText}) in {report.Packages} packages.[/]");
    }

    /// <summary>
    /// Audits the env's installed tool tree against the registry's advisory
    /// endpoint. Never throws: every failure comes back as an outcome.
    /// </summary>
    public static AuditReport Audit(string envDir)
    {
        string modulesRoot = ToolService.ToolModulesRoot(envDir);
        if (!Directory.Exists(modulesRoot))
            return new AuditReport(AuditOutcome.NothingToAudit, 0, []);

        string registry = ToolService.RunNpmCapture(envDir, new[] { "config", "get", "registry" })
                           ?? "https://registry.npmjs.org";
        if (!Uri.TryCreate(registry, UriKind.Absolute, out Uri? uri) || uri.IsFile)
            registry = "https://registry.npmjs.org";

        string? report = RunAuditScript(envDir, modulesRoot, registry);
        if (report == null)
            return new AuditReport(AuditOutcome.Unavailable, 0, []);

        JToken? root;
        try
        {
            root = JToken.Parse(report);
        }
        catch (Exception)
        {
            return new AuditReport(AuditOutcome.InvalidResponse, 0, []);
        }

        int packages = (int)(root?["packages"] ?? 0);
        if (packages == 0)
            return new AuditReport(AuditOutcome.NothingToAudit, 0, []);

        var advisories = ((root?["advisories"] as JArray)?.ToList() ?? [])
            .Select(t => new Advisory(
                Str(t?["severity"]) ?? "unknown",
                Str(t?["name"]) ?? "?",
                (t?["versions"] as JArray)?.Select(v => v.ToString() ?? string.Empty).ToList() ?? [],
                Str(t?["title"]) ?? "Unknown advisory",
                Str(t?["url"]) ?? string.Empty))
            .OrderByDescending(a => RankOf(a.Severity))
            .ThenBy(a => a.Name)
            .ToList();

        return new AuditReport(AuditOutcome.Completed, packages, advisories);
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
