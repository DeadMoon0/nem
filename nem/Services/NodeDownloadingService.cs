using nem.Common;
using Spectre.Console;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace nem.Services;

public static class NodeDownloadingService
{
    public static async Task DownloadNodeVersion(string version, string targetNodePath, bool cleanDownload)
    {
        string osArchitecture = GetOSArchitecture();
        string outputZipPath = Path.Combine(IOPathManager.System.DownloadCacheDirPath, $"node-v{version}-{osArchitecture}.zip");
        string extractPath = Path.Combine(IOPathManager.System.ExtractCacheDirPath);

        if (File.Exists(outputZipPath) && cleanDownload)
        {
            File.Delete(outputZipPath);
        }
        else if (File.Exists(outputZipPath))
        {
            AnsiConsole.MarkupLine("[Gray]Download cache hit for this Version. Skipping the Download.");
            return;
        }
        else
        {
            AnsiConsole.MarkupLine("[Gray] Starting download for node version: " + version + " ...[/]");
            string url = $"https://nodejs.org/dist/v{version}/node-v{version}-{osArchitecture}.zip";

            using HttpClient client = new HttpClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using (var fileStream = new FileStream(outputZipPath, FileMode.Create))
            {
                await contentStream.CopyToAsync(fileStream);
            }
            AnsiConsole.MarkupLine("[Gray] Download Successful![/]");

            AnsiConsole.MarkupLine("[Gray] Starting Extraction...[/]");
            string extractPathNode = Path.Combine(extractPath, $"node-v{version}-{osArchitecture}");
            if (Directory.Exists(extractPathNode)) Directory.Delete(extractPathNode, true);
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(outputZipPath, extractPath);
            AnsiConsole.MarkupLine("[Gray] Extraction Successful![/]");

            AnsiConsole.MarkupLine("[Gray] Starting to copy to .nenv dir...[/]");
            CopyFilesRecursively(extractPathNode, targetNodePath);
            AnsiConsole.MarkupLine("[Gray] Copy Successful![/]");
        }
    }

    private static void CopyFilesRecursively(string sourcePath, string targetPath)
    {
        //Now Create all of the directories
        foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
        }

        //Copy all the files & Replaces any files with the same name
        foreach (string newPath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
        }
    }

    private static string GetOSArchitecture()
    {
        // Determine OS and architecture
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Environment.Is64BitProcess ? "linux-x64" : "linux-x86";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "darwin-x64";
        else
            throw new PlatformNotSupportedException();
    }
}