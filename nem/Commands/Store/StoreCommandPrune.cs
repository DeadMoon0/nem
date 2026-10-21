using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.Threading;

namespace nem.Commands.Store;

internal class StorePruneCommandSettings : CommandSettings
{
    [CommandOption("--older-than <days>")]
    [Description("Removes entries no tool start used for this many days. 0 removes every entry not in use right now.")]
    public int? OlderThanDays { get; init; }

    public override ValidationResult Validate() =>
        OlderThanDays is < 0
            ? ValidationResult.Error("--older-than takes a number of days, 0 or more.")
            : ValidationResult.Success();
}

internal class StoreCommandPrune : Command<StorePruneCommandSettings>
{
    protected override int Execute(CommandContext context, StorePruneCommandSettings settings, CancellationToken cancellationToken)
    {
        TimeSpan maxAge = settings.OlderThanDays is { } days ? TimeSpan.FromDays(days) : EnvironmentInstaller.PruneAfter;
        EnvironmentInstaller.PruneStore(EnvStore.Default, DateTime.UtcNow, maxAge, quiet: false);
        return 0;
    }
}
