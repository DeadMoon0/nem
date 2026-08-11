using nem.Common;
using nem.Common.Models;
using Newtonsoft.Json;
using Spectre.Console;
using System.IO;
using System.Threading.Tasks;

namespace nem.Services;

public static class IOService
{
    public static void InitEnv(string path, string version)
    {
        string configPath = Path.Combine(path, IOPathManager.CONFIG_FILE_NAME);
        if (File.Exists(configPath)) AnsiConsole.MarkupLine("[yellow]Skipped " + IOPathManager.CONFIG_FILE_NAME + "-File because it already exists.[/]");
        else File.WriteAllText(configPath, JsonConvert.SerializeObject(new NemConfig { NodeVersion = version }));
        
        string envPath = Path.Combine(path, IOPathManager.ENV_FOLDER_NAME);
        if (Directory.Exists(envPath)) AnsiConsole.MarkupLine("[yellow]Skipped " + IOPathManager.ENV_FOLDER_NAME + "-Folder because it already exists.[/]");
        else Directory.CreateDirectory(envPath);
    }
}