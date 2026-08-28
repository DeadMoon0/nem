using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Parsing of 'nem tool add <name>[@<version>]' package specs, including
/// scoped packages (@scope/name).
/// </summary>
public class ToolSpecParsingTests
{
    [Theory]
    [InlineData("typescript", true, "typescript", null)]
    [InlineData("typescript@5.6.3", true, "typescript", "5.6.3")]
    [InlineData("typescript@latest", true, "typescript", "latest")]
    [InlineData("@angular/cli", true, "@angular/cli", null)]
    [InlineData("@angular/cli@18.2.6", true, "@angular/cli", "18.2.6")]
    [InlineData("@angular/cli@latest", true, "@angular/cli", "latest")]
    [InlineData("pkg@", true, "pkg@", null)]
    [InlineData("", false, null, null)]
    [InlineData("   ", false, null, null)]
    public void TryParsePackageSpec_Splits_Name_And_Version(string input, bool expectedOk, string? expectedName, string? expectedVersion)
    {
        bool ok = ToolService.TryParsePackageSpec(input, out string? name, out string? version);

        Assert.Equal(expectedOk, ok);
        if (ok)
        {
            Assert.Equal(expectedName, name);
            Assert.Equal(expectedVersion, version);
        }
    }

    [Theory]
    [InlineData("typescript", true)]
    [InlineData("@angular/cli", true)]
    [InlineData("@angular/cli-extra", true)]
    [InlineData("@a/b", true)]
    [InlineData("@angular", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("has space", false)]
    [InlineData("pkg/", false)]
    public void IsValidPackageName_Applies_Npm_Naming_Rules(string name, bool expected)
        => Assert.Equal(expected, ToolService.IsValidPackageName(name));

    [Fact]
    public void IsToolInstalled_Reflects_The_Env_Modules_Root()
    {
        using var tmp = new TempDir();
        string envDir = Path.Combine(tmp.FullName, ".nenv");
        string modulesRoot = NodeEnvLayout.Create(envDir).ModulesRoot;

        Assert.False(ToolService.IsToolInstalled(envDir, "typescript"));

        Directory.CreateDirectory(Path.Combine(modulesRoot, "typescript"));
        File.WriteAllText(
            Path.Combine(modulesRoot, "typescript", "package.json"),
            """{"name":"typescript","version":"5.6.3"}""");
        Assert.True(ToolService.IsToolInstalled(envDir, "typescript"));

        // Scoped packages live in @scope/pkg subdirectories.
        Directory.CreateDirectory(Path.Combine(modulesRoot, "@angular", "cli"));
        File.WriteAllText(
            Path.Combine(modulesRoot, "@angular", "cli", "package.json"),
            """{"name":"@angular/cli","version":"18.2.6"}""");
        Assert.True(ToolService.IsToolInstalled(envDir, "@angular/cli"));
    }

    [Fact]
    public void GetInstalledToolVersion_Reads_The_Version_From_Disk()
    {
        using var tmp = new TempDir();
        string envDir = Path.Combine(tmp.FullName, ".nenv");
        string packageDir = Path.Combine(NodeEnvLayout.Create(envDir).ModulesRoot, "typescript");
        Directory.CreateDirectory(packageDir);

        Assert.Null(ToolService.GetInstalledToolVersion(envDir, "typescript"));

        File.WriteAllText(Path.Combine(packageDir, "package.json"), """{"name":"typescript"}""");
        Assert.Null(ToolService.GetInstalledToolVersion(envDir, "typescript"));

        File.WriteAllText(
            Path.Combine(packageDir, "package.json"),
            """{"name":"typescript","version":"5.6.3"}""");
        Assert.Equal("5.6.3", ToolService.GetInstalledToolVersion(envDir, "typescript"));
    }

    [Fact]
    public void ReadToolBins_Uses_The_Bin_Field_From_The_Package()
    {
        using var tmp = new TempDir();
        string envDir = Path.Combine(tmp.FullName, ".nenv");
        string packageDir = Path.Combine(NodeEnvLayout.Create(envDir).ModulesRoot, "typescript");
        Directory.CreateDirectory(packageDir);

        // No package.json: fall back to the (guessed) binary name.
        Assert.Equal(["typescript"], ToolService.ReadToolBins(envDir, "typescript"));

        // A string bin value means the executable is named after the package itself.
        File.WriteAllText(Path.Combine(packageDir, "package.json"), """{"bin":"tsc"}""");
        Assert.Equal(["typescript"], ToolService.ReadToolBins(envDir, "typescript"));

        // An object bin value lists the actual executable names.
        File.WriteAllText(
            Path.Combine(packageDir, "package.json"),
            """{"bin":{"tsc":"bin/tsc.js","tsserver":"bin/tsserver.js"}}""");
        Assert.Equal(["tsc", "tsserver"], ToolService.ReadToolBins(envDir, "typescript"));
    }

    [Fact]
    public void ReadToolBins_Guesses_The_Short_Name_For_Scoped_Packages()
    {
        using var tmp = new TempDir();
        string envDir = Path.Combine(tmp.FullName, ".nenv");

        Assert.Equal(["cli"], ToolService.ReadToolBins(envDir, "@angular/cli"));
    }
}
