using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

namespace Cosmos.TestingFramework.Extensions;

internal sealed class TestingFrameworkCommandLineOptions : ICommandLineOptionsProvider
{
    public const string TestProjectFileOption = "projectfile";

    public string Uid => nameof(TestingFrameworkCommandLineOptions);

    public string Version => "1.0.0";

    public string DisplayName => nameof(TestingFrameworkCommandLineOptions);

    public string Description => "Testing framework command line options";

    public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() =>
    [
        new CommandLineOption(TestProjectFileOption, "Executing Assembly Project file Path", ArgumentArity.ExactlyOne, false)
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

        return ValidationResult.ValidTask;
    }
}