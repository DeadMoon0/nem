using nem.Commands;
using nem.Commands.Tool;
using Spectre.Console.Cli;
using System.Reflection;

namespace nem;

internal class Program
{
    static int Main(string[] args)
    {
        // 'nem run <tool> <args...>': everything after the tool name is an argument
        // for the tool, but the CLI parser stops passing options through at the
        // first '--'. Normalize 'nem run -- <tool> ...' and 'nem run <tool> ...'
        // to the form the parser needs: 'nem run <tool> -- <args...>'.
        if (args.Length > 1 && string.Equals(args[0], "run", System.StringComparison.OrdinalIgnoreCase))
        {
            string[] rest = args[1..];
            if (rest.Length > 0 && rest[0] == "--")
                rest = rest[1..];
            if (rest.Length > 1 && rest[1] != "--")
                rest = [rest[0], "--", .. rest[1..]];
            args = [args[0], .. rest];
        }

        var app = new CommandApp();

        app.Configure(config =>
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "unknown";

            config.SetApplicationVersion(version);
            config.SetApplicationName("nem");

            config.AddCommand<InitCommand>("init")
                .WithDescription("Creates a nem env (nem.json + .nenv folder) for a project.");

            config.AddCommand<InstallCommand>("install")
                .WithDescription("Downloads/copies the Node version and installs tool proxies from nem.json.");

            config.AddCommand<RunCommand>("run")
                .WithDescription("Runs a tool from the nem env (or the system fallback). Tool arguments follow the tool name.");

            config.AddCommand<SetupCommand>("setup")
                .WithDescription("Adds the nem proxy directory to your PATH. Only needs to be run once.");

            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Updates the env: a Node version ('22'), a tool ('typescript', '@angular/cli@22'), or 'all'. Without arguments it reviews the available updates interactively.");

            config.AddBranch("tool", c =>
            {
                c.SetDescription("Manages the tools of the env of the current directory.");
                c.AddCommand<ToolCommandAdd>("add")
                    .WithDescription("Records an npm package in nem.json (it is installed by 'nem install'), e.g. 'nem tool add ts-node@10.9.0'.");
                c.AddCommand<ToolCommandRemove>("remove")
                    .WithDescription("Removes an npm package from the env.");
                c.AddCommand<ToolCommandList>("list")
                    .WithDescription("Lists the tools of the env.");
            });
        });

        return app.Run(args);
    }
}
