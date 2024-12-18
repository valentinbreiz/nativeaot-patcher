using Liquip.Patcher;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.UseAssemblyInformationalVersion();
    config.AddCommand<PatchCommand>("patch");
});

app.Run(args);