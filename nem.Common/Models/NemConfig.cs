using nem.Common.Attributes;
using System.Reflection;

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
    public static readonly string Header = typeof(NemConfig)
        .GetCustomAttributes(typeof(JsonHeaderAttribute), false)
        .OfType<JsonHeaderAttribute>()
        .FirstOrDefault()?
        .Header ?? string.Empty;

    public string? NodeVersion { get; set; }
    public List<NemToolConfig> Tools { get; set; } = [];
    public string Version { get; set; } = "Current.Default.Version";
}
