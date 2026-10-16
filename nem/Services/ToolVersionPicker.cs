using System;
using System.Collections.Generic;
using System.Linq;
using Semver;

namespace nem.Services;

/// <summary>The outcome of resolving a tool to an exact version.</summary>
public abstract record VersionPick
{
    private VersionPick() { }

    /// <summary>The exact version to install.</summary>
    public sealed record Picked(string Version) : VersionPick;

    /// <summary>No version could be chosen; <see cref="Reason"/> says why, for the user.</summary>
    public sealed record Unresolved(string Reason) : VersionPick;
}

/// <summary>
/// Chooses the exact version of a tool from its packument, the way 'nem tool add'
/// and 'nem update' promise:
///  - an exact published version -> that version
///  - a dist-tag (latest, next, ...) -> the version it points to
///  - an npm range -> the newest stable version inside it
///  - nothing requested -> the newest stable version whose engines.node accepts
///    the env's Node, then the newest such prerelease, then the newest version at all
/// An explicit request that matches nothing is unresolved: silently falling back to
/// another version would install the wrong package.
/// </summary>
public static class ToolVersionPicker
{
    private sealed record Candidate(string Text, SemVersion Version, string? EnginesNode);

    public static VersionPick Pick(string packageName, Packument packument, string nodeVersion, string? requested)
    {
        List<Candidate> all = packument.Versions
            .Select(v => NpmSemver.ParseVersion(v.Key) is { } parsed ? new Candidate(v.Key, parsed, v.Value.EnginesNode) : null)
            .OfType<Candidate>()
            .ToList();
        List<Candidate> stable = all.Where(c => !c.Version.IsPrerelease).ToList();

        return string.IsNullOrWhiteSpace(requested)
            ? PickForNode(packageName, all, stable, nodeVersion)
            : PickRequested(packageName, packument, all, stable, requested.Trim());
    }

    static VersionPick PickRequested(string packageName, Packument packument, List<Candidate> all, List<Candidate> stable, string requested)
    {
        if (all.Any(c => c.Text == requested))
            return new VersionPick.Picked(requested);
        if (packument.DistTags.TryGetValue(requested, out string? tagged))
            return new VersionPick.Picked(tagged);

        SemVersionRange? range = NpmSemver.ParseRange(requested, includePrerelease: false);
        Candidate? newest = range == null ? null : Newest(stable.Where(c => range.Contains(c.Version)));
        return newest != null
            ? new VersionPick.Picked(newest.Text)
            : new VersionPick.Unresolved($"no version of {packageName} matches '{requested}'");
    }

    static VersionPick PickForNode(string packageName, List<Candidate> all, List<Candidate> stable, string nodeVersion)
    {
        SemVersion? node = NpmSemver.ParseVersion(nodeVersion);
        Candidate? newest = Newest(stable.Where(c => SupportsNode(c, node)))
                            ?? Newest(all.Where(c => SupportsNode(c, node)))
                            ?? Newest(stable)
                            ?? Newest(all);
        return newest != null
            ? new VersionPick.Picked(newest.Text)
            : new VersionPick.Unresolved($"{packageName} has no published versions");
    }

    // A release without engines.node runs anywhere. A range that cannot be read, or
    // an env Node that is not a version, counts as unsupported, as npm treats it.
    static bool SupportsNode(Candidate candidate, SemVersion? node)
    {
        if (string.IsNullOrWhiteSpace(candidate.EnginesNode))
            return true;
        if (node == null)
            return false;
        SemVersionRange? range = NpmSemver.ParseRange(candidate.EnginesNode, includePrerelease: true);
        return range != null && range.Contains(node);
    }

    static Candidate? Newest(IEnumerable<Candidate> candidates) =>
        candidates.OrderByDescending(c => c.Version, SemVersion.PrecedenceComparer).FirstOrDefault();
}
