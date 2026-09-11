using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// A project that declares a tool itself decides which version runs there, because
/// npm CLIs resolve the nearest node_modules first. nem reports that rather than
/// fighting it, so it has to find the project copy the same way npm would.
/// </summary>
public class LocalToolVersionTests
{
    static void WritePackage(string nodeModulesOwner, string packageName, string version)
    {
        string dir = Path.Combine(nodeModulesOwner, "node_modules", Path.Combine(packageName.Split('/')));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $"{{\"version\":\"{version}\"}}");
    }

    [Fact]
    public void Finds_The_Copy_In_The_Current_Folder()
    {
        using var tmp = new TempDir();
        WritePackage(tmp.FullName, "typescript", "5.6.3");

        Assert.Equal("5.6.3", ToolService.FindLocalToolVersion(tmp.FullName, "typescript"));
    }

    [Fact]
    public void Finds_A_Copy_Above_The_Current_Folder()
    {
        using var tmp = new TempDir();
        WritePackage(tmp.FullName, "typescript", "5.6.3");
        string deep = Path.Combine(tmp.FullName, "src", "app");
        Directory.CreateDirectory(deep);

        Assert.Equal("5.6.3", ToolService.FindLocalToolVersion(deep, "typescript"));
    }

    [Fact]
    public void Handles_Scoped_Package_Names()
    {
        using var tmp = new TempDir();
        WritePackage(tmp.FullName, "@angular/cli", "20.3.37");

        Assert.Equal("20.3.37", ToolService.FindLocalToolVersion(tmp.FullName, "@angular/cli"));
    }

    [Fact]
    public void The_Nearest_Copy_Wins()
    {
        using var tmp = new TempDir();
        WritePackage(tmp.FullName, "@angular/cli", "20.3.11");
        string inner = Path.Combine(tmp.FullName, "app");
        Directory.CreateDirectory(inner);
        WritePackage(inner, "@angular/cli", "20.3.37");

        Assert.Equal("20.3.37", ToolService.FindLocalToolVersion(inner, "@angular/cli"));
    }

    [Fact]
    public void Returns_Null_Without_A_Project_Copy()
    {
        using var tmp = new TempDir();

        Assert.Null(ToolService.FindLocalToolVersion(tmp.FullName, "@angular/cli"));
    }

    [Fact]
    public void Returns_Null_For_A_Malformed_Package_Json()
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.FullName, "node_modules", "typescript");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), "{ not json");

        Assert.Null(ToolService.FindLocalToolVersion(tmp.FullName, "typescript"));
    }

    [Fact]
    public void Returns_Null_When_The_Package_Json_Has_No_Version()
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.FullName, "node_modules", "typescript");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), "{\"name\":\"typescript\"}");

        Assert.Null(ToolService.FindLocalToolVersion(tmp.FullName, "typescript"));
    }

    [Fact]
    public void Walking_Up_Stops_At_The_Filesystem_Root()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        ToolService.FindLocalToolVersion(root, "typescript");
    }
}
