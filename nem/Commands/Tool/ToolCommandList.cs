using nem.Services;
using Spectre.Console.Cli;
using System.Threading;

namespace nem.Commands.Tool;

internal class ToolListCommandSettings : CommandSettings
{
}

internal class ToolCommandList : Command<ToolListCommandSettings>
{
    protected override int Execute(CommandContext context, ToolListCommandSettings settings, CancellationToken cancellationToken)
    {
        return ToolService.List();
    }
}
