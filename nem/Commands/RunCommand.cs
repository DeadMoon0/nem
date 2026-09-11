using nem.Services;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Threading;

namespace nem.Commands;

internal class RunCommandSettings : CommandSettings
{
    [CommandArgument(0, "<toolName>")]
    [Description("The tool to run. Pass its arguments after a '--' separator.")]
    public required string ToolName { get; init; }
}

internal class RunCommand : Command<RunCommandSettings>
{
    protected override int Execute(CommandContext context, RunCommandSettings settings, CancellationToken cancellationToken)
    {
        // Deliberately not gated on 'nem setup': running a tool needs only the env
        // (or the system fallback), not the proxies. This is the escape hatch the
        // PATH warnings point to, so it has to work when the PATH is not right.
        return ProxyService.CallToolInEnvContext(settings.ToolName, context.Remaining.Raw);
    }
}
