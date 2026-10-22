using Xunit;
using nem.Services;

namespace nem.Tests;

/// <summary>
/// 'nem resolve' answers the proxy scripts: which file to start and with which
/// variables, written so each shell reads every value back exactly as it was.
/// </summary>
public class ResolveTests
{
    static ToolLaunch Launch(string tool, params (string Name, string Value)[] variables) =>
        new(tool, variables.Select(v => new KeyValuePair<string, string>(v.Name, v.Value)).ToList());

    [Fact]
    public void Sh_keeps_quotes_dollars_and_spaces_literal()
    {
        string text = ResolveOutput.Format(ShellSyntax.Sh, Launch("/opt/it's $HOME/ng", ("NODE_PATH", "/a b/`x`")));

        Assert.Equal("export NODE_PATH='/a b/`x`'\nNEM_TOOL='/opt/it'\\''s $HOME/ng'\n", text);
    }

    [Fact]
    public void Cmd_sets_each_value_in_quotes_with_crlf_lines()
    {
        string text = ResolveOutput.Format(ShellSyntax.Cmd, Launch(@"C:\a b\100%\x!y\ng.cmd", ("PATH", @"C:\store\node;C:\Windows")));

        Assert.Equal("set \"PATH=C:\\store\\node;C:\\Windows\"\r\nset \"NEM_TOOL=C:\\a b\\100%\\x!y\\ng.cmd\"\r\n", text);
    }

    [Fact]
    public void Ps_lines_keep_an_equals_sign_inside_the_value()
    {
        string text = ResolveOutput.Format(ShellSyntax.Ps, Launch(@"C:\tools\ng.cmd", ("NODE_PATH", @"C:\a=b")));

        Assert.Equal("NODE_PATH=C:\\a=b\nNEM_TOOL=C:\\tools\\ng.cmd\n", text);
    }

    [Theory]
    [InlineData(ShellSyntax.Sh, "")]
    [InlineData(ShellSyntax.Ps, "")]
    [InlineData(ShellSyntax.Cmd, "set \"NEM_EXIT=3\"\r\n")]
    public void A_failure_names_no_tool_and_cmd_gets_the_exit_code_as_a_variable(ShellSyntax shell, string expected)
    {
        // sh and PowerShell read nem's exit code; cmd's 'for /f' cannot.
        Assert.Equal(expected, ResolveOutput.FormatFailure(shell, 3));
    }

    [Fact]
    public void A_tool_in_an_env_runs_with_the_env_first_on_the_path_and_its_own_npm_prefix()
    {
        using var tmp = new TempDir();
        var store = new EnvStore(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));
        string project = Directory.CreateDirectory(Path.Combine(tmp.FullName, "project")).FullName;
        File.WriteAllText(Path.Combine(project, "nem.jsonc"), """{ "NodeVersion": "22.0.0", "Tools": [ { "ToolName": "typescript", "ToolVersion": "5.0.0" } ] }""");
        StoreFixture.CompleteEntry(store.NodeEntryDir("22.0.0"));
        string toolEntry = store.ToolEntryDir("typescript", "5.0.0", 22);
        StoreFixture.CompleteEntry(toolEntry);
        string binDir = Directory.CreateDirectory(NodeEnvLayout.Create(toolEntry).BinDir).FullName;
        File.WriteAllText(Path.Combine(binDir, OperatingSystem.IsWindows() ? "tsc.cmd" : "tsc"), "");

        ToolLaunch launch = Assert.IsType<ToolLaunch>(ProxyService.PrepareLaunch("tsc", project, store, out _));
        var variables = launch.Environment.ToDictionary(v => v.Key, v => v.Value);

        Assert.StartsWith(binDir, launch.ExecutablePath);
        Assert.StartsWith(binDir, variables["PATH"]);
        Assert.Equal(store.GlobalsDirFor(22), variables["npm_config_prefix"]);
        Assert.Contains(NodeEnvLayout.Create(toolEntry).ModulesRoot, variables["NODE_PATH"]);
    }

    [Fact]
    public void Outside_an_env_the_system_tool_runs_with_the_environment_untouched()
    {
        using var tmp = new TempDir();
        var store = new EnvStore(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));

        // dotnet is on the PATH wherever these tests run.
        ToolLaunch launch = Assert.IsType<ToolLaunch>(ProxyService.PrepareLaunch("dotnet", tmp.FullName, store, out _));

        Assert.Empty(launch.Environment);
    }

    [Fact]
    public void A_tool_that_exists_nowhere_fails_without_an_answer()
    {
        using var tmp = new TempDir();
        var store = new EnvStore(Path.Combine(tmp.FullName, "store"), Path.Combine(tmp.FullName, "globals"));

        Assert.Null(ProxyService.PrepareLaunch("definitely-not-a-real-tool-xyz", tmp.FullName, store, out int exitCode));
        Assert.Equal(1, exitCode);
    }
}
