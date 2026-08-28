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
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        // Allow a hand-edited nem.json to carry a partial spec ("22"): resolve it
        // to the newest matching release before downloading.
        if (IsPartialVersionSpec(version))
            version = await ResolveNodeVersionAsync(version);

        (string tag, string archiveExtension) = GetPlatformPackage(version);
        string fileName = $"{tag}.{archiveExtension}";
        string url = $"https://nodejs.org/dist/v{version}/{fileName}";
        string archivePath = Path.Combine(IOPathManager.System.DownloadCacheDirPath, fileName);
        string extractDir = IOPathManager.System.ExtractCacheDirPath;
        string extractedNode = Path.Combine(extractDir, tag);
        string primaryBinary = PrimaryNodeBinary(envDir);

        if (clean)
        {
            DeleteIfExists(archivePath);
            DeleteDirectoryIfExists(extractedNode);
            WipeDirectory(envDir);
            AnsiConsole.MarkupLine("[gray]Cleaned previous install.[/]");
        }

        if (!File.Exists(archivePath))
            await DownloadAsync(url, archivePath);
        else
            AnsiConsole.MarkupLine($"[gray]Using cached {fileName}.[/]");

        if (!Directory.Exists(extractedNode))
        {
            AnsiConsole.MarkupLine("[gray]Starting extraction...[/]");
            Directory.CreateDirectory(extractDir);
            if (archiveExtension == "zip")
                ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
            else
                ExtractWithTar(archivePath, extractDir);
            AnsiConsole.MarkupLine("[gray]Extraction successful.[/]");
        }

        if (!File.Exists(primaryBinary))
        {
            AnsiConsole.MarkupLine("[gray]Starting to copy to .nenv dir...[/]");
            CopyFilesRecursively(extractedNode, envDir);
            EnsureUnixExecutableBits(envDir);
            AnsiConsole.MarkupLine("[green]Copy successful.[/]");
        }
        else
        {
            string? installedVersion = GetInstalledNodeVersion(envDir);
            if (installedVersion != null && VersionSpecMatches(version, installedVersion))
            {
                AnsiConsole.MarkupLine("[yellow]Node is already installed in the env, nothing to do.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(installedVersion != null
                    ? $"[gray]Node version changed: updating the env from [yellow]{installedVersion}[/] to [green]{version}[/]...[/]"
                    : $"[gray]The installed Node version could not be determined; re-copying Node {version} into the env...[/]");
                RemoveNodeDistribution(extractedNode, envDir);
                CopyFilesRecursively(extractedNode, envDir);
                EnsureUnixExecutableBits(envDir);
                AnsiConsole.MarkupLine("[green]Copy successful.[/]");
            }
        }
    }

    /// <summary>
    /// Runs the env's node to get its exact version (e.g. "22.23.2"), or null when
    /// it cannot be determined.
    /// </summary>
    public static string? GetInstalledNodeVersion(string envDir)
    {
        string node = PrimaryNodeBinary(envDir);
        if (!File.Exists(node))
            return null;

        try
        {
            var psi = new ProcessStartInfo(node)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = envDir,
            };
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add("process.versions.node");

            using var process = Process.Start(psi);
            if (process == null)
                return null;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the files of the Node distribution (as shipped in the extracted zip)
    /// from the env, so an older Node version does not leave stale files behind.
    /// </summary>
    static void RemoveNodeDistribution(string extractedNode, string envDir)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(extractedNode))
        {
            string dest = Path.Combine(envDir, Path.GetFileName(entry));
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            else if (File.Exists(dest))
                File.Delete(dest);
        }
    }

    // ---- Node.js version validation / resolution (nodejs.org) ----

    static List<string>? _availableVersions;

    /// <summary>
    /// Normalizes a version spec: trims whitespace, a leading "v" and a trailing ".".
    /// </summary>
    public static string NormalizeVersion(string spec)
    {
        string s = spec.Trim();
        if (s.Length > 0 && char.ToLowerInvariant(s[0]) == 'v')
            s = s[1..];
        return s.TrimEnd('.');
    }

    /// <summary>
    /// True for specs with one or two numeric parts ("22", "22.0") that match a whole
    /// range of releases instead of exactly one.
    /// </summary>
    public static bool IsPartialVersionSpec(string spec)
    {
        string s = NormalizeVersion(spec);
        if (s.Length == 0)
            return false;

        bool numericOnly = true;
        int dots = 0;
        foreach (char c in s)
        {
            if (c == '.') dots++;
            else if (!char.IsDigit(c)) { numericOnly = false; break; }
        }
        return numericOnly && dots <= 1;
    }

    /// <summary>
    /// True when a (possibly partial) version spec matches a fully resolved version:
    /// "22" matches "22.23.2" but "22.3" does not. Exact specs compare as versions.
    /// </summary>
    public static bool VersionSpecMatches(string spec, string resolved)
    {
        string s = NormalizeVersion(spec);
        if (s.Length == 0)
            return true;

        if (IsPartialVersionSpec(s))
            return resolved.StartsWith(s + ".", System.StringComparison.Ordinal);
        return s.Equals(resolved, System.StringComparison.OrdinalIgnoreCase);
    }

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
            string? newest = NewestStableVersion(versions, input);
            if (newest == null)
                throw new InvalidOperationException($"No Node.js release matches '{spec.Trim()}' (checked https://nodejs.org/dist). See https://nodejs.org/en/download for available versions.");
            return newest;
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
    /// Returns the newest stable (official, non-RC) Node.js release, e.g. "26.6.0".
    /// Throws InvalidOperationException when the nodejs.org index cannot be reached.
    /// </summary>
    public static async Task<string> GetLatestStableNodeVersionAsync()
    {
        List<string> versions = await GetAvailableVersionsAsync();
        return NewestStableVersion(versions)
            ?? throw new InvalidOperationException("The nodejs.org version index lists no stable releases.");
    }

    /// <summary>
    /// The newest stable release (no leading 'v') from a nodejs.org tag list.
    /// Tags with a '-' (nightly/beta/rc) are skipped; when <paramref name="prefix"/>
    /// is given, only releases starting with that version prefix qualify ("22" ->
    /// 22.x). Returns null when nothing qualifies.
    /// </summary>
    internal static string? NewestStableVersion(IEnumerable<string> versions, string? prefix = null)
    {
        string? requiredPrefix = prefix == null ? null : NormalizeVersion(prefix) + ".";
        string? best = null;
        foreach (string v in versions)
        {
            if (v.Length <= 1 || v[0] != 'v')
                continue;
            string candidate = v[1..];
            if (candidate.Contains('-'))
                continue; // official releases only
            if (requiredPrefix != null && !candidate.StartsWith(requiredPrefix, StringComparison.Ordinal))
                continue;
            if (best == null || CompareVersions(candidate, best) > 0)
                best = candidate;
        }
        return best;
    }

    /// <summary>
    /// A spec is partial (resolve to newest matching release) when it is
    /// "major" or "major.minor" with numeric parts; anything else is exact.
    /// </summary>
    internal static bool IsPartialSpec(string input)
    {
        string[] parts = input.Split('.');
        if (parts.Length > 2)
            return false;
        return parts.All(p => p.Length > 0 && p.All(char.IsDigit));
    }

    internal static int CompareVersions(string a, string b)
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

        string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        string? expectedSha256 = await FetchExpectedSha256(url, fileName);
        if (expectedSha256 != null)
        {
            string actualSha256 = ComputeSha256(tmpPath);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tmpPath);
                throw new IOException($"SHA256 mismatch for {fileName}: expected {expectedSha256}, got {actualSha256}. Delete the cached file and try again.");
            }
        }

        File.Move(tmpPath, destPath, overwrite: true);
        AnsiConsole.MarkupLine("[gray]Download successful![/]");
    }

    /// <summary>
    /// Fetches the SHASUMS256.txt next to the given file and returns the expected
    /// SHA256 for fileName. Returns null (with a warning) when the list cannot be
    /// fetched or parsed, so a flaky list never blocks an install.
    /// </summary>
    static async Task<string?> FetchExpectedSha256(string fileUrl, string fileName)
    {
        string shasumsUrl = fileUrl.Replace(fileName, "SHASUMS256.txt");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.GetAsync(shasumsUrl);
            response.EnsureSuccessStatusCode();
            string shasums = await response.Content.ReadAsStringAsync();
            return ParseSha256(shasums, fileName);
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine("[yellow]Could not fetch SHASUMS256.txt, so the download is used without checksum verification.[/]");
            return null;
        }
    }

    internal static string? ParseSha256(string shasumsText, string fileName)
    {
        foreach (string line in shasumsText.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            string name = parts[^1].TrimStart('*');
            if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{64}$"))
                return parts[0];
        }

        return null;
    }

    static string ComputeSha256(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>
    /// The nodejs.org distribution tag and archive extension for this platform.
    /// Windows ships .zip archives; Linux and macOS ship .tar.xz.
    /// </summary>
    static (string Tag, string ArchiveExtension) GetPlatformPackage(string version) =>
        GetPlatformPackage(
            version,
            RuntimeInformation.ProcessArchitecture,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsLinux(),
            OperatingSystem.IsMacOS());

    /// <summary>
    /// Maps a version plus platform facts to the nodejs.org dist package name.
    /// </summary>
    internal static (string Tag, string ArchiveExtension) GetPlatformPackage(
        string version,
        Architecture processArchitecture,
        bool isWindows,
        bool isLinux,
        bool isMacOS)
    {
        string platform;
        string arch;
        string extension;
        if (isWindows)
        {
            if (processArchitecture == Architecture.Arm64)
                throw new NotSupportedException("ARM64 Windows is not supported by nem yet.");
            platform = "win";
            arch = Environment.Is64BitProcess ? "x64" : "x86";
            extension = "zip";
        }
        else if (isLinux)
        {
            platform = "linux";
            arch = processArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            extension = "tar.xz";
        }
        else if (isMacOS)
        {
            platform = "darwin";
            arch = processArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            extension = "tar.xz";
        }
        else
        {
            throw new NotSupportedException("nem does not support downloading Node for this operating system.");
        }
        return ($"node-v{version}-{platform}-{arch}", extension);
    }

    /// <summary>
    /// Extracts a .tar.xz archive with the system 'tar' (tar auto-detects xz).
    /// </summary>
    static void ExtractWithTar(string archivePath, string targetDir)
    {
        var psi = new ProcessStartInfo("tar")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-xf");
        psi.ArgumentList.Add(archivePath);
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(targetDir);

        using var process = Process.Start(psi) ?? throw new IOException("Could not start the 'tar' process.");
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException($"Failed to extract {Path.GetFileName(archivePath)} with 'tar': {stderr.Trim()}");
    }

    /// <summary>
    /// Restores the executable bit on the env's bin directory (Unix). File copies
    /// of the Node distribution (node, npm, npx, ...) would otherwise lose their
    /// executable bit, and the symlinks are flattened to regular files.
    /// </summary>
    static void EnsureUnixExecutableBits(string envDir)
    {
        if (OperatingSystem.IsWindows())
            return;

        string binDir = NodeEnvLayout.Create(envDir).BinDir;
        if (!Directory.Exists(binDir))
            return;

        UnixFileMode executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        foreach (string file in Directory.EnumerateFiles(binDir))
            File.SetUnixFileMode(file, executable);
    }

    /// <summary>
    /// node.exe (Windows) or bin/node (Unix) inside the env directory.
    /// </summary>
    public static string PrimaryNodeBinary(string envDir) =>
        NodeEnvLayout.Create(envDir).NodeBinary;

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
