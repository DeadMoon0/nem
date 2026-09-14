namespace nem.Common.Models;

public class NemConfig
{
    /// <summary>
    /// The schema this config was read from. It defaults to
    /// <see cref="NemConfigVersion.Initial"/> rather than to
    /// <see cref="NemConfigVersion.Current"/> on purpose: a file written before
    /// versioning existed has no version field, and leaving the default at the
    /// current one would let such a file pass as the newest schema. Saving stamps
    /// the current version, so this only ever reads back what was on disk.
    /// </summary>
    public int Version { get; set; } = NemConfigVersion.Initial;

    public string? NodeVersion { get; set; }
    public List<NemToolConfig> Tools { get; set; } = [];
}
