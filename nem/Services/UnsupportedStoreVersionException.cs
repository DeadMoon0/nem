using System;

namespace nem.Services;

/// <summary>
/// A store whose format is newer than this nem knows. Refusing it is the point of
/// versioning the store: a nem that cannot know what a newer layout means must say
/// so, instead of taking entries it does not recognise for missing ones.
/// </summary>
public class UnsupportedStoreVersionException : Exception
{
    public UnsupportedStoreVersionException(int storeVersion, int supportedVersion, string storeDir)
        : base($"The store at '{storeDir}' has format version {storeVersion}, but this nem only knows up to version {supportedVersion}. "
             + "Update nem with 'dotnet tool update -g nem' to use it.")
    {
        StoreVersion = storeVersion;
    }

    /// <summary>The format the store declares.</summary>
    public int StoreVersion { get; }
}
