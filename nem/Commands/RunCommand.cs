using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class RunCommandSettings : CommandSettings
{
    [CommandArgument(1, "<toolName>")]
    [Description("The folder path to where the env is in.")]
    public required string ToolName { get; init; }
}

internal class RunCommand : AsyncCommand<RunCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, RunCommandSettings settings, CancellationToken cancellationToken)
    {
        IOService.EnsureSystemDir();
        AnsiConsole.WriteLine("Proxied");
        ProxyService.CallToolInEnvContext(settings.ToolName, context.Remaining.Raw);
        return 0;
    }
} 