using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nem.Services;

/// <summary>One published version of a package, as far as version resolution cares.</summary>
/// <param name="EnginesNode">The release's engines.node range, or null when it declares none.</param>
/// <param name="Bins">The commands the release ships (its "bin" field), empty when none.</param>
public sealed record PackumentVersion(string? EnginesNode, IReadOnlyList<string> Bins);

/// <summary>
/// The parts of a registry package document ("packument") that nem reads: every
/// published version (its Node range and commands) and the dist-tags.
/// </summary>
public sealed record Packument(
    IReadOnlyDictionary<string, PackumentVersion> Versions,
    IReadOnlyDictionary<string, string> DistTags)
{
    /// <summary>
    /// Reads a full or abbreviated packument. Returns null when the text is not a
    /// JSON object.
    /// </summary>
    public static Packument? Parse(string json)
    {
        JObject root;
        try
        {
            if (JToken.Parse(json) is not JObject parsed)
                return null;
            root = parsed;
        }
        catch (JsonException)
        {
            return null;
        }

        string packageName = StringOrNull(root["name"]) ?? "";
        var versions = new Dictionary<string, PackumentVersion>(StringComparer.Ordinal);
        if (root["versions"] is JObject versionsObject)
        {
            foreach (JProperty version in versionsObject.Properties())
            {
                var release = version.Value as JObject;
                // engines is an array in some very old packages; only an object carries a node range.
                JToken? enginesNode = (release?["engines"] as JObject)?["node"];
                versions[version.Name] = new PackumentVersion(StringOrNull(enginesNode), ToolBins.FromBinField(packageName, release?["bin"]));
            }
        }

        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["dist-tags"] is JObject tagsObject)
        {
            foreach (JProperty tag in tagsObject.Properties())
            {
                if (StringOrNull(tag.Value) is { } target)
                    tags[tag.Name] = target;
            }
        }

        return new Packument(versions, tags);
    }

    static string? StringOrNull(JToken? token) =>
        token?.Type == JTokenType.String ? token.Value<string>() : null;
}
