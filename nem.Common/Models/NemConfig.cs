namespace nem.Common.Models;

public record NemConfig
{
    public required string NodeVersion { get; set; }
    public List<NemToolConfig> Tools { get; set; } = [];
}