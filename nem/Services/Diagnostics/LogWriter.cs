using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nem.Services.Diagnostics;

/// <summary>How much nem writes to its log (<see cref="NemLog.LevelVariable"/>).</summary>
public enum LogLevel
{
    Off,
    /// <summary>What nem decided and did: one or a few lines per call.</summary>
    Info,
    /// <summary>Also the full environment a tool starts with, for chasing a problem.</summary>
    Debug,
}

/// <summary>
/// Appends JSON lines to one log file per day and keeps <see cref="KeepDays"/>
/// days of them. Each line is written while holding the file exclusively, so
/// several nem processes can log at the same time without overwriting each
/// other's lines - .NET's append mode does not append atomically across
/// processes. Best effort: logging never makes a command fail.
/// </summary>
public sealed class LogWriter(string logDir, LogLevel level, string invocationId, string version)
{
    public const int KeepDays = 14;
    const string FilePrefix = "nem-";
    const string FileExtension = ".jsonl";

    // How often, and how far apart, a line tries again while another nem holds the
    // file: writing one line takes well under a millisecond, so this is plenty.
    internal const int WriteAttempts = 50;
    internal static readonly TimeSpan WriteRetryDelay = TimeSpan.FromMilliseconds(5);

    public string LogDir => logDir;
    public LogLevel Level => level;

    public static string FileNameFor(DateTime day) => $"{FilePrefix}{day:yyyy-MM-dd}{FileExtension}";

    /// <summary>The day a log file is for, or null for a file that is not a nem log.</summary>
    public static DateTime? DayOf(string fileName) =>
        fileName.StartsWith(FilePrefix) && fileName.EndsWith(FileExtension)
        && DateTime.TryParseExact(fileName[FilePrefix.Length..^FileExtension.Length], "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
            ? day
            : null;

    public void Write(DateTime now, LogLevel eventLevel, string eventName, object? fields = null)
    {
        if (level == LogLevel.Off || eventLevel > level)
            return;

        var line = new JObject
        {
            ["t"] = now.ToString("o", CultureInfo.InvariantCulture),
            ["id"] = invocationId,
            ["v"] = version,
            ["event"] = eventName,
        };
        if (fields != null)
        {
            foreach (JProperty field in JObject.FromObject(fields).Properties())
                line[field.Name] = field.Value;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(Redactor.Redact(line.ToString(Formatting.None)) + "\n");

        try
        {
            Directory.CreateDirectory(logDir);
            string path = Path.Combine(logDir, FileNameFor(now));
            bool firstOfTheDay = !File.Exists(path);
            AppendExclusively(path, bytes);
            if (firstOfTheDay)
                DeleteOldFiles(now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written is no reason to stop the command.
        }
    }

    static void AppendExclusively(string path, byte[] bytes)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                file.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < WriteAttempts)
            {
                Thread.Sleep(WriteRetryDelay);
            }
        }
    }

    /// <summary>Removes log files older than <see cref="KeepDays"/>; runs once a day, with that day's first line.</summary>
    internal void DeleteOldFiles(DateTime now)
    {
        foreach (string file in Directory.EnumerateFiles(logDir))
        {
            if (DayOf(Path.GetFileName(file)) is { } day && day < now.Date.AddDays(-KeepDays))
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
