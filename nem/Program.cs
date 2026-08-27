using nem.Commands;
using nem.Commands.Tool;
using Spectre.Console.Cli;
using System.Reflection;

namespace nem;

internal class Program
{
    static int Main(string[] args)
    {
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
                .WithDescription("Runs a tool from the nem env (or the system fallback). Pass tool arguments after '--'.");

            config.AddCommand<SetupCommand>("setup")
                .WithDescription("Adds the nem proxy directory to your PATH. Only needs to be run once.");

            config.AddBranch("tool", c =>
            {
                c.SetDescription("Manages the tools of the env of the current directory.");
                c.AddCommand<ToolCommandAdd>("add")
                    .WithDescription("Installs an npm package into the env, e.g. 'nem tool add ts-node@10.9.0'.");
                c.AddCommand<ToolCommandRemove>("remove")
                    .WithDescription("Removes an npm package from the env.");
                c.AddCommand<ToolCommandList>("list")
                    .WithDescription("Lists the tools of the env.");
            });
        });

        return app.Run(args);
    }
}
