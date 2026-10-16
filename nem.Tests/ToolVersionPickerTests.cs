using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The version a tool resolves to when it is added or updated: an explicit
/// request is honoured exactly, and without one the newest release that runs on
/// the env's Node wins.
/// </summary>
public class ToolVersionPickerTests
{
    static Packument Package(Dictionary<string, string?> versions, Dictionary<string, string>? tags = null) =>
        new(versions.ToDictionary(v => v.Key, v => new PackumentVersion(v.Value)), tags ?? new Dictionary<string, string>());

    static string? Picked(VersionPick pick) => (pick as VersionPick.Picked)?.Version;

    // Engines ranges as @angular/cli publishes them.
    static readonly Packument AngularCli = Package(new()
    {
        ["15.2.11"] = "^14.20.0 || ^16.13.0 || ^18.10.0",
        ["19.2.27"] = "^18.19.1 || ^20.11.1 || >=22.0.0",
        ["20.3.0"] = "^20.19.0 || ^22.12.0 || >=24.0.0",
        ["21.0.0-next.1"] = "^20.19.0 || ^22.12.0 || >=24.0.0",
    }, new() { ["latest"] = "20.3.0", ["next"] = "21.0.0-next.1" });

    [Fact]
    public void An_exact_version_is_taken_even_when_its_engines_exclude_the_env_node()
    {
        Assert.Equal("15.2.11", Picked(ToolVersionPicker.Pick("@angular/cli", AngularCli, "22.12.0", "15.2.11")));
    }

    [Fact]
    public void Without_a_version_the_newest_release_supporting_the_env_node_is_taken()
    {
        Assert.Equal("19.2.27", Picked(ToolVersionPicker.Pick("@angular/cli", AngularCli, "18.20.8", null)));
    }

    [Fact]
    public void Without_a_version_on_a_current_node_the_newest_stable_release_is_taken()
    {
        Assert.Equal("20.3.0", Picked(ToolVersionPicker.Pick("@angular/cli", AngularCli, "24.1.0", null)));
    }

    [Fact]
    public void A_node_older_than_every_alternative_of_an_engines_range_is_not_supported()
    {
        // 20.15.0 matches none of 20.3.0's '^20.19.0 || ^22.12.0 || >=24.0.0', but 19.2.27's '^20.11.1'.
        Assert.Equal("19.2.27", Picked(ToolVersionPicker.Pick("@angular/cli", AngularCli, "20.15.0", null)));
    }

    [Fact]
    public void A_dist_tag_resolves_to_the_version_it_points_to()
    {
        Assert.Equal("21.0.0-next.1", Picked(ToolVersionPicker.Pick("@angular/cli", AngularCli, "22.12.0", "next")));
    }

    [Fact]
    public void A_range_resolves_to_the_newest_stable_version_inside_it()
    {
        var typescript = Package(new() { ["5.4.5"] = null, ["5.6.3"] = null, ["5.7.0-beta"] = null, ["6.0.2"] = null });

        Assert.Equal("5.6.3", Picked(ToolVersionPicker.Pick("typescript", typescript, "22.12.0", "^5.4.0")));
    }

    [Fact]
    public void A_range_that_matches_nothing_is_an_error_not_a_fallback()
    {
        VersionPick pick = ToolVersionPicker.Pick("@angular/cli", AngularCli, "22.12.0", "^99.0.0");

        var unresolved = Assert.IsType<VersionPick.Unresolved>(pick);
        Assert.Contains("^99.0.0", unresolved.Reason);
    }

    [Fact]
    public void A_release_without_engines_runs_on_any_node()
    {
        var tool = Package(new() { ["1.0.0"] = ">=18", ["2.0.0"] = null });

        Assert.Equal("2.0.0", Picked(ToolVersionPicker.Pick("tool", tool, "16.20.2", null)));
    }

    [Fact]
    public void A_prerelease_is_only_taken_when_no_stable_release_supports_the_env_node()
    {
        var tool = Package(new() { ["1.0.0"] = ">=24", ["2.0.0-rc.1"] = ">=20" });

        Assert.Equal("2.0.0-rc.1", Picked(ToolVersionPicker.Pick("tool", tool, "22.12.0", null)));
    }

    [Fact]
    public void When_no_release_supports_the_env_node_the_newest_stable_release_is_taken()
    {
        var tool = Package(new() { ["1.0.0"] = ">=24", ["2.0.0"] = ">=26", ["3.0.0-rc.1"] = ">=26" });

        Assert.Equal("2.0.0", Picked(ToolVersionPicker.Pick("tool", tool, "18.20.8", null)));
    }

    [Fact]
    public void A_package_without_published_versions_does_not_resolve()
    {
        Assert.IsType<VersionPick.Unresolved>(ToolVersionPicker.Pick("empty", Package(new()), "22.12.0", null));
    }
}
