using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace nem.Services;

/// <summary>
/// Reads the command names a package ships from its "bin" field - the same field
/// in an installed package.json and in a registry document, so nem knows a tool's
/// commands before installing it.
/// </summary>
public static class ToolBins
{
    /// <summary>
    /// The command names in <paramref name="bin"/>: an object maps each command to
    /// its script, a string means one command named after the package. Empty when
    /// the package ships no command.
    /// </summary>
    public static IReadOnlyList<string> FromBinField(string packageName, JToken? bin) => bin?.Type switch
    {
        JTokenType.Object => ((JObject)bin).Properties().Select(p => p.Name).Where(name => name.Length > 0).Distinct().ToList(),
        JTokenType.String when !string.IsNullOrWhiteSpace(bin.ToString()) && DefaultBinName(packageName).Length > 0 => [DefaultBinName(packageName)],
        _ => [],
    };

    /// <summary>The command a string "bin" installs: the package name without its scope.</summary>
    public static string DefaultBinName(string packageName) =>
        packageName.Contains('/') ? packageName[(packageName.LastIndexOf('/') + 1)..] : packageName;
}
