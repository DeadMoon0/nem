using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// What the audit reports for an advisory: the installed versions it actually
/// affects, and when in doubt every installed version rather than none.
/// </summary>
public class AuditMatchingTests
{
    static IReadOnlyList<AuditService.Advisory> Match(string package, string[] installed, params RegistryAdvisory[] advisories) =>
        AuditService.MatchAdvisories(
            new Dictionary<string, IReadOnlyList<string>> { [package] = installed },
            new Dictionary<string, IReadOnlyList<RegistryAdvisory>> { [package] = advisories });

    static RegistryAdvisory Advisory(string range, string severity = "high") =>
        new(severity, "Prototype pollution", "https://github.com/advisories/GHSA-test", range);

    [Fact]
    public void Only_installed_versions_inside_the_vulnerable_range_are_reported()
    {
        var result = Match("minimist", ["1.2.5", "1.2.8"], Advisory("<1.2.6"));

        Assert.Equal(["1.2.5"], Assert.Single(result).Versions);
    }

    [Fact]
    public void An_advisory_without_a_range_reports_every_installed_version()
    {
        var result = Match("minimist", ["1.2.5", "1.2.8"], Advisory(""));

        Assert.Equal(["1.2.5", "1.2.8"], Assert.Single(result).Versions);
    }

    [Fact]
    public void An_advisory_whose_range_matches_no_installed_version_still_reports_them_all()
    {
        var result = Match("minimist", ["1.2.8"], Advisory("<1.2.6"));

        Assert.Equal(["1.2.8"], Assert.Single(result).Versions);
    }

    [Fact]
    public void The_worst_severity_is_reported_first()
    {
        var result = Match("minimist", ["1.2.5"], Advisory("<2", "low"), Advisory("<2", "critical"), Advisory("<2", "moderate"));

        Assert.Equal(["critical", "moderate", "low"], result.Select(a => a.Severity));
    }

    [Fact]
    public void A_package_several_tools_carry_is_audited_in_every_version_they_carry()
    {
        var merged = AuditService.Merge(
        [
            new Dictionary<string, IReadOnlyList<string>> { ["minimist"] = ["1.2.5"], ["semver"] = ["7.6.0"] },
            new Dictionary<string, IReadOnlyList<string>> { ["minimist"] = ["1.2.8", "1.2.5"] },
        ]);

        Assert.Equal(["1.2.5", "1.2.8"], merged["minimist"]);
        Assert.Equal(["7.6.0"], merged["semver"]);
    }
}
