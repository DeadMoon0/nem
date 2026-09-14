using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// The one decision behind both 'nem run' and 'nem which': which copy of a tool a
/// name reaches, and the chain that explains it.
/// </summary>
public class ToolResolverTests
{
    /// <summary>
    /// An env that declares <paramref name="packageName"/>, has it installed with
    /// the given bins, and carries a shim for each bin - the shape a real
    /// 'nem install' leaves behind.
    /// </summary>
    static string MakeEnv(string root, string packageName, params string[] bins)
    {
        File.WriteAllText(
            Path.Combine(root, "nem.json"),
            $"{{\"NodeVersion\":\"22.0.0\",\"Tools\":[{{\"ToolName\":\"{packageName}\",\"ToolVersion\":\"1.0.0\"}}]}}");

        string envDir = Path.Combine(root, ".nenv");

        // The env's modules root is not 'node_modules' everywhere - Unix installs
        // under lib/. Asking the layout keeps the fixture the same shape as a real
        // install on whichever host the tests run.
        string modules = Path.Combine(
            NodeEnvLayout.Create(envDir).ModulesRoot,
            Path.Combine(packageName.Split('/')));
        Directory.CreateDirectory(modules);
        string binMap = string.Join(",", bins.Select(bin => $"\"{bin}\":\"bin.js\""));
        File.WriteAllText(Path.Combine(modules, "package.json"), $"{{\"version\":\"1.0.0\",\"bin\":{{{binMap}}}}}");

        foreach (string bin in bins)
            File.WriteAllText(Path.Combine(envDir, bin + (OperatingSystem.IsWindows() ? ".cmd" : "")), "shim");

        return envDir;
    }

    static void MakeLocalPackage(string owner, string packageName, string version)
    {
        string dir = Path.Combine(owner, "node_modules", Path.Combine(packageName.Split('/')));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $"{{\"version\":\"{version}\"}}");
    }

    [Fact]
    public void The_Env_Copy_Wins_Without_A_Project_Copy()
    {
        using var tmp = new TempDir();
        string envDir = MakeEnv(tmp.FullName, "@angular/cli", "ng");

        var resolution = ToolResolver.Resolve(tmp.FullName, "ng");

        Assert.Equal(ToolResolver.ToolSource.Env, resolution.Source);
        Assert.Equal(envDir, resolution.EnvDir);
        Assert.NotNull(resolution.ExecutablePath);
        Assert.Null(resolution.LocalVersion);
    }

    [Fact]
    public void A_Project_Copy_Nearer_The_Folder_Is_Reported()
    {
        using var tmp = new TempDir();
        MakeEnv(tmp.FullName, "@angular/cli", "ng");
        string app = Path.Combine(tmp.FullName, "app");
        Directory.CreateDirectory(app);
        MakeLocalPackage(app, "@angular/cli", "20.3.37");

        var resolution = ToolResolver.Resolve(app, "ng");

        Assert.Equal(ToolResolver.ToolSource.Local, resolution.Source);
        Assert.Equal("20.3.37", resolution.LocalVersion);
    }

    [Fact]
    public void The_Env_Copy_Is_Still_What_Gets_Started_When_A_Project_Copy_Exists()
    {
        // nem launches the env copy either way; whether the tool hands over to the
        // project copy is the tool's business, so the path must stay the env's.
        using var tmp = new TempDir();
        string envDir = MakeEnv(tmp.FullName, "@angular/cli", "ng");
        MakeLocalPackage(tmp.FullName, "@angular/cli", "9.9.9");

        var resolution = ToolResolver.Resolve(tmp.FullName, "ng");

        Assert.Equal(ToolResolver.ToolSource.Local, resolution.Source);
        Assert.StartsWith(envDir, resolution.ExecutablePath);
    }

    [Fact]
    public void A_Tool_The_Env_Does_Not_Carry_Is_Unavailable()
    {
        using var tmp = new TempDir();
        MakeEnv(tmp.FullName, "@angular/cli", "ng");

        var resolution = ToolResolver.Resolve(tmp.FullName, "tsc");

        Assert.Equal(ToolResolver.ToolSource.Unavailable, resolution.Source);
        Assert.Null(resolution.ExecutablePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exactly_One_Step_Decides(bool withEnv)
    {
        // Whichever branch the chain takes, the reader must be able to point at one
        // step and say "that is why".
        using var tmp = new TempDir();
        if (withEnv)
            MakeEnv(tmp.FullName, "@angular/cli", "ng");

        var resolution = ToolResolver.Resolve(tmp.FullName, "ng");

        Assert.Single(resolution.Chain, step => step.Decided);
    }

    [Fact]
    public void The_Chain_Records_Every_Question_Asked()
    {
        using var tmp = new TempDir();
        MakeEnv(tmp.FullName, "@angular/cli", "ng");

        var resolution = ToolResolver.Resolve(tmp.FullName, "ng");

        // typed name, env, env copy, project copy
        Assert.Equal(4, resolution.Chain.Count);
        Assert.All(resolution.Chain, step => Assert.False(string.IsNullOrWhiteSpace(step.Finding)));
    }

    [Fact]
    public void Without_An_Env_The_Chain_Stops_At_The_Env_Question()
    {
        using var tmp = new TempDir();

        var resolution = ToolResolver.Resolve(tmp.FullName, "definitely-not-a-real-tool-xyz");

        Assert.Equal(ToolResolver.ToolSource.Unavailable, resolution.Source);
        Assert.Null(resolution.EnvDir);
        Assert.Contains(resolution.Chain, step => step.Question == "env");
        Assert.Contains(resolution.Chain, step => step.Question == "system tool" && step.Decided);
    }

    [Fact]
    public void The_Nearest_Project_Copy_Is_The_One_Reported()
    {
        using var tmp = new TempDir();
        MakeEnv(tmp.FullName, "@angular/cli", "ng");
        MakeLocalPackage(tmp.FullName, "@angular/cli", "20.3.11");
        string app = Path.Combine(tmp.FullName, "app");
        Directory.CreateDirectory(app);
        MakeLocalPackage(app, "@angular/cli", "20.3.37");

        Assert.Equal("20.3.37", ToolResolver.Resolve(app, "ng").LocalVersion);
    }

    [Fact]
    public void FindLocalPackageDir_Returns_Null_Without_A_Copy()
    {
        using var tmp = new TempDir();

        Assert.Null(ToolResolver.FindLocalPackageDir(tmp.FullName, "@angular/cli"));
    }
}
