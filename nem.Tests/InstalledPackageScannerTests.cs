using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// Pins which packages the audit sends to the registry: every installed package
/// and nested dependency, but not Node's own npm/corepack bundles or non-packages.
/// </summary>
public class InstalledPackageScannerTests
{
    static void WritePackage(string dir, string name, string version)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $$"""{ "name": "{{name}}", "version": "{{version}}" }""");
    }

    [Fact]
    public void Scan_Collects_Tools_Scoped_Packages_And_Nested_Dependencies()
    {
        using var temp = new TempDir();
        string root = temp.FullName;
        WritePackage(Path.Combine(root, "typescript"), "typescript", "5.6.3");
        WritePackage(Path.Combine(root, "@angular", "cli"), "@angular/cli", "19.2.27");
        WritePackage(Path.Combine(root, "@angular", "cli", "node_modules", "semver"), "semver", "7.6.3");
        WritePackage(Path.Combine(root, "typescript", "node_modules", "semver"), "semver", "7.5.4");

        var found = InstalledPackageScanner.Scan(root);

        Assert.Equal(["@angular/cli", "semver", "typescript"], found.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(["7.5.4", "7.6.3"], found["semver"]);
    }

    [Fact]
    public void Scan_Skips_Node_Bundles_Dot_Folders_And_Folders_Without_A_Package()
    {
        using var temp = new TempDir();
        string root = temp.FullName;
        WritePackage(Path.Combine(root, "npm"), "npm", "10.9.0");
        WritePackage(Path.Combine(root, "corepack"), "corepack", "0.30.0");
        WritePackage(Path.Combine(root, ".bin"), "hidden", "1.0.0");
        Directory.CreateDirectory(Path.Combine(root, "not-a-package"));
        Directory.CreateDirectory(Path.Combine(root, "broken"));
        File.WriteAllText(Path.Combine(root, "broken", "package.json"), "{ not json");

        Assert.Empty(InstalledPackageScanner.Scan(root));
    }

    [Fact]
    public void Scan_Of_A_Missing_Folder_Finds_Nothing()
    {
        Assert.Empty(InstalledPackageScanner.Scan(Path.Combine(Path.GetTempPath(), "nem-tests", Guid.NewGuid().ToString("N"))));
    }
}
