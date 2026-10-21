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
    public void Installing_A_Tool_Leaves_The_Proxies_Of_Other_Envs_Alone()
    {
        // The proxy directory is shared by every env on the machine, so installing
        // one env's tool must never remove another env's proxies.
        using var tmp = new TempDir();
        string filesDir = Path.Combine(tmp.FullName, "files");
        string targetDir = Path.Combine(tmp.FullName, "target");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(targetDir);
        foreach (string template in new[] { "NAME", "NAME.bat", "NAME.ps1" })
            File.WriteAllText(Path.Combine(filesDir, template), "template");

        // Another project's proxy is already there.
        foreach (string suffix in new[] { "", ".bat", ".ps1" })
            File.WriteAllText(Path.Combine(targetDir, "ng" + suffix), "other-env-proxy");

        ProxyService.TryInstallTool("tsc", filesDir, targetDir);

        Assert.True(File.Exists(Path.Combine(targetDir, "tsc.bat")));
        Assert.True(File.Exists(Path.Combine(targetDir, "ng")));
        Assert.True(File.Exists(Path.Combine(targetDir, "ng.bat")));
        Assert.True(File.Exists(Path.Combine(targetDir, "ng.ps1")));
    }

    [Fact]
    public void FindCommand_Takes_The_First_Folder_That_Has_The_Command()
    {
        using var tmp = new TempDir();
        string first = Directory.CreateDirectory(Path.Combine(tmp.FullName, "tool")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(tmp.FullName, "node")).FullName;
        string name = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        File.WriteAllText(Path.Combine(second, name), "");

        Assert.Equal(Path.Combine(second, name), EnvComposition.FindCommand([first, second], "npm"));

        File.WriteAllText(Path.Combine(first, name), "");
        Assert.Equal(Path.Combine(first, name), EnvComposition.FindCommand([first, second], "npm"));
        Assert.Null(EnvComposition.FindCommand([first, second], "missing-tool"));
    }

    [Fact]
    public void FindCommand_Prefers_An_Exe_Over_A_Cmd_Shim_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.FullName, "node.cmd"), "");
        File.WriteAllText(Path.Combine(tmp.FullName, "node.exe"), "");

        Assert.Equal(Path.Combine(tmp.FullName, "node.exe"), EnvComposition.FindCommand([tmp.FullName], "node"));
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
        using var tmp = new TempDir();
        var store = new EnvStore(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));
        string project = Directory.CreateDirectory(Path.Combine(tmp.FullName, "project")).FullName;
        File.WriteAllText(Path.Combine(project, "nem.jsonc"), """{ "NodeVersion": "22.0.0", "Tools": [ { "ToolName": "typescript", "ToolVersion": "5.0.0" } ] }""");

        // Both entries complete, so the call goes straight to the tool.
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.0.0"));
        string toolEntry = store.ToolEntryDir("typescript", "5.0.0", 22);
        StoreFixture.CompleteEntry(toolEntry);
        string binDir = NodeEnvLayout.Create(toolEntry).BinDir;
        Directory.CreateDirectory(binDir);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(binDir, "tsc.bat"), "@echo off\r\nexit /b 42\r\n");
        }
        else
        {
            string toolPath = Path.Combine(binDir, "tsc");
            File.WriteAllText(toolPath, "#!/bin/sh\nexit 42\n");
            File.SetUnixFileMode(toolPath, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(42, ProxyService.CallToolInEnvContext("tsc", [], project, store));
    }
}
