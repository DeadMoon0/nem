using nem.Common;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace nem.Services;

public static class NodeDownloadingService
{
    /// <summary>
    /// Downloads (or reuses a cached) Node.js version and copies it into the given env directory.
    /// </summary>
    public static async Task InstallNodeAsync(string version, string envDir, bool clean)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A node version is required.", nameof(version));

        string tag = GetPlatformTag(version);
        string fileName = $"{tag}.zip";
        string url = $"https://nodejs.org/dist/v{version}/{fileName}";
        string zipPath = Path.Combine(IOPathManager.System.DownloadCacheDirPath, fileName);
        string extractDir = IOPathManager.System.ExtractCacheDirPath;
        string extractedNode = Path.Combine(extractDir, tag);
        string primaryBinary = PrimaryNodeBinary(envDir);

        if (clean)
        {
            DeleteIfExists(zipPath);
            DeleteDirectoryIfExists(extractedNode);
            WipeDirectory(envDir);
            AnsiConsole.MarkupLine("[gray]Cleaned previous install.[/]");
        }

        if (!File.Exists(zipPath))
            await DownloadAsync(url, zipPath);
        else
            AnsiConsole.MarkupLine($"[gray]Using cached {fileName}.[/]");

        if (!Directory.Exists(extractedNode))
        {
            AnsiConsole.MarkupLine("[gray]Starting extraction...[/]");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            AnsiConsole.MarkupLine("[gray]Extraction successful.[/]");
        }

        if (!File.Exists(primaryBinary))
        {
            AnsiConsole.MarkupLine("[gray]Starting to copy to .nenv dir...[/]");
            CopyFilesRecursively(extractedNode, envDir);
            AnsiConsole.MarkupLine("[green]Copy successful.[/]");
        }
        else if (!clean)
        {
            AnsiConsole.MarkupLine("[yellow]Node is already installed in the env, nothing to do.[/]");
        }
    }

    // ---- Node.js version validation / resolution (nodejs.org) ----

    static List<string>? _availableVersions;

    /// <summary>
    /// Validates a full Node.js version (e.g. "18.12.0") against nodejs.org and
    /// resolves partial specs ("22", "18.12") to the newest matching release.
    /// Returns the version without a leading "v". Throws InvalidOperationException
    /// with a user-facing message on failure.
    /// </summary>
    public static async Task<string> ResolveNodeVersionAsync(string spec)
    {
        string input = spec.Trim();
        if (input.Length > 0 && char.ToLowerInvariant(input[0]) == 'v')
            input = input[1..];
        input = input.TrimEnd('.');
        if (input.Length == 0)
            throw new InvalidOperationException("A node version is required.");

        if (IsPartialSpec(input))
        {
            List<string> versions = await GetAvailableVersionsAsync();
            string prefix = input + ".";
            string? best = null;
            foreach (string v in versions)
            {
                if (v.Length <= 1 || v[0] != 'v')
                    continue;
                string candidate = v[1..];
                if (candidate.Contains('-'))
                    continue; // official releases only
                if (candidate.StartsWith(prefix, System.StringComparison.Ordinal) &&
                    (best == null || CompareVersions(candidate, best) > 0))
                    best = candidate;
            }

            if (best == null)
                throw new InvalidOperationException($"No Node.js release matches '{spec.Trim()}' (checked https://nodejs.org/dist). See https://nodejs.org/en/download for available versions.");
            return best;
        }

        // Full spec: it must exist.
        bool listed;
        try
        {
            listed = (await GetAvailableVersionsAsync()).Contains("v" + input, System.StringComparer.Ordinal);
        }
        catch (InvalidOperationException)
        {
            listed = false; // index unreachable; try the direct check below
        }
        if (listed)
            return input;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync($"https://nodejs.org/dist/v{input}/", HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
                return input; // e.g. an RC that is not listed in the index
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Could not reach https://nodejs.org to validate the node version.");
        }

        throw new InvalidOperationException($"Node.js version '{spec.Trim()}' does not exist. See https://nodejs.org/en/download for available versions.");
    }

    /// <summary>
    /// A spec is partial (resolve to newest matching release) when it is
    /// "major" or "major.minor" with numeric parts; anything else is exact.
    /// </summary>
    static bool IsPartialSpec(string input)
    {
        string[] parts = input.Split('.');
        if (parts.Length > 2)
            return false;
        return parts.All(p => p.Length > 0 && p.All(char.IsDigit));
    }

    static int CompareVersions(string a, string b)
    {
        int[] pa = a.Split('.').Select(int.Parse).ToArray();
        int[] pb = b.Split('.').Select(int.Parse).ToArray();
        for (int i = 0; i < 3; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    static async Task<List<string>> GetAvailableVersionsAsync()
    {
        if (_availableVersions != null)
            return _availableVersions;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        string json;
        try
        {
            json = await client.GetStringAsync("https://nodejs.org/dist/index.json");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Could not reach https://nodejs.org/dist (is the network available?)", e);
        }

        try
        {
            List<string> versions = new();
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                versions.Add(entry.GetProperty("version").GetString()!);
            _availableVersions = versions;
            return versions;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Could not parse the nodejs.org version index.", e);
        }
    }

    static async Task DownloadAsync(string url, string destPath)
    {
        AnsiConsole.MarkupLine($"[gray]Starting download for node version from {url}...[/]");

        var tmpPath = destPath + ".part";
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            using var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;

            var progress = AnsiConsole.Progress();

            await progress.StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Downloading[/]");
                if (total > 0) task.MaxValue = total;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(tmpPath);
                var buffer = new byte[1 << 16];
                long read = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    read += bytesRead;
                    task.Value = read;
                    if (total > 0) task.Description = $"[green]Downloading[/] {read / 1024} / {total / 1024} KB";
                    else task.Description = $"[green]Downloading[/] {read / 1024} KB";
                }
            });
        }

        File.Move(tmpPath, destPath, overwrite: true);
        AnsiConsole.MarkupLine("[gray]Download successful![/]");
    }

    static string GetPlatformTag(string version)
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException("nem currently only supports downloading Node for Windows.");

        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            throw new NotSupportedException("ARM64 Windows is not supported by nem yet.");

        string arch = Environment.Is64BitProcess ? "x64" : "x86";
        return $"node-v{version}-win-{arch}";
    }

    /// <summary>
    /// node.exe (Windows) or bin/node (Unix) inside the env directory.
    /// </summary>
    public static string PrimaryNodeBinary(string envDir)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(envDir, "node.exe")
            : Path.Combine(envDir, "bin", "node");
    }

    static void CopyFilesRecursively(string sourcePath, string targetPath)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(sourcePath, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourcePath, path);

            // install_tools.bat is a legacy VS helper from the Node zip; not needed in the env.
            if (rel.StartsWith("install_tools.bat", StringComparison.OrdinalIgnoreCase))
                continue;

            string dest = Path.Combine(targetPath, rel);
            if (Directory.Exists(path))
                Directory.CreateDirectory(dest);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(path, dest, overwrite: true);
            }
        }
    }

    static void WipeDirectory(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            return;

        foreach (string entry in Directory.EnumerateFileSystemEntries(dirPath))
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }
    }

    static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    static void DeleteDirectoryIfExists(string dirPath)
    {
        if (Directory.Exists(dirPath))
            Directory.Delete(dirPath, recursive: true);
    }
}
