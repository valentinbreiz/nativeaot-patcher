using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Linq;
using System.IO;
using Cosmos.TestingFramework.Engine;
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
using EngineClass = Cosmos.TestingFramework.Engine.Engine;

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
                            Uid = $"{test.DeclaringType!.FullName}.{test.Name}",
                            DisplayName = test.Name,
                            Properties = new PropertyBag(new SkippedTestNodeStateProperty(skipAttribute.Reason)),
                        };
                        var testMethodIdentifierProperty = new TestMethodIdentifierProperty(test.DeclaringType!.Assembly!.FullName!,
                        test.DeclaringType!.Namespace!,
                        test.DeclaringType.Name!,
                        test.Name,
                        test.GetGenericArguments().Length,
                        test.GetParameters().Select(x => x.ParameterType.FullName).ToArray()!,
                        test.ReturnType.FullName!);

                        skippedTestNode.Properties.Add(testMethodIdentifierProperty);

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
                    var config = new TestConfiguration
                    {
                        KernelProjectPath = Path.GetDirectoryName(_projectFile)!,
                        Architecture = "x64",
                        TimeoutSeconds = 120,
                        KeepBuildArtifacts = false, // Keep artifacts for debugging
                        Mode = TestRunnerMode.Dev,
                        CoverageEnabled = false
                    };
                    try
                    {
                        var runner = new EngineClass(config);
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

                                test.Properties.Add(new StandardOutputProperty(_projectFile));
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
