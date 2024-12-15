using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.UseAssemblyInformationalVersion();
    
});

app.Run(args);