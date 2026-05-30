using System.Reflection;
using Cosmos.TestingFramework.Engine;
using Cosmos.TestingFramework.Attributes;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.OutputDevice;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Logging;
using Microsoft.Testing.Platform.OutputDevice;
using Microsoft.Testing.Platform.Requests;
using Microsoft.Testing.Platform.Configurations;

namespace Cosmos.TestingFramework
{
    internal partial class CosmosTestingFramework : ITestFramework, IDataProducer, IOutputDeviceDataProducer
    {

        private async Task RunTestExecution(ExecuteRequestContext context, RunTestExecutionRequest runTestExecutionRequest)
        {
            try
            {
                await _outputDevice.DisplayAsync(this, new FormattedTextOutputDeviceData($"{nameof(CosmosTestingFramework)} version '{Version}' running tests, project file '{_projectFile}") { ForegroundColor = new SystemConsoleColor() { ConsoleColor = ConsoleColor.Green } }, context.CancellationToken);
                
                var results = new List<Task>();

                // Build the TestKernel List
                var testKernels = GetTestsMethodFromAssemblies()
                    .GroupBy(m => m.DeclaringType!)
                    .Select(g => new TestKernel(g.Key, g.ToList()))
                    .ToList();

                var uidToMethod = testKernels
                    .SelectMany(tk => tk.Methods.Select(m => (Uid: TestKernel.GetUid(m), Method: m)))
                    .ToDictionary(x => x.Uid, x => x.Method);

                foreach (MethodInfo test in GetTestsMethodFromAssemblies())
                {
                    SkipAttribute? skipAttribute = test.GetCustomAttribute<SkipAttribute>();
                    if (skipAttribute != null)
                    {
                        var skippedTestNode = new TestNode()
                        {
                            Uid = test.GetUid(),
                            DisplayName = test.GetUid(),
                            Properties = new PropertyBag(new SkippedTestNodeStateProperty(skipAttribute.Reason)),
                        };
                        
                        skippedTestNode.Properties.Add(test.GetTestMethodIdentifierProperty());

                        if (_capabilities.TrxCapability.IsTrxEnabled)
                        {
                            FillTrxProperties(skippedTestNode, test);
                        }

                        skippedTestNode.Properties.Add(new StandardOutputProperty(_projectFile));
                        await context.MessageBus.PublishAsync(this, new TestNodeUpdateMessage(runTestExecutionRequest.Session.SessionUid, skippedTestNode));

                        continue;
                    }
                }

                results.Add(Task.Run(async () =>
                {
                    var config = _testingConfiguration with
                    {
                        KernelProjectPath = Path.GetDirectoryName(_projectFile)!,
                    };

                    try
                    {
                        var runner = new Engine.Engine(config);
                        var results = await runner.ExecuteAsync();
                        results.Tests.ForEach(async ktest =>
                        {
                            Console.WriteLine($"Test {ktest.TestName} finished with status {ktest.Status} in {ktest.DurationMs} milliseconds");
                            var test = new TestNode()
                            {
                                Uid = ktest.TestName,
                                DisplayName = ktest.TestName
                            };

                            // If we know the MethodInfo for this test, enrich the TestNode with identifier, timing and TRX info
                            if (uidToMethod.TryGetValue(ktest.TestName, out var methodInfo))
                            {
                                test.Properties.Add(methodInfo.GetTestMethodIdentifierProperty());

                                if (_capabilities.TrxCapability.IsTrxEnabled)
                                {
                                    FillTrxProperties(test, methodInfo);
                                }
                            }

                            switch (ktest.Status)
                            {
                                case TestStatus.Passed:
                                    test.Properties.Add(PassedTestNodeStateProperty.CachedInstance);
                                    
                                    // TODO: The test duration is currently only available in milliseconds, we might want to enhance the TestRunner to also provide start and end time.
                                    DateTimeOffset endtime = DateTimeOffset.Now;
                                    var duration = TimeSpan.FromMilliseconds(ktest.DurationMs);
                                    DateTimeOffset starttime = endtime - duration;
                                    var timeinfo = new TimingInfo(starttime, endtime, duration);
                                    test.Properties.Add(new TimingProperty(timeinfo));
                                    break;
                                case TestStatus.Failed:
                                    test.Properties.Add(new FailedTestNodeStateProperty(ktest.ErrorMessage));
                                    break;
                                case TestStatus.Skipped:
                                    test.Properties.Add(new SkippedTestNodeStateProperty("Skipped by Cosmos Test Runner"));
                                    break;
                            }

                            await context.MessageBus.PublishAsync(this, new TestNodeUpdateMessage(runTestExecutionRequest.Session.SessionUid, test));
                        });

                        await _outputDevice.DisplayAsync(this, new FormattedTextOutputDeviceData(results.ErrorMessage) { ForegroundColor = new SystemConsoleColor() { ConsoleColor = ConsoleColor.Green } }, context.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex);
                    }

                    if(!string.IsNullOrEmpty(config.XmlOutputPath))
                    {
                        await context.MessageBus.PublishAsync(this, new SessionFileArtifact(runTestExecutionRequest.Session.SessionUid, new FileInfo(config.XmlOutputPath), "Testing framework results"));
                    }

                    if(!string.IsNullOrEmpty(config.UartLogPath))
                    {
                        await context.MessageBus.PublishAsync(this, new SessionFileArtifact(runTestExecutionRequest.Session.SessionUid, new FileInfo(config.UartLogPath), "UART log output"));
                    }   
                }));


                await Task.WhenAll(results);
            }
            finally
            {
                // Ensure to complete the request also in case of exception
                context.Complete();
            }
        }
    }
}
