using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Cosmos.TestingFramework.Attributes;
using Cosmos.TestingFramework.Capabilities;
using Cosmos.TestingFramework.Extensions;
using Microsoft.Testing.Extensions.TrxReport.Abstractions;
using Microsoft.Testing.Platform.Capabilities;
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
        private readonly string _projectFile = string.Empty;
        private readonly Assembly[] _assemblies;

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
        }

        public string Uid => nameof(CosmosTestingFramework);

        public string Version => "1.0.0";

        public string DisplayName => "Cosmos Test Framework";

        public string Description => "Integration of Cosmos Test Framework";

        public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage), typeof(SessionFileArtifact)];
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);

        public Task<CloseTestSessionResult> CloseTestSessionAsync(CloseTestSessionContext context) => Task.FromResult(new CloseTestSessionResult() { IsSuccess = true });

        public async Task<CreateTestSessionResult> CreateTestSessionAsync(CreateTestSessionContext context) => await Task.FromResult(new CreateTestSessionResult() { IsSuccess = true });

        public async Task ExecuteRequestAsync(ExecuteRequestContext context)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                await _logger.LogDebugAsync($"Executing request of type '{context.Request}'");
            }
            
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
