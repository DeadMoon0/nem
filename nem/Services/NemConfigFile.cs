using nem.Common.Models;
using Newtonsoft.Json;
using System;
using System.IO;

namespace nem.Services;

/// <summary>
/// The project config file, in the one place that knows its format. It is hand-edited
/// project state, so it may carry comments - Newtonsoft skips those on read, and a
/// new '.jsonc' file is created with a header explaining what the file is. A '.json'
/// file never gets that header: a comment would make it invalid JSON for every other
/// tool that reads it.
/// </summary>
public static class NemConfigFile
{
    /// <summary>
    /// The banner a new '.jsonc' config is created with. Normalized to this machine's
    /// line endings so the written file does not depend on how this source file was
    /// checked out.
    /// </summary>
    public static string Header { get; } =
        """
        /*
         * nem - Node Environment Manager
         * https://github.com/DeadMoon0/NEM
         *
         * Declares the Node.js version and the npm tools this project needs.
         * Get nem with "dotnet tool install -g nem", then run "nem install"
         * after cloning to reproduce the exact same environment.
         *
         * Comments are allowed in this file, but nem rewrites it on "nem update"
         * and "nem tool", and only this header survives that.
         */
        """.ReplaceLineEndings();

    /// <summary>
    /// The config a file's content declares. Comments are skipped, and content that
    /// holds nothing at all is an empty config, the same as "{}".
    /// </summary>
    public static NemConfig Parse(string content) =>
        string.IsNullOrWhiteSpace(content)
            ? new NemConfig()
            : JsonConvert.DeserializeObject<NemConfig>(content) ?? new NemConfig();

    /// <summary>
    /// The text a config file gets. The header only goes into a file that may hold
    /// comments, so serializing for a '.json' target still yields plain JSON.
    /// </summary>
    public static string Serialize(NemConfig config, bool withHeader)
    {
        string json = JsonConvert.SerializeObject(config, Formatting.Indented);
        return (withHeader ? Header + Environment.NewLine + json : json) + Environment.NewLine;
    }

    /// <summary>True for a config file that may hold comments, i.e. the '.jsonc' one.</summary>
    public static bool AllowsComments(string configFilePath) =>
        string.Equals(Path.GetExtension(configFilePath), ".jsonc", StringComparison.OrdinalIgnoreCase);

    public static NemConfig Read(string configFilePath) =>
        Parse(File.ReadAllText(configFilePath));

    public static void Write(string configFilePath, NemConfig config) =>
        File.WriteAllText(configFilePath, Serialize(config, AllowsComments(configFilePath)));
}
