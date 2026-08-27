namespace nem.Common.Models;

public class NemConfig
{
    public string? NodeVersion { get; set; }
    public List<NemToolConfig> Tools { get; set; } = [];
}
