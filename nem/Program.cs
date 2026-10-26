using nem.Commands;
using nem.Commands.Store;
using nem.Commands.Tool;
using nem.Services;
using Spectre.Console.Cli;

namespace nem;

internal class Program
{
    static int Main(string[] args)
    {
        CommandLine commandLine = CommandLine.Split(args);
        ProxyMigrator.Run(commandLine.NemArguments);
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationVersion(ProxyMigrator.CurrentVersion);
            config.SetApplicationName("nem");

            config.AddCommand<AuditCommand>("audit")
                .WithDescription("Lists the known vulnerabilities in the env's installed tools. 'nem install' only prints the count.");

            config.AddCommand<InitCommand>("init")
                .WithDescription("Creates a nem env (a nem.jsonc) for a project.");

            config.AddCommand<InstallCommand>("install")
                .WithDescription("Puts the env's Node version and tools into the store and creates their proxies. Tools also do this on first use.");

            config.AddCommand<ResolveCommand>("resolve")
                .WithDescription("Used by the proxies: installs what the env lacks, then prints the tool to start and its environment, for a shell to run.")
                .IsHidden();

            config.AddCommand<RunCommand>("run")
                .WithDescription("Runs a tool from the nem env (or the system fallback). Tool arguments follow the tool name.")
                .WithData(commandLine.ToolArguments);

            config.AddCommand<SetupCommand>("setup")
                .WithDescription("Adds the nem proxy directory to your PATH. Only needs to be run once; '--uninstall' takes it back out.");

            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Updates the env: a Node version ('22'), a tool ('typescript', '@angular/cli@22'), or 'all'. Without arguments it reviews the available updates interactively.");

            config.AddCommand<WhichCommand>("which")
                .WithDescription("Explains which copy of a tool a typed name runs - global, env or a project copy - and why.");

            config.AddBranch("store", c =>
            {
                c.SetDescription("The store that holds the Node versions and tools of every env.");
                c.AddCommand<StoreCommandList>("list")
                    .WithDescription("Lists the store's entries and when each was last used.");
                c.AddCommand<StoreCommandPrune>("prune")
                    .WithDescription("Removes entries no tool start used for a while (default 180 days).");
            });

            config.AddBranch("tool", c =>
            {
                c.SetDescription("Manages the tools of the env of the current directory.");
                c.AddCommand<ToolCommandAdd>("add")
                    .WithDescription("Records an npm package in the nem config (it is installed by 'nem install'), e.g. 'nem tool add ts-node@10.9.0'.");
                c.AddCommand<ToolCommandRemove>("remove")
                    .WithDescription("Removes an npm package from the env.");
                c.AddCommand<ToolCommandList>("list")
                    .WithDescription("Lists the tools of the env.");
            });
        });

        return app.Run(commandLine.NemArguments);
    }
}
