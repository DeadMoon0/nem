using nem.Common;
using nem.Common.Models;
using nem.Services;
using Newtonsoft.Json;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class InstallCommandSettings : CommandSettings
{
    [CommandArgument(1, "[path]")]
    [DefaultValue(".")]
    [Description("The folder path to where the env is in.")]
    public required string Path { get; init; }
    
    [CommandOption("-c|--clean")]
    [DefaultValue(false)]
    [Description("If set, will clean before Installing.")]
    public required bool Clean { get; init; }
}

internal class InstallCommand : AsyncCommand<InstallCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, InstallCommandSettings settings, CancellationToken cancellationToken)
    {
        IOService.EnsureSystemDir();
        NemConfig config = JsonConvert.DeserializeObject<NemConfig>(File.ReadAllText(IOPathManager.Local(Path.GetFullPath(settings.Path)).ConfigFilePath))!;
        await NodeDownloadingService.DownloadNodeVersion(config.NodeVersion, IOPathManager.Local(Path.GetFullPath(settings.Path)).EnsureEnvDirPath(), true);

        foreach (var tool in config.Tools)
        {

        }

        return 0;
    }
}