using System.IO.Compression;
using Xunit;
using nem.Services;
using nem.Services.Diagnostics;
using Newtonsoft.Json.Linq;

namespace nem.Tests;

/// <summary>
/// A report holds what is needed to understand a problem on another machine, and
/// names what looks wrong up front.
/// </summary>
public class DiagnosticReportTests
{
    static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "ProxyFiles");

    sealed class Machine : IDisposable
    {
        readonly TempDir _tmp = new();
        public string Root => _tmp.FullName;
        public string ProxyDir { get; }
        public string LogDir { get; }
        public EnvStore Store { get; }
        public string Project { get; }

        public Machine()
        {
            ProxyDir = Directory.CreateDirectory(Path.Combine(Root, "proxy")).FullName;
            LogDir = Directory.CreateDirectory(Path.Combine(Root, "logs")).FullName;
            Store = new EnvStore(Path.Combine(Root, "store"), Path.Combine(Root, "globals"));
            Project = Directory.CreateDirectory(Path.Combine(Root, "project")).FullName;
        }

        public IReadOnlyList<DiagnosticReport.ReportFile> Report(string? cwd = null, int days = 14) =>
            DiagnosticReport.Collect(new ReportInputs(cwd ?? Project, ProxyDir, TemplatesDir, Store, LogDir, days, Tool: null, Now));

        public void Dispose() => _tmp.Dispose();
    }

    static string File(IReadOnlyList<DiagnosticReport.ReportFile> files, string path) => files.Single(f => f.Path == path).Content;

    [Fact]
    public void A_proxy_an_older_nem_wrote_is_named_in_the_summary()
    {
        using var machine = new Machine();
        System.IO.File.WriteAllText(Path.Combine(machine.ProxyDir, "npm.bat"), "@echo off\r\nnem run %~n0 -- %*\r\n");
        System.IO.File.Copy(Path.Combine(TemplatesDir, "NAME.bat"), Path.Combine(machine.ProxyDir, "ng.bat"));

        var files = machine.Report();

        Assert.Contains("npm.bat", File(files, "report.md"));
        var proxies = JObject.Parse(File(files, "proxies.json"))["files"]!;
        Assert.False((bool)proxies.Single(p => (string)p["name"]! == "npm.bat")["matchesTemplate"]!);
        Assert.Equal("nem run", (string)proxies.Single(p => (string)p["name"]! == "npm.bat")["calls"]!);
        Assert.True((bool)proxies.Single(p => (string)p["name"]! == "ng.bat")["matchesTemplate"]!);
    }

    [Fact]
    public void What_the_env_lacks_in_the_store_is_named_in_the_summary()
    {
        using var machine = new Machine();
        System.IO.File.WriteAllText(Path.Combine(machine.Project, "nem.jsonc"), """{ "NodeVersion": "22.23.3", "Tools": [ { "ToolName": "typescript", "ToolVersion": "5.9.3" } ] }""");
        StoreFixture.CompleteEntry(machine.Store.NodeEntryDir("22.23.3"));

        var files = machine.Report();

        Assert.Contains("typescript@5.9.3", File(files, "report.md"));
        JObject env = JObject.Parse(File(files, "env.json"));
        Assert.True((bool)env["found"]!);
        Assert.Equal(["typescript@5.9.3"], env["missing"]!.Select(m => (string)m!));
        // Before the install, the package name stands in for its commands.
        Assert.NotNull(env["which"]!["typescript"]);
    }

    [Fact]
    public void An_install_that_never_finished_is_named_in_the_summary()
    {
        using var machine = new Machine();
        Directory.CreateDirectory(machine.Store.ToolEntryDir("@angular/cli", "22.2.3", 22));

        var files = machine.Report();

        Assert.Contains("@angular+cli@22.2.3-node22", File(files, "report.md"));
        Assert.False((bool)JObject.Parse(File(files, "store.json"))["entries"]![0]!["complete"]!);
    }

    [Fact]
    public void The_logs_of_the_chosen_days_go_along_and_their_errors_are_summarised()
    {
        using var machine = new Machine();
        var today = new LogWriter(machine.LogDir, LogLevel.Info, "1", "1.5.0");
        today.Write(Now.AddHours(-1), LogLevel.Info, "error", new { where = "resolve", message = "Access to the path is denied." });
        System.IO.File.WriteAllText(Path.Combine(machine.LogDir, LogWriter.FileNameFor(Now.AddDays(-3))), "{}\n");
        System.IO.File.WriteAllText(Path.Combine(machine.LogDir, LogWriter.FileNameFor(Now.AddDays(-30))), "{}\n");

        var files = machine.Report(days: 7);

        Assert.Equal(2, files.Count(f => f.Path.StartsWith("logs/")));
        Assert.Contains("resolve: Access to the path is denied.", File(files, "report.md"));
    }

    [Fact]
    public void Outside_an_env_the_report_says_so_and_still_shows_what_npm_runs()
    {
        using var machine = new Machine();

        JObject env = JObject.Parse(File(machine.Report(), "env.json"));

        Assert.False((bool)env["found"]!);
        Assert.NotNull(env["which"]!["npm"]);
    }

    [Fact]
    public void Secrets_in_any_part_are_masked()
    {
        using var machine = new Machine();
        System.IO.File.WriteAllText(Path.Combine(machine.Project, "nem.jsonc"), """{ "NodeVersion": "22.23.3" } // registry https://me:pa55@feed.example/""");

        var files = machine.Report();

        Assert.All(files, f => Assert.DoesNotContain("pa55", f.Content));
    }

    [Fact]
    public void The_zip_holds_every_file_of_the_report()
    {
        using var machine = new Machine();
        var files = machine.Report();
        string zipPath = Path.Combine(machine.Root, "report.zip");

        DiagnosticReport.WriteZip(files, zipPath);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(files.Select(f => f.Path).Order(), zip.Entries.Select(e => e.FullName).Order());
    }
}
