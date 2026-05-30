using System;
using System.Globalization;
using Cosmos.TestingFramework.Engine;
using Microsoft.Testing.Platform.Configurations;

namespace Cosmos.TestingFramework.Extensions
{
    internal static class ConfigurationExtensions
    {
        private const string SectionName = "CosmosTestingFramework";

        public static TestConfiguration GetCosmosTestingFrameworkConfiguration(this IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var config = new TestConfiguration();
            config.Architecture = configuration[$"{SectionName}:Architecture"] ?? config.Architecture;
            config.BuildConfiguration = configuration[$"{SectionName}:BuildConfiguration"] ?? config.BuildConfiguration;

            if (int.TryParse(configuration[$"{SectionName}:TimeoutSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeoutSeconds))
            {
                config.TimeoutSeconds = timeoutSeconds;
            }

            // config.OutputDirectory = configuration[$"{SectionName}:OutputDirectory"] ?? config.OutputDirectory;
            config.UartLogPath = configuration[$"{SectionName}:UartLogPath"] ?? config.UartLogPath;

            if (bool.TryParse(configuration[$"{SectionName}:UartLog"], out bool uartLogEnabled) && uartLogEnabled)
            {
                config.UartLogPath = Path.Combine(configuration.GetTestResultDirectory(), "uart.log");
            }

            if (bool.TryParse(configuration[$"{SectionName}:XmlOutput"], out bool xmlOutputEnabled) && xmlOutputEnabled)
            {
                config.XmlOutputPath = Path.Combine(configuration.GetTestResultDirectory(), "result.xml");
            }

            if (bool.TryParse(configuration[$"{SectionName}:KeepBuildArtifacts"], out bool keepBuildArtifacts))
            {
                config.KeepBuildArtifacts = keepBuildArtifacts;
            }

            var modeValue = configuration[$"{SectionName}:Mode"];
            if (!string.IsNullOrWhiteSpace(modeValue) && Enum.TryParse<TestRunnerMode>(modeValue, ignoreCase: true, out var mode))
            {
                config.Mode = mode;
            }

            if (bool.TryParse(configuration[$"{SectionName}:ShowDisplay"], out bool showDisplay))
            {
                config.ShowDisplay = showDisplay;
            }

            if (bool.TryParse(configuration[$"{SectionName}:CoverageEnabled"], out bool coverageEnabled))
            {
                config.CoverageEnabled = coverageEnabled;
            }

            return config;
        }
    }
}
