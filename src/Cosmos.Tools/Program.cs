using Spectre.Console.Cli;
using Cosmos.Tools.Commands;

namespace Cosmos.Tools;

class Program
{
    static int Main(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("cosmos");

            config.AddCommand<CheckCommand>("check")
                .WithDescription("Check if required development tools are installed");

            config.AddCommand<InstallCommand>("install")
                .WithDescription("Install required development tools");

            config.AddCommand<NewCommand>("new")
                .WithDescription("Create a new Cosmos kernel project");

            config.AddCommand<BuildCommand>("build")
                .WithDescription("Build a Cosmos kernel project");

            config.AddCommand<InfoCommand>("info")
                .WithDescription("Show platform and environment information");

            config.AddCommand<UninstallCommand>("uninstall")
                .WithDescription("Uninstall Cosmos tools, templates, and VS Code extension");
        });

        return app.Run(args);
    }
}
