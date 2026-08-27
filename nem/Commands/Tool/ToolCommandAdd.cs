using nem.Services;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Threading;

namespace nem.Commands.Tool;

internal class ToolAddCommandSettings : CommandSettings
{
    [CommandArgument(0, "<package>")]
    [Description("The npm package to install into the env, e.g. 'ts-node' or 'ts-node@10.9.0'.")]
    public required string Package { get; init; }
}

internal class ToolCommandAdd : Command<ToolAddCommandSettings>
{
    protected override int Execute(CommandContext context, ToolAddCommandSettings settings, CancellationToken cancellationToken)
    {
        return ToolService.Add(settings.Package);
    }
}
