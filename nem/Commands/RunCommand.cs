using nem.Services;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace nem.Commands;

internal class RunCommandSettings : CommandSettings
{
    [CommandArgument(0, "<toolName>")]
    [Description("The tool to run. Pass its arguments after a '--' separator.")]
    public required string ToolName { get; init; }
}

internal class RunCommand : AsyncCommand<RunCommandSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, RunCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!IOService.EnsureSystemDir())
            return 1;

        return ProxyService.CallToolInEnvContext(settings.ToolName, context.Remaining.Raw);
    }
}
