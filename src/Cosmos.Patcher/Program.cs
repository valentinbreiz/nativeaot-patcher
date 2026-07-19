using Cosmos.Patcher;
using Cosmos.Patcher.Coverage;
using Spectre.Console.Cli;

CommandApp app = new();
app.Configure(config =>
{
    config.UseAssemblyInformationalVersion();
    config.AddCommand<PatchCommand>("patch");
    config.AddCommand<InstrumentCoverageCommand>("instrument-coverage");
});

app.Run(args);
