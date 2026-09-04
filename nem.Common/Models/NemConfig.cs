using nem.Common.Attributes;

namespace nem.Common.Models;

[JsonHeader(
    @"/*
 * nem - Node Environment Manager
 * Repository: https://github.com/DeadMoon0/nem
 * Install: dotnet tool install -g nem
 * This file declares the Node.js version and tools for your project.
 * Run ""nem install"" after cloning to set up the exact same environment.
 */")]
public class NemConfig
{
    public string? NodeVersion { get; set; }
    public List<NemToolConfig> Tools { get; set; } = [];
    public string Version { get; set; } = "1.0.0";
}
