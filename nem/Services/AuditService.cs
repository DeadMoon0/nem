using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>One advisory from the registry, for one installed package.</summary>
    public sealed record Advisory(string Severity, string Name, IReadOnlyList<string> Versions, string Title, string Url);

    /// <summary>Why an audit produced no advisory list.</summary>
    public enum AuditOutcome
    {
        /// <summary>The env has no installed tools to audit.</summary>
        NothingToAudit,
        /// <summary>The registry could not be reached, or refused the request.</summary>
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
                AnsiConsole.MarkupLine("[gray]Security audit skipped (registry unreachable).[/]");
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

        IReadOnlyDictionary<string, IReadOnlyList<string>> installed = InstalledPackageScanner.Scan(modulesRoot);
        if (installed.Count == 0)
            return new AuditReport(AuditOutcome.NothingToAudit, 0, []);

        var registry = new NpmRegistryClient(ToolService.RunNpmCapture(envDir, new[] { "config", "get", "registry" }));
        return registry.FetchBulkAdvisories(installed).Match(
            ok: advisories => new AuditReport(AuditOutcome.Completed, installed.Count, MatchAdvisories(installed, advisories)),
            failed: (failure, _) => new AuditReport(
                failure == RegistryFailure.InvalidResponse ? AuditOutcome.InvalidResponse : AuditOutcome.Unavailable, 0, []));
    }

    /// <summary>
    /// Pairs the registry's advisories with the installed versions they affect,
    /// worst first.
    /// </summary>
    internal static IReadOnlyList<Advisory> MatchAdvisories(
        IReadOnlyDictionary<string, IReadOnlyList<string>> installed,
        IReadOnlyDictionary<string, IReadOnlyList<RegistryAdvisory>> advisories) =>
        advisories
            .SelectMany(package => package.Value.Select(a => new Advisory(
                a.Severity,
                package.Key,
                AffectedVersions(installed.GetValueOrDefault(package.Key) ?? [], a.VulnerableVersions),
                a.Title,
                a.Url)))
            .OrderByDescending(a => RankOf(a.Severity))
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The installed versions inside the advisory's range. Conservative: when the
    /// range is missing or unreadable, or matches none of them (the registry only
    /// answers for affected packages), every installed version is reported.
    /// </summary>
    static IReadOnlyList<string> AffectedVersions(IReadOnlyList<string> installed, string vulnerableVersions)
    {
        if (string.IsNullOrWhiteSpace(vulnerableVersions) || NpmSemver.ParseRange(vulnerableVersions, includePrerelease: false) is not { } range)
            return installed;

        // A version nem cannot read is kept: better a false alarm than a missed one.
        List<string> affected = installed
            .Where(v => NpmSemver.ParseVersion(v) is not { } version || range.Contains(version))
            .ToList();
        return affected.Count > 0 ? affected : installed;
    }
}
