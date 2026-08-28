using Xunit;
using nem.Services;

namespace nem.Tests;

public class ProxyServiceTests
{
    [Fact]
    public void TryInstallTool_Copies_The_Proxies_For_The_Tool_Name()
    {
        using var tmp = new TempDir();
        string filesDir = Path.Combine(tmp.FullName, "files");
        string targetDir = Path.Combine(tmp.FullName, "target");
        Directory.CreateDirectory(filesDir);
        File.WriteAllText(Path.Combine(filesDir, "NAME"), "sh-template");
        File.WriteAllText(Path.Combine(filesDir, "NAME.bat"), "bat-template");
        File.WriteAllText(Path.Combine(filesDir, "NAME.ps1"), "ps1-template");

        Assert.True(ProxyService.TryInstallTool("tsc", filesDir, targetDir));

        Assert.Equal("sh-template", File.ReadAllText(Path.Combine(targetDir, "tsc")));
        Assert.Equal("bat-template", File.ReadAllText(Path.Combine(targetDir, "tsc.bat")));
        Assert.Equal("ps1-template", File.ReadAllText(Path.Combine(targetDir, "tsc.ps1")));
    }

    [Fact]
    public void TryInstallTool_Fails_Without_Template_Files()
    {
        using var tmp = new TempDir();
        string targetDir = Path.Combine(tmp.FullName, "target");

        Assert.False(ProxyService.TryInstallTool("tsc", Path.Combine(tmp.FullName, "none"), targetDir));
        Assert.False(File.Exists(Path.Combine(targetDir, "tsc")));
        Assert.False(File.Exists(Path.Combine(targetDir, "tsc.bat")));
    }

    [Fact]
    public void Prune_Removes_Proxies_Outside_The_Keep_List_But_Keeps_Npm_And_Npx()
    {
        using var tmp = new TempDir();
        string proxyDir = tmp.FullName;
        foreach (string name in new[] { "npm", "npx", "tsc", "oldbin" })
        {
            foreach (string suffix in new[] { "", ".bat", ".ps1" })
                File.WriteAllText(Path.Combine(proxyDir, name + suffix), "proxy");
        }

        ProxyService.PruneStaleProxies(proxyDir, ["tsc"]);

        Assert.Equal(
            ["npm", "npm.bat", "npm.ps1", "npx", "npx.bat", "npx.ps1", "tsc", "tsc.bat", "tsc.ps1"],
            Directory.EnumerateFiles(proxyDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Prune_Matches_Keep_Names_Case_Insensitively()
    {
        using var tmp = new TempDir();
        string proxyDir = tmp.FullName;
        File.WriteAllText(Path.Combine(proxyDir, "TSC"), "proxy");
        File.WriteAllText(Path.Combine(proxyDir, "TSC.bat"), "proxy");

        ProxyService.PruneStaleProxies(proxyDir, ["tsc"]);

        Assert.True(File.Exists(Path.Combine(proxyDir, "TSC")));
        Assert.True(File.Exists(Path.Combine(proxyDir, "TSC.bat")));
    }

    [Fact]
    public void Prune_Handles_A_Missing_Directory()
    {
        string missing = Path.Combine(Path.GetTempPath(), "nem-tests-missing-" + Guid.NewGuid().ToString("N"));
        ProxyService.PruneStaleProxies(missing, []);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void ResolveToolInEnv_Finds_Binaries_In_The_Env()
    {
        using var tmp = new TempDir();
        string envDir = tmp.FullName;
        string binDir = Path.Combine(envDir, "bin");
        Directory.CreateDirectory(binDir);

        if (OperatingSystem.IsWindows())
        {
            string exe = Path.Combine(binDir, "tsc.exe");
            File.WriteAllText(exe, "binary");
            Assert.Equal(exe, ProxyService.ResolveToolInEnv(envDir, "tsc"));

            File.Delete(exe);
            string cmd = Path.Combine(envDir, "tsc.cmd");
            File.WriteAllText(cmd, "cmd");
            Assert.Equal(cmd, ProxyService.ResolveToolInEnv(envDir, "tsc"));
        }
        else
        {
            string bin = Path.Combine(binDir, "tsc");
            File.WriteAllText(bin, "binary");
            Assert.Equal(bin, ProxyService.ResolveToolInEnv(envDir, "tsc"));

            File.Delete(bin);
            string root = Path.Combine(envDir, "tsc");
            File.WriteAllText(root, "binary");
            Assert.Equal(root, ProxyService.ResolveToolInEnv(envDir, "tsc"));
        }

        Assert.Null(ProxyService.ResolveToolInEnv(envDir, "missing-tool"));
    }

    [Fact]
    public void CallToolInEnvContext_Fails_For_Unknown_Tools_Outside_A_Env()
    {
        string originalCwd = Directory.GetCurrentDirectory();
        using var tmp = new TempDir();
        Directory.SetCurrentDirectory(tmp.FullName);
        try
        {
            int exit = ProxyService.CallToolInEnvContext("definitely-not-a-real-tool-xyz", []);
            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    [Fact]
    public void CallToolInEnvContext_Forwards_The_Tool_Exit_Code()
    {
        string originalCwd = Directory.GetCurrentDirectory();
        using var tmp = new TempDir();
        string envDir = Path.Combine(tmp.FullName, ".nenv");
        string binDir = Path.Combine(envDir, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(tmp.FullName, "nem.json"), "{}");

        string toolPath;
        if (OperatingSystem.IsWindows())
        {
            toolPath = Path.Combine(binDir, "tsc.bat");
            File.WriteAllText(toolPath, "@echo off\r\nexit /b 42\r\n");
        }
        else
        {
            toolPath = Path.Combine(binDir, "tsc");
            File.WriteAllText(toolPath, "#!/bin/sh\nexit 42\n");
            File.SetUnixFileMode(toolPath, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Directory.SetCurrentDirectory(tmp.FullName);
        try
        {
            Assert.Equal(42, ProxyService.CallToolInEnvContext("tsc", []));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }
}
