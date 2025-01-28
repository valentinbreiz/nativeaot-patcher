using Liquip.Patcher;
using Spectre.Console.Cli;

CommandApp? app = new();
app.Configure(config =>
{
    config.UseAssemblyInformationalVersion();
    config.AddCommand<PatchCommand>("patch");
});

app.Run(args);
