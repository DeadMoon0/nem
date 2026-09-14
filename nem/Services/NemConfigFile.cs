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
         *
         * "Version" is the config schema version. nem maintains it - leave it alone.
         */
        """.ReplaceLineEndings();

    /// <summary>
    /// The config a file's content declares. Comments are skipped, and content that
    /// holds nothing at all is an empty config, the same as "{}". A config from a
    /// schema this nem does not know is refused rather than half-read.
    /// </summary>
    /// <exception cref="UnsupportedConfigVersionException">
    /// The content declares a schema newer than <see cref="NemConfigVersion.Current"/>.
    /// </exception>
    public static NemConfig Parse(string content)
    {
        NemConfig config = string.IsNullOrWhiteSpace(content)
            ? new NemConfig()
            : JsonConvert.DeserializeObject<NemConfig>(content) ?? new NemConfig();

        if (config.Version > NemConfigVersion.Current)
            throw new UnsupportedConfigVersionException(config.Version);

        // An older schema is brought up to the current one here, before the config
        // reaches any caller, so the rest of nem only ever sees the current shape.
        // Version 1 is still the only schema, so there is nothing to migrate yet.
        return config;
    }

    /// <summary>
    /// The text a config file gets, for the config exactly as given - including its
    /// <see cref="NemConfig.Version"/>, which <see cref="Write"/> stamps beforehand.
    /// The header only goes into a file that may hold comments, so serializing for a
    /// '.json' target still yields plain JSON.
    /// </summary>
    public static string Serialize(NemConfig config, bool withHeader)
    {
        string json = JsonConvert.SerializeObject(config, Formatting.Indented);
        return (withHeader ? Header + Environment.NewLine + json : json) + Environment.NewLine;
    }

    /// <summary>True for a config file that may hold comments, i.e. the '.jsonc' one.</summary>
    public static bool AllowsComments(string configFilePath) =>
        string.Equals(Path.GetExtension(configFilePath), ".jsonc", StringComparison.OrdinalIgnoreCase);

    /// <exception cref="UnsupportedConfigVersionException">
    /// The file declares a schema newer than <see cref="NemConfigVersion.Current"/>.
    /// </exception>
    public static NemConfig Read(string configFilePath)
    {
        try
        {
            return Parse(File.ReadAllText(configFilePath));
        }
        catch (UnsupportedConfigVersionException e)
        {
            // Parse only sees the text; name the file the user has to look at.
            throw new UnsupportedConfigVersionException(e.FileVersion, configFilePath);
        }
    }

    /// <summary>
    /// Writes the config, stamped with the schema nem is writing. Stamping here and
    /// not in <see cref="Serialize"/> keeps the object and the file in agreement:
    /// after a save, the config in memory says what is actually on disk.
    /// </summary>
    public static void Write(string configFilePath, NemConfig config)
    {
        config.Version = NemConfigVersion.Current;
        File.WriteAllText(configFilePath, Serialize(config, AllowsComments(configFilePath)));
    }
}
