using System.Reflection;
using Cosmos.TestingFramework.Capabilities;
using Cosmos.TestingFramework.Extensions;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Logging;
using Microsoft.Testing.Platform.Services;

namespace Cosmos.TestingFramework
{
    public static class TestingApplicationBuilderExtensions
    {
        extension(ITestApplicationBuilder builder)
        {
            public void AddCosmosTestFramework()
            {
                builder.AddCosmosTestFramework(() => [Assembly.GetEntryAssembly()!]);
            }

            public void AddCosmosTestFramework(Func<Assembly[]> assembliesFactory)
            {
                builder.CommandLine.AddProvider(static () => new TestingFrameworkCommandLineOptions());

                builder.RegisterTestFramework(_ => new TestingFrameworkCapabilities(),
                    (capabilities, serviceProvider) => new CosmosTestingFramework
                    (
                        capabilities:           capabilities,
                        commandLineOptions:     serviceProvider.GetCommandLineOptions(),
                        configuration:          serviceProvider.GetConfiguration(),
                        logger:                 serviceProvider.GetLoggerFactory().CreateLogger<CosmosTestingFramework>(),
                        outputDevice:           serviceProvider.GetOutputDevice(),
                        assemblies:             assembliesFactory()
                    )
                );
            }
        }
    }
}
