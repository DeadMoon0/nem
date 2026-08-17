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

            config.AddCommand<InitCommand>("init").WithDescription("Used to Init a new Env.");
            config.AddCommand<InstallCommand>("install").WithDescription("Used to Install every needed dependency to the .nem folder.");
            config.AddCommand<RunCommand>("run").WithDescription("Used to run a registered tool.");
            config.AddBranch("tool", c =>
            {
                c.SetDescription("Used to Managed needed Tools.");
                c.AddCommand<ToolCommandAdd>("add");
                c.AddCommand<ToolCommandAdd>("remove");
                c.AddCommand<ToolCommandAdd>("list");
            });
        });

        return app.Run(args);
    }
}