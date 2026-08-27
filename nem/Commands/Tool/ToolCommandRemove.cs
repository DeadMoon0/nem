using nem.Services;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Threading;

namespace nem.Commands.Tool;

internal class ToolRemoveCommandSettings : CommandSettings
{
    [CommandArgument(0, "<toolName>")]
    [Description("The npm package to remove from the env.")]
    public required string ToolName { get; init; }
}

internal class ToolCommandRemove : Command<ToolRemoveCommandSettings>
{
    protected override int Execute(CommandContext context, ToolRemoveCommandSettings settings, CancellationToken cancellationToken)
    {
        return ToolService.Remove(settings.ToolName);
    }
}
