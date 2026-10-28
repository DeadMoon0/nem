using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nem.Services.Diagnostics;

/// <summary>Where a report looks; the real folders, or test folders.</summary>
public sealed record ReportInputs(
    string Cwd,
    string ProxyDir,
    string TemplatesDir,
    EnvStore Store,
    string LogDir,
    int LogDays,
    string? Tool,
    DateTime Now);

/// <summary>
/// Everything needed to understand a problem on another machine, as plain files:
/// a readable summary, the logs, and snapshots of the system, the proxies, the
/// store and the env of the folder it was taken in. Looks only - it never installs,
/// migrates or refreshes anything, so the report shows the machine as it was.
/// Secrets are removed from every file (<see cref="Redactor"/>).
/// </summary>
public static class DiagnosticReport
{
    /// <summary>One file of the report: its path inside the zip and its text.</summary>
    public sealed record ReportFile(string Path, string Content);

    static readonly string[] VariablePrefixes = ["NEM_", "NODE_", "npm_config_"];

    public static IReadOnlyList<ReportFile> Collect(ReportInputs inputs)
    {
        var findings = new List<string>();
        JToken system = Section(() => System(inputs, findings));
        JToken proxies = Section(() => Proxies(inputs, findings));
        JToken store = Section(() => StoreSnapshot(inputs, findings));
        JToken env = Section(() => Env(inputs, findings));
        List<ReportFile> logs = Logs(inputs);
        IReadOnlyList<string> recentErrors = RecentErrors(logs, inputs.Now);

        var files = new List<ReportFile>
        {
            new("report.md", Summary(inputs, findings, env, recentErrors, logs.Count)),
            new("system.json", Json(system)),
            new("proxies.json", Json(proxies)),
            new("store.json", Json(store)),
            new("env.json", Json(env)),
        };
        files.AddRange(logs);
        return files.Select(f => f with { Content = Redactor.Redact(f.Content) }).ToList();
    }

    /// <summary>Writes the files into a zip at <paramref name="zipPath"/>.</summary>
    public static void WriteZip(IReadOnlyList<ReportFile> files, string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (ReportFile file in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(file.Path).Open(), new UTF8Encoding(false));
            writer.Write(file.Content);
        }
    }

    public static string ZipFileName(DateTime now) =>
        $"nem-report-{Environment.MachineName}-{now.ToLocalTime():yyyyMMdd-HHmmss}.zip";

    // A part that fails says so in its file instead of costing the whole report.
    static JToken Section(Func<JToken> collect)
    {
        try
        {
            return collect();
        }
        catch (Exception e)
        {
            return new JObject { ["error"] = e.ToString() };
        }
    }

    static string Json(JToken token) => token.ToString(Formatting.Indented) + "\n";

    // ---------- sections ----------

    static JToken System(ReportInputs inputs, List<string> findings)
    {
        PackageRedirection? redirection = PackageRedirection.Detect();
        if (redirection != null)
            findings.Add(redirection.Warning);

        return new JObject
        {
            ["packageRedirection"] = redirection == null ? null : new JObject
            {
                ["package"] = redirection.Package,
                ["redirectedNemDir"] = redirection.RedirectedNemDir,
            },
            ["nemVersion"] = ProxyMigrator.CurrentVersion,
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["dotnet"] = RuntimeInformation.FrameworkDescription,
            ["machine"] = Environment.MachineName,
            ["user"] = Environment.UserName,
            ["taken"] = inputs.Now.ToString("o"),
            ["cwd"] = inputs.Cwd,
            ["processPath"] = new JArray(PathPrecedence.ProcessPathDirs()),
            ["storedPath"] = new JArray(PathPrecedence.StoredPathDirs()),
            ["variables"] = new JObject(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .Select(v => (Name: v.Key.ToString()!, Value: v.Value?.ToString() ?? ""))
                .Where(v => VariablePrefixes.Any(p => v.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v => new JProperty(v.Name, v.Value))),
        };
    }

    static JToken Proxies(ReportInputs inputs, List<string> findings)
    {
        if (!Directory.Exists(inputs.ProxyDir))
        {
            findings.Add($"The proxy directory {inputs.ProxyDir} does not exist - 'nem setup' never ran.");
            return new JObject { ["dir"] = inputs.ProxyDir, ["exists"] = false };
        }

        string markerPath = Path.Combine(inputs.ProxyDir, ProxyMigrator.MarkerFileName);
        string? marker = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
        var files = new JArray();
        var stale = new List<string>();
        foreach (string file in Directory.EnumerateFiles(inputs.ProxyDir).Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;
            string? template = TemplateFor(name);
            string content = File.ReadAllText(file);
            bool? current = template == null ? null : content == File.ReadAllText(Path.Combine(inputs.TemplatesDir, template));
            if (current == false)
                stale.Add(name);
            files.Add(new JObject
            {
                ["name"] = name,
                ["calls"] = content.Contains("nem resolve") ? "nem resolve" : content.Contains("nem run") ? "nem run" : "other",
                ["matchesTemplate"] = current,
            });
        }

        bool onPath = PathPrecedence.ProcessPathDirs().Any(d => PathPrecedence.SamePath(d, inputs.ProxyDir));
        var proxied = files.Select(f => Path.GetFileNameWithoutExtension((string)f["name"]!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var shadows = onPath ? PathPrecedence.FindShadowsOnProcessPath(proxied) : [];

        if (marker != ProxyMigrator.CurrentVersion)
            findings.Add($"The proxies were last brought up to date by nem {marker ?? "(unknown)"}, this is {ProxyMigrator.CurrentVersion}.");
        if (stale.Count > 0)
            findings.Add($"{stale.Count} proxies differ from this nem's templates: {string.Join(", ", stale)}.");
        if (!onPath)
            findings.Add("The proxy directory is not on this terminal's PATH - typed tool names never reach nem.");
        foreach (PathPrecedence.Shadow shadow in shadows)
            findings.Add(shadow.Reason == PathPrecedence.Unreachable.NoProxy
                ? $"'{shadow.ToolName}' has no proxy."
                : $"'{shadow.ToolName}' is answered by {shadow.ShadowingDir} before the nem proxy.");

        return new JObject
        {
            ["dir"] = inputs.ProxyDir,
            ["marker"] = marker,
            ["onProcessPath"] = onPath,
            ["files"] = files,
            ["shadows"] = new JArray(shadows.Select(s => new JObject { ["tool"] = s.ToolName, ["reason"] = s.Reason.ToString(), ["answeredBy"] = s.ShadowingDir })),
        };
    }

    static string? TemplateFor(string proxyName) => Path.GetExtension(proxyName).ToLowerInvariant() switch
    {
        ".bat" => "NAME.bat",
        ".ps1" => "NAME.ps1",
        "" => "NAME",
        _ => null,
    };

    static JToken StoreSnapshot(ReportInputs inputs, List<string> findings)
    {
        string markerPath = Path.Combine(inputs.Store.StoreDir, StoreMigrator.MarkerFileName);
        var entries = inputs.Store.Entries();
        foreach (EnvStore.Entry entry in entries.Where(e => !e.Complete))
            findings.Add($"Store entry {entry.Kind} {entry.Name} is incomplete (an install that never finished).");

        return new JObject
        {
            ["dir"] = inputs.Store.StoreDir,
            ["format"] = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : null,
            ["entries"] = new JArray(entries.Select(e => new JObject
            {
                ["kind"] = e.Kind,
                ["name"] = e.Name,
                ["complete"] = e.Complete,
                ["lastUsed"] = e.LastUsed.ToString("o"),
            })),
            ["globals"] = Directory.Exists(inputs.Store.GlobalsDir)
                ? new JArray(Directory.EnumerateDirectories(inputs.Store.GlobalsDir).Select(Path.GetFileName))
                : new JArray(),
        };
    }

    static JToken Env(ReportInputs inputs, List<string> findings)
    {
        if (!IOPathManager.TryFindEnv(inputs.Cwd, out IOPathManager.IOPathManagerEnv? env))
        {
            return new JObject { ["found"] = false, ["which"] = Which(inputs, ["npm", "npx", "node", .. Extra(inputs)]) };
        }

        var result = new JObject
        {
            ["found"] = true,
            ["dir"] = env.DirPath,
            ["config"] = File.ReadAllText(env.ConfigFilePath),
        };

        NemConfig config = NemConfigFile.Read(env.ConfigFilePath);
        EnvComposition composition;
        switch (EnvComposition.Compose(config, inputs.Store))
        {
            case EnvComposition.Result.Ok ok:
                composition = ok.Env;
                break;
            case EnvComposition.Result.Invalid invalid:
                findings.Add($"The env's config cannot be used: {invalid.Reason}.");
                result["invalid"] = invalid.Reason;
                return result;
            default:
                throw new InvalidOperationException("EnvComposition.Result has exactly two cases.");
        }

        if (!composition.IsInstalled)
            findings.Add($"The store lacks what this env declares: {string.Join(", ", composition.Missing)} (the next tool start installs it).");

        var names = composition.Tools
            .SelectMany(t => EnvStore.IsComplete(t.Dir) ? ToolService.ReadToolBins(t.Dir, t.Name) : [ToolBins.DefaultBinName(t.Name)])
            .Concat(["npm", "npx", "node", .. Extra(inputs)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result["node"] = new JObject { ["version"] = composition.NodeVersion, ["dir"] = composition.NodeDir, ["complete"] = EnvStore.IsComplete(composition.NodeDir) };
        result["tools"] = new JArray(composition.Tools.Select(t => new JObject { ["name"] = t.Name, ["version"] = t.Version, ["dir"] = t.Dir, ["complete"] = EnvStore.IsComplete(t.Dir) }));
        result["missing"] = new JArray(composition.Missing);
        result["globals"] = composition.GlobalsDir;
        result["which"] = Which(inputs, names);
        if (EnvStore.IsComplete(composition.NodeDir))
        {
            result["npm"] = new JObject
            {
                ["registry"] = ToolService.RunNpmCapture(composition.NodeLayout, ["config", "get", "registry"]),
                ["prefix"] = ToolService.RunNpmCapture(composition.NodeLayout, ["config", "get", "prefix"]),
            };
        }
        return result;
    }

    static string[] Extra(ReportInputs inputs) => inputs.Tool == null ? [] : [inputs.Tool];

    /// <summary>What each name runs in this folder, with the chain behind it - as 'nem which' shows it.</summary>
    static JObject Which(ReportInputs inputs, IEnumerable<string> names)
    {
        var which = new JObject();
        foreach (string name in names)
        {
            ToolResolver.ToolResolution resolution = ToolResolver.Resolve(inputs.Cwd, name, inputs.Store);
            var entry = new JObject
            {
                ["source"] = resolution.Source.ToString(),
                ["typedNameReachesNem"] = resolution.TypedNameReachesNem,
                ["runs"] = resolution.ExecutablePath,
                ["chain"] = new JArray(resolution.Chain.Select(step => $"{(step.Decided ? "> " : "  ")}{step.Question}: {step.Finding}")),
            };
            if (name == inputs.Tool && resolution.Env != null)
            {
                entry["variables"] = new JObject(ToolLaunch.EnvironmentFor(resolution.Env, Environment.GetEnvironmentVariable("PATH"))
                    .Select(v => new JProperty(v.Key, v.Value)));
            }
            which[name] = entry;
        }
        return which;
    }

    static List<ReportFile> Logs(ReportInputs inputs)
    {
        if (!Directory.Exists(inputs.LogDir))
            return [];
        DateTime from = inputs.Now.Date.AddDays(-inputs.LogDays);
        return Directory.EnumerateFiles(inputs.LogDir)
            .Where(f => LogWriter.DayOf(Path.GetFileName(f)) is { } day && day >= from)
            .Order(StringComparer.Ordinal)
            .Select(f => new ReportFile($"logs/{Path.GetFileName(f)}", ReadShared(f)))
            .ToList();
    }

    // Another nem may be writing a line to today's log right now; it holds the
    // file only for that moment.
    static string ReadShared(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < LogWriter.WriteAttempts)
            {
                global::System.Threading.Thread.Sleep(LogWriter.WriteRetryDelay);
            }
        }
    }

    /// <summary>The errors logged in the last day, newest first, as "time  where: message".</summary>
    internal static IReadOnlyList<string> RecentErrors(IEnumerable<ReportFile> logs, DateTime now) =>
        logs.SelectMany(log => log.Content.Split('\n'))
            .Select(ParseLogLine)
            .OfType<JObject>()
            .Where(line => (string?)line["event"] == "error"
                && DateTime.TryParse((string?)line["t"], null, global::System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t)
                && now - t < TimeSpan.FromDays(1))
            .Select(line => $"{(string?)line["t"]}  {(string?)line["where"]}: {(string?)line["message"]}")
            .Reverse()
            .ToList();

    // Dates stay text: the log writes them as round-trip strings, and reading them
    // as DateTime would format them back in this machine's culture.
    static JObject? ParseLogLine(string line)
    {
        if (line.Length == 0)
            return null;
        try
        {
            using var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string Summary(ReportInputs inputs, List<string> findings, JToken env, IReadOnlyList<string> recentErrors, int logFiles)
    {
        var md = new StringBuilder();
        md.AppendLine("# nem report");
        md.AppendLine();
        md.AppendLine($"- nem {ProxyMigrator.CurrentVersion} on {RuntimeInformation.OSDescription} ({Environment.MachineName})");
        md.AppendLine($"- taken {inputs.Now.ToLocalTime():yyyy-MM-dd HH:mm:ss} in `{inputs.Cwd}`");
        md.AppendLine($"- env: {((bool?)env["found"] == true ? $"`{(string?)env["dir"]}`" : "none - tools fall back to the system")}");
        md.AppendLine();

        md.AppendLine("## Conspicuous");
        md.AppendLine();
        if (findings.Count == 0)
            md.AppendLine("Nothing - proxies, store and env look as they should.");
        foreach (string finding in findings)
            md.AppendLine($"- {finding}");
        md.AppendLine();

        if (env["which"] is JObject which)
        {
            md.AppendLine("## What a typed name runs here");
            md.AppendLine();
            md.AppendLine("| Name | Copy | Runs |");
            md.AppendLine("| --- | --- | --- |");
            foreach (JProperty name in which.Properties())
                md.AppendLine((bool?)name.Value["typedNameReachesNem"] == false
                    ? $"| `{name.Name}` | typed, it never reaches nem: {(string?)name.Value["chain"]?[0]?.ToString().Split(": ", 2)[^1]} | `nem run {name.Name}` would run `{(string?)name.Value["runs"] ?? "-"}` |"
                    : $"| `{name.Name}` | {(string?)name.Value["source"]} | `{(string?)name.Value["runs"] ?? "-"}` |");
            md.AppendLine();
        }

        md.AppendLine("## Errors in the last 24 hours");
        md.AppendLine();
        if (recentErrors.Count == 0)
            md.AppendLine("None logged.");
        foreach (string error in recentErrors.Take(10))
            md.AppendLine($"- {error}");
        md.AppendLine();

        md.AppendLine("## Files");
        md.AppendLine();
        md.AppendLine("- `system.json` - OS, .NET, PATH (this terminal's and the stored one), nem-related variables");
        md.AppendLine("- `proxies.json` - every proxy, whether it matches this nem's template, which PATH entry wins");
        md.AppendLine("- `store.json` - every store entry, whether it is complete, when it was last used");
        md.AppendLine("- `env.json` - the env of this folder: config, store entries, the 'nem which' chain per name");
        md.AppendLine($"- `logs/` - {logFiles} day(s) of nem's log, one JSON line per event");
        md.AppendLine();
        md.AppendLine($"Secrets (npm auth tokens, passwords, credentials in URLs) are replaced by `{Redactor.Mask}`.");
        return md.ToString();
    }
}
