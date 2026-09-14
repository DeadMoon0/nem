using nem.Common.Models;
using System;

namespace nem.Services;

/// <summary>
/// A config file whose schema is newer than this nem understands. Refusing it is the
/// point of versioning the file: a nem that cannot know what a newer field means must
/// say so, instead of silently reading a config it only half understands and then
/// writing it back with the parts it dropped.
/// </summary>
public class UnsupportedConfigVersionException : Exception
{
    public UnsupportedConfigVersionException(int fileVersion, string? configFilePath = null)
        : base(BuildMessage(fileVersion, configFilePath))
    {
        FileVersion = fileVersion;
        ConfigFilePath = configFilePath;
    }

    /// <summary>The schema the file declares.</summary>
    public int FileVersion { get; }

    /// <summary>The highest schema this nem can read.</summary>
    public int SupportedVersion => NemConfigVersion.Current;

    /// <summary>The file it came from, when it was read from one.</summary>
    public string? ConfigFilePath { get; }

    /// <summary>
    /// The message carries the way out as well as the problem, because this is the
    /// only place that knows both. Nothing above needs to recognize the type to say
    /// something useful about it.
    /// </summary>
    static string BuildMessage(int fileVersion, string? configFilePath)
    {
        string what = configFilePath == null ? "That nem config" : $"'{configFilePath}'";
        return $"{what} uses config schema version {fileVersion}, but this nem only reads up to version {NemConfigVersion.Current}. "
             + "Update nem with 'dotnet tool update -g nem' to read it.";
    }
}
