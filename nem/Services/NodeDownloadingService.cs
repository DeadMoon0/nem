using nem.Common;
using Spectre.Console;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
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
