using Xunit;
using nem.Services.Diagnostics;
using Newtonsoft.Json.Linq;

namespace nem.Tests;

/// <summary>
/// nem's log keeps what every call decided for two weeks, without secrets, and
/// several nem processes can write to it at once.
/// </summary>
public class NemLogTests
{
    static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    static JObject[] Lines(string logDir, DateTime day) =>
        File.ReadAllLines(Path.Combine(logDir, LogWriter.FileNameFor(day))).Select(JObject.Parse).ToArray();

    [Fact]
    public void Every_event_is_one_json_line_naming_the_call_and_the_nem_version()
    {
        using var tmp = new TempDir();
        var log = new LogWriter(tmp.FullName, LogLevel.Info, "4711-abc", "1.5.0");

        log.Write(Now, LogLevel.Info, "start", new { args = new[] { "resolve", "ng" } });
        log.Write(Now, LogLevel.Info, "end", new { exitCode = 0 });

        JObject[] lines = Lines(tmp.FullName, Now);
        Assert.Equal(["start", "end"], lines.Select(l => (string)l["event"]!));
        Assert.All(lines, l => Assert.Equal("4711-abc", (string)l["id"]!));
        Assert.All(lines, l => Assert.Equal("1.5.0", (string)l["v"]!));
        Assert.Equal("ng", (string)lines[0]["args"]![1]!);
    }

    [Fact]
    public void Debug_detail_is_only_written_when_asked_for_and_off_writes_nothing()
    {
        using var tmp = new TempDir();
        string info = Path.Combine(tmp.FullName, "info"), debug = Path.Combine(tmp.FullName, "debug"), off = Path.Combine(tmp.FullName, "off");

        foreach ((string dir, LogLevel level) in new[] { (info, LogLevel.Info), (debug, LogLevel.Debug), (off, LogLevel.Off) })
        {
            var log = new LogWriter(dir, level, "1", "1.5.0");
            log.Write(Now, LogLevel.Info, "decision");
            log.Write(Now, LogLevel.Debug, "detail");
        }

        Assert.Equal(["decision"], Lines(info, Now).Select(l => (string)l["event"]!));
        Assert.Equal(["decision", "detail"], Lines(debug, Now).Select(l => (string)l["event"]!));
        Assert.False(Directory.Exists(off));
    }

    [Theory]
    [InlineData(null, LogLevel.Info)]
    [InlineData("", LogLevel.Info)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("OFF", LogLevel.Off)]
    [InlineData("0", LogLevel.Off)]
    public void The_level_comes_from_NEM_LOG(string? value, LogLevel expected)
    {
        Assert.Equal(expected, NemLog.ParseLevel(value));
    }

    [Fact]
    public void Two_weeks_are_kept_and_older_days_go_with_the_first_line_of_a_new_day()
    {
        using var tmp = new TempDir();
        string kept = Path.Combine(tmp.FullName, LogWriter.FileNameFor(Now.AddDays(-LogWriter.KeepDays)));
        string old = Path.Combine(tmp.FullName, LogWriter.FileNameFor(Now.AddDays(-LogWriter.KeepDays - 1)));
        string notALog = Path.Combine(tmp.FullName, "notes.txt");
        foreach (string file in new[] { kept, old, notALog })
            File.WriteAllText(file, "");

        new LogWriter(tmp.FullName, LogLevel.Info, "1", "1.5.0").Write(Now, LogLevel.Info, "start");

        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(notALog));
    }

    [Fact]
    public void Secrets_never_reach_the_log()
    {
        using var tmp = new TempDir();
        var log = new LogWriter(tmp.FullName, LogLevel.Info, "1", "1.5.0");

        log.Write(Now, LogLevel.Info, "start", new { args = new[] { "npm", "--//feed.example/npm/:_authToken=s3cr3t", "--registry=https://me:pa55@feed.example/" } });

        string text = File.ReadAllText(Path.Combine(tmp.FullName, LogWriter.FileNameFor(Now)));
        Assert.DoesNotContain("s3cr3t", text);
        Assert.DoesNotContain("pa55", text);
        Assert.Single(Lines(tmp.FullName, Now));
    }

    [Fact]
    public void Several_processes_writing_at_once_do_not_mix_their_lines()
    {
        using var tmp = new TempDir();
        const int LinesPerWriter = 200;

        Parallel.For(0, 4, writer =>
        {
            var log = new LogWriter(tmp.FullName, LogLevel.Info, $"writer-{writer}", "1.5.0");
            for (int i = 0; i < LinesPerWriter; i++)
                log.Write(Now, LogLevel.Info, "tool.resolved", new { i, padding = new string('x', 300) });
        });

        JObject[] lines = Lines(tmp.FullName, Now);
        Assert.Equal(4 * LinesPerWriter, lines.Length);
    }
}
