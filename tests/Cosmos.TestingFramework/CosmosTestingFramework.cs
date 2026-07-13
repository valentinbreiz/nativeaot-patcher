using System.Globalization;
using System.Reflection;
using Cosmos.TestingFramework.Capabilities;
using Cosmos.TestingFramework.Engine;
using Cosmos.TestingFramework.Extensions;
using Cosmos.Tools.Platform;
using Microsoft.Testing.Platform.Capabilities.TestFramework;
using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Configurations;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.OutputDevice;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Logging;
using Microsoft.Testing.Platform.OutputDevice;
using Microsoft.Testing.Platform.Requests;

namespace Cosmos.TestingFramework
{
    internal partial class CosmosTestingFramework : ITestFramework, IDataProducer, IOutputDeviceDataProducer
    {
        private readonly TestingFrameworkCapabilities _capabilities;
        private readonly ICommandLineOptions _commandLineOptions;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CosmosTestingFramework> _logger;
        private readonly IOutputDevice _outputDevice;
        private readonly TestConfiguration _testingConfiguration;
        private readonly string _projectFile = string.Empty;
        private readonly Assembly[] _assemblies;

        public string Uid => nameof(CosmosTestingFramework);

        public string Version => typeof(CosmosTestingFramework).Assembly.GetName().Version?.ToString() ?? "Unknown";

        public string DisplayName => "Cosmos Test Framework";

        public string Description => "Integration of Cosmos Test Framework";

        public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage), typeof(SessionFileArtifact), typeof(FormattedTextOutputDeviceData)];

        public CosmosTestingFramework(ITestFrameworkCapabilities capabilities, ICommandLineOptions commandLineOptions, IConfiguration configuration, ILogger<CosmosTestingFramework> logger, IOutputDevice outputDevice, Assembly[] assemblies)
        {
            _capabilities = (TestingFrameworkCapabilities)capabilities;
            _commandLineOptions = commandLineOptions;
            _configuration = configuration;
            _logger = logger;
            _outputDevice = outputDevice;
            _assemblies = assemblies;
            if (_commandLineOptions.TryGetOptionArgumentList(TestingFrameworkCommandLineOptions.TestProjectFileOption, out string[]? projectFile))
            {
                _projectFile = projectFile[0];
            }

            _testingConfiguration = _configuration.GetCosmosTestingFrameworkConfiguration();

            if (_commandLineOptions.IsOptionSet(TestingFrameworkCommandLineOptions.ReportXmlOption))
            {
                string filename = "results.xml";
                if (_commandLineOptions.TryGetOptionArgumentList(TestingFrameworkCommandLineOptions.ReportXmlFilenameOption, out string[]? reportXmlFilename))
                {
                    filename = reportXmlFilename[0];
                }

                _testingConfiguration.XmlOutputPath = Path.Combine(_configuration.GetTestResultDirectory(), filename);
            }

            if (_commandLineOptions.IsOptionSet(TestingFrameworkCommandLineOptions.UartLogOption))
            {
                string filename = "uart.log";
                if (_commandLineOptions.TryGetOptionArgumentList(TestingFrameworkCommandLineOptions.UartLogFilenameOption, out string[]? uartLogFilename))
                {
                    filename = uartLogFilename[0];
                }

                _testingConfiguration.UartLogPath = Path.Combine(_configuration.GetTestResultDirectory(), filename);
            }

            if (_commandLineOptions.TryGetOptionArgumentList(TestingFrameworkCommandLineOptions.KernelArchitectureOption, out string[]? kernelArch))
            {
                _testingConfiguration.Architecture = kernelArch[0];
            }

            if (_commandLineOptions.TryGetOptionArgumentList(TestingFrameworkCommandLineOptions.KernelTimeoutOption, out string[]? kernelTimeout)
                && int.TryParse(kernelTimeout[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kernelTimeoutSeconds))
            {
                _testingConfiguration.TimeoutSeconds = kernelTimeoutSeconds;
            }

            if (_commandLineOptions.IsOptionSet(TestingFrameworkCommandLineOptions.KeepOutputOption))
            {
                _testingConfiguration.KeepBuildArtifacts = true;
            }
        }

        public Task<bool> IsEnabledAsync() => Task.FromResult(true);

        public Task<CloseTestSessionResult> CloseTestSessionAsync(CloseTestSessionContext context) => Task.FromResult(new CloseTestSessionResult() { IsSuccess = true });

        public async Task<CreateTestSessionResult> CreateTestSessionAsync(CreateTestSessionContext context)
        {
            List<ToolStatus> tools = await ToolChecker.CheckAllToolsAsync();
            CommandToolDefinition vmToolDef = _testingConfiguration.Architecture switch
            {
                "x64" => ToolDefinitions.QemuX64,
                "arm64" => ToolDefinitions.QemuArm64,
                _ => throw new NotSupportedException($"Architecture {_testingConfiguration.Architecture} not supported")
            };

            var missingTools = tools
                .Where(t => !t.Found && (t.Tool.Required || t.Tool == vmToolDef))
                .ToList();

            if (missingTools.Count > 0)
            {
                _logger.LogError("One or more required tools not found. Cannot create test session.");

                foreach (var tool in missingTools)
                {
                    await _outputDevice.DisplayAsync(this,
                        new FormattedTextOutputDeviceData($"Tool '{tool.Tool.Name}' not found") { ForegroundColor = new SystemConsoleColor() { ConsoleColor = ConsoleColor.Red } },
                        context.CancellationToken
                    );
                }

                return new CreateTestSessionResult
                {
                    IsSuccess = false,
                    ErrorMessage = "One or more required tools are missing. See logs for details."
                };
            }

            return new CreateTestSessionResult() { IsSuccess = true };
        }

        public async Task ExecuteRequestAsync(ExecuteRequestContext context)
        {
            switch (context.Request)
            {
                case DiscoverTestExecutionRequest discoverTestExecutionRequest:
                {
                    await DiscoverTestExecution(context, discoverTestExecutionRequest);
                    break;
                }
                case RunTestExecutionRequest runTestExecutionRequest:
                {
                    await RunTestExecution(context, runTestExecutionRequest);
                    break;
                }
                default:
                    throw new NotSupportedException($"Request {context.GetType()} not supported");
            }
        }
    }
}
