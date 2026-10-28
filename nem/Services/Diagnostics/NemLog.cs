using System;
using System.Collections.Generic;
using System.IO;
using nem.Common;

namespace nem.Services.Diagnostics;

/// <summary>
/// nem's log: what every call decided and did, kept for two weeks in the nem
/// folder (logs/), so a problem that "fixed itself" can still be looked at
/// afterwards - 'nem report' puts these files into the report.
/// </summary>
public static class NemLog
{
    /// <summary>off, info (the default) or debug.</summary>
    public const string LevelVariable = "NEM_LOG";

    static LogWriter? _writer;

    /// <summary>Where the logs go; under nem's folder, so NEM_HOME moves them along.</summary>
    public static string LogDir => Path.Combine(IOPathManager.System.DirPath, "logs");

    public static LogLevel ParseLevel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "off" or "0" or "false" or "no" => LogLevel.Off,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info,
    };

    /// <summary>Starts the log for this call and records how nem was called.</summary>
    public static void Start(IReadOnlyList<string> args)
    {
        try
        {
            _writer = new LogWriter(
                LogDir,
                ParseLevel(Environment.GetEnvironmentVariable(LevelVariable)),
                $"{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}",
                ProxyMigrator.CurrentVersion);
        }
        catch (InvalidOperationException)
        {
            // No usable nem folder; the command reports that itself.
            return;
        }
        Info("start", new { args, cwd = Environment.CurrentDirectory });
    }

    public static void Info(string eventName, object? fields = null) =>
        _writer?.Write(DateTime.UtcNow, LogLevel.Info, eventName, fields);

    public static void Debug(string eventName, object? fields = null) =>
        _writer?.Write(DateTime.UtcNow, LogLevel.Debug, eventName, fields);

    public static void Error(Exception exception, string where) =>
        _writer?.Write(DateTime.UtcNow, LogLevel.Info, "error", new
        {
            where,
            type = exception.GetType().FullName,
            message = exception.Message,
            stack = exception.ToString(),
        });
}
