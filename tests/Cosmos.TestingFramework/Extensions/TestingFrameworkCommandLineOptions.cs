using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

namespace Cosmos.TestingFramework.Extensions;

internal sealed class TestingFrameworkCommandLineOptions : ICommandLineOptionsProvider
{
    public const string TestProjectFileOption = "projectfile";
    public const string ReportXmlOption = "report-xml";
    public const string ReportXmlFilenameOption = "report-xml-filename";
    public const string UartLogOption = "uartlog";
    public const string UartLogFilenameOption = "uartlog-filename";

    public string Uid => nameof(TestingFrameworkCommandLineOptions);

    public string Version => "1.0.0";

    public string DisplayName => nameof(TestingFrameworkCommandLineOptions);

    public string Description => "Testing framework command line options";

    public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() =>
    [
        new CommandLineOption(TestProjectFileOption, "Executing Assembly Project file Path", ArgumentArity.ExactlyOne, false),
        new CommandLineOption(ReportXmlOption, "Enable Cosmos XML Report", ArgumentArity.Zero, false),
        new CommandLineOption(ReportXmlFilenameOption, "The name of the Cosmos XML Report", ArgumentArity.ExactlyOne, false),
        new CommandLineOption(UartLogOption, "Enable UART log", ArgumentArity.Zero, false),
        new CommandLineOption(UartLogFilenameOption, "The name of the uart log file", ArgumentArity.ExactlyOne, false)
    ];

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public Task<ValidationResult> ValidateOptionArgumentsAsync(CommandLineOption commandOption, string[] arguments)
    {
        return ValidationResult.ValidTask;
    }

    public Task<ValidationResult> ValidateCommandLineOptionsAsync(ICommandLineOptions commandLineOptions)
    {
        if (commandLineOptions.TryGetOptionArgumentList(TestProjectFileOption, out string[]? projectFiles)) 
        {
            if(!File.Exists(projectFiles[0]))
            {
                return ValidationResult.InvalidTask("Project not set or not found");
            }
        }

        if (commandLineOptions.TryGetOptionArgumentList(UartLogFilenameOption, out string[]? _) 
            && !commandLineOptions.IsOptionSet(UartLogOption))
        {
            return ValidationResult.InvalidTask("--uartlog must be set to use --uartlog-filename");
        }
        
        if(commandLineOptions.TryGetOptionArgumentList(ReportXmlFilenameOption, out string[]? _) 
            && !commandLineOptions.IsOptionSet(ReportXmlOption))
        {
            return ValidationResult.InvalidTask("--report-xml must be set to use --report-xml-filename");
        }

        return ValidationResult.ValidTask;
    }
}