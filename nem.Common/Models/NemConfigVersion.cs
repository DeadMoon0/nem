namespace nem.Common.Models;

/// <summary>
/// The schema versions of the config file. This is the file format's own version,
/// not the nem package version: it only goes up when the shape of the file changes,
/// so a nem release that leaves the format alone keeps writing the same number.
/// </summary>
public static class NemConfigVersion
{
    /// <summary>
    /// The schema of a file that does not state a version. Versioning arrived after
    /// the format already existed, so an absent version means this one - never the
    /// current one, or an old file would claim to be something it is not.
    /// </summary>
    public const int Initial = 1;

    /// <summary>
    /// The schema this nem writes, and the highest one it can read. A file above it
    /// was written by a newer nem and is refused rather than guessed at.
    /// </summary>
    public const int Current = 1;
}
