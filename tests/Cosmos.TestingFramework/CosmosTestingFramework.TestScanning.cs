
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Cosmos.TestingFramework.Attributes;
using Microsoft.Testing.Extensions.TrxReport.Abstractions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.OutputDevice;
using Microsoft.Testing.Platform.Requests;

namespace Cosmos.TestingFramework
{
    internal partial class CosmosTestingFramework
    {
        private IEnumerable<MethodInfo> GetTestsMethodFromAssemblies()
        {
            return _assemblies
                    .SelectMany(x => x.GetTypes().Where(t => t.GetCustomAttributes<TestClassAttribute>().Any()))
                    .SelectMany(x => x.GetMethods())
                    .Where(x => x.GetCustomAttributes<TestMethodAttribute>().Any());
        }

        private async Task DiscoverTestExecution(ExecuteRequestContext context, DiscoverTestExecutionRequest discoverTestExecutionRequest)
        {
            try
            {
                foreach (MethodInfo test in GetTestsMethodFromAssemblies())
                {
                    var testNode = new TestNode()
                    {
                        Uid = test.GetUid(),
                        DisplayName = test.Name,
                        Properties = new PropertyBag(DiscoveredTestNodeStateProperty.CachedInstance),
                    };

                    await _outputDevice.DisplayAsync(this, new FormattedTextOutputDeviceData($"Found Test {testNode.Uid}") { ForegroundColor = new SystemConsoleColor() { ConsoleColor = ConsoleColor.Green } }, context.CancellationToken);

                    testNode.Properties.Add(test.GetTestMethodIdentifierProperty());

                    await context.MessageBus.PublishAsync(this, new TestNodeUpdateMessage(discoverTestExecutionRequest.Session.SessionUid, testNode));
                }
            }
            finally
            {
                context.Complete();
            }
        }
    }
}
