namespace nem.Common.Models;

public record NemToolConfig
{
    public required string ToolName { get; set; }
    public required string ToolVersion { get; set; }
}