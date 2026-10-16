using Semver;

namespace nem.Services;

/// <summary>
/// npm's semver rules for versions and ranges. The single place nem depends on
/// the Semver package, whose npm parser follows node-semver (^, ~, ||, x-ranges,
/// hyphen ranges and npm's prerelease handling).
/// </summary>
public static class NpmSemver
{
    /// <summary>An exact version ("1.2.3", "1.2.3-beta.1"), or null when it is not one.</summary>
    public static SemVersion? ParseVersion(string text) =>
        SemVersion.TryParse(text, SemVersionStyles.Strict, out SemVersion? version) ? version : null;

    /// <summary>
    /// An npm range, or null when it is not one. <paramref name="includePrerelease"/>
    /// mirrors node-semver's includePrerelease option: when false, a prerelease only
    /// matches a range that names a prerelease of the same major.minor.patch.
    /// </summary>
    public static SemVersionRange? ParseRange(string text, bool includePrerelease) =>
        SemVersionRange.TryParseNpm(text, includePrerelease, out SemVersionRange? range) ? range : null;
}
