using nem.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Threading;

namespace nem.Commands.Store;

internal class StoreCommandList : Command<EmptyCommandSettings>
{
    protected override int Execute(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        EnvStore store = EnvStore.Default;
        var entries = store.Entries();
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[gray]The store at {Markup.Escape(store.StoreDir)} is empty.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("Kind")
            .AddColumn("Entry")
            .AddColumn("Last used");
        DateTime now = DateTime.UtcNow;
        foreach (EnvStore.Entry entry in entries)
        {
            string lastUsed = $"{entry.LastUsed.ToLocalTime():yyyy-MM-dd} ({(now - entry.LastUsed).TotalDays:0} days ago)";
            table.AddRow(
                entry.Kind,
                Markup.Escape(entry.Name) + (entry.Complete ? "" : " [yellow](incomplete)[/]"),
                lastUsed);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[gray]store: {Markup.Escape(store.StoreDir)}[/]");
        return 0;
    }
}
