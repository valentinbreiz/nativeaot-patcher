using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Cosmos.TestRunner.Engine.Hosts;
using Cosmos.TestRunner.Engine.Protocol;
using Cosmos.TestRunner.Engine.OutputHandlers;

namespace Cosmos.TestRunner.Engine;

/// <summary>
/// Main test runner engine - orchestrates build, launch, monitor, and result collection
/// </summary>
public partial class Engine
{
    private readonly TestConfiguration _config;
    private readonly IQemuHost _qemuHost;
    private readonly OutputHandlerBase _outputHandler;

    public Engine(TestConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Select QEMU host based on architecture
        _qemuHost = _config.Architecture.ToLowerInvariant() switch
        {
            "x64" => new QemuX64Host(),
            "arm64" => new QemuARM64Host(),
            _ => throw new ArgumentException($"Unsupported architecture: {_config.Architecture}")
        };

        // Setup output handler(s)
        _outputHandler = SetupOutputHandler();
    }

    private OutputHandlerBase SetupOutputHandler()
    {
        // If user provided a custom handler, use it
        if (_config.OutputHandler != null)
        {
            return _config.OutputHandler;
        }

        // Default: console output
        var consoleHandler = new OutputHandlerConsole(useColors: true, verbose: false);

        // If XML output requested, multiplex console + XML
        if (!string.IsNullOrEmpty(_config.XmlOutputPath))
        {
            var xmlHandler = new OutputHandlerXml(_config.XmlOutputPath);
            return new MultiplexingOutputHandler(consoleHandler, xmlHandler);
        }

        return consoleHandler;
    }

    /// <summary>
    /// Main execution flow: Build → Launch → Monitor → Results
    /// </summary>
    public async Task<TestResults> ExecuteAsync()
    {
        Console.WriteLine($"[Engine] Starting test execution for {_config.KernelProjectPath}");
        Console.WriteLine($"[Engine] Architecture: {_config.Architecture}");
        Console.WriteLine($"[Engine] Timeout: {_config.TimeoutSeconds}s");

        var stopwatch = Stopwatch.StartNew();

        // Get suite name from project path
        string suiteName = Path.GetFileNameWithoutExtension(_config.KernelProjectPath);

        try
        {
            // Notify start
            _outputHandler.OnTestSuiteStart(suiteName, _config.Architecture);

            // Step 1: Build kernel to ISO
            Console.WriteLine("[Engine] Building kernel...");
            string isoPath = await BuildKernelAsync();
            Console.WriteLine($"[Engine] Build complete: {isoPath}");

            // Step 2: Launch QEMU and monitor execution
            Console.WriteLine("[Engine] Launching QEMU...");
            var qemuResult = await LaunchAndMonitorAsync(isoPath);
            Console.WriteLine($"[Engine] QEMU execution complete (Exit: {qemuResult.ExitCode}, TimedOut: {qemuResult.TimedOut})");

            // Step 3: Parse results from UART log
            Console.WriteLine("[Engine] Parsing test results...");
            var results = ParseResults(qemuResult);
            results.SuiteName = suiteName;
            results.TotalDuration = stopwatch.Elapsed;

            Console.WriteLine($"[Engine] Results: {results.PassedTests}/{results.TotalTests} passed");

            // Report coverage if data was received
            if (results.CoverageHitMethodIds.Count > 0)
            {
                ReportCoverage(results);
            }

            // Notify individual test results
            foreach (var test in results.Tests)
            {
                _outputHandler.OnTestStart(test.TestNumber, test.TestName);

                switch (test.Status)
                {
                    case TestStatus.Passed:
                        _outputHandler.OnTestPass(test.TestNumber, test.TestName, test.DurationMs);
                        break;
                    case TestStatus.Failed:
                        _outputHandler.OnTestFail(test.TestNumber, test.TestName, test.ErrorMessage, test.DurationMs);
                        break;
                    case TestStatus.Skipped:
                        _outputHandler.OnTestSkip(test.TestNumber, test.TestName, test.ErrorMessage);
                        break;
                }
            }

            // Notify end
            _outputHandler.OnTestSuiteEnd(results);
            _outputHandler.Complete();

            // Step 4: Cleanup (optional)
            if (!_config.KeepBuildArtifacts && !string.IsNullOrEmpty(isoPath))
            {
                CleanupBuildArtifacts(isoPath);
            }

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Engine] ERROR: {ex.Message}");
            _outputHandler.OnError(ex.Message);

            var results = new TestResults
            {
                SuiteName = suiteName,
                Architecture = _config.Architecture,
                ErrorMessage = ex.Message,
                TotalDuration = stopwatch.Elapsed
            };

            _outputHandler.OnTestSuiteEnd(results);
            _outputHandler.Complete();

            return results;
        }
    }

    private async Task<QemuRunResult> LaunchAndMonitorAsync(string isoPath)
    {
        // Setup UART log path
        string uartLogPath = _config.UartLogPath;
        if (string.IsNullOrEmpty(uartLogPath))
        {
            uartLogPath = Path.Combine(
                Path.GetDirectoryName(isoPath) ?? ".",
                "uart.log"
            );
        }

        // Detect if this is a network test kernel
        bool enableNetworkTesting = _config.KernelProjectPath.Contains("Network", StringComparison.OrdinalIgnoreCase);

        // Launch QEMU and capture UART
        return await _qemuHost.RunKernelAsync(isoPath, uartLogPath, _config.TimeoutSeconds, _config.ShouldShowDisplay, enableNetworkTesting);
    }

    private TestResults ParseResults(QemuRunResult qemuResult)
    {
        // Always try to parse UART log, even on timeout - kernel may have completed tests
        var results = UartMessageParser.ParseUartLog(qemuResult.UartLog ?? string.Empty, _config.Architecture);
        results.TimedOut = qemuResult.TimedOut;
        results.UartLog = qemuResult.UartLog ?? string.Empty;
        results.ErrorMessage = qemuResult.ErrorMessage ?? string.Empty;

        // If the suite completed normally (TestSuiteEnd received and validated), all tests ran —
        // no need to synthesise failures for missing tests.
        if (!results.SuiteCompleted && results.ExpectedTestCount > 0 && results.Tests.Count < results.ExpectedTestCount)
        {
            int actualCount = results.Tests.Count;

            // Sanity check: timer interrupts can corrupt the ExpectedTestCount field in the
            // TestSuiteStart message (high byte replaced by '[' = 0x5B from "[GenericTimer]" text).
            // If the expected count is implausibly large compared to what actually ran, ignore it.
            int maxPlausible = actualCount + 10000;
            if (results.ExpectedTestCount > maxPlausible)
            {
                Console.WriteLine($"[ParseResults] Warning: ExpectedTestCount={results.ExpectedTestCount} seems corrupted (actual={actualCount}), ignoring.");
            }
            else
            {
                for (int i = actualCount + 1; i <= results.ExpectedTestCount; i++)
                {
                    results.Tests.Add(new TestResult
                    {
                        TestNumber = i,
                        TestName = $"Test {i}",
                        Status = TestStatus.Failed,
                        ErrorMessage = "Test did not execute (kernel crashed or timed out)",
                        DurationMs = 0
                    });
                }
            }
        }

        return results;
    }

    private void ReportCoverage(TestResults results)
    {
        // Look for coverage-map.txt in the build output
        string projectDir = _config.KernelProjectPath;
        string rid = _config.Architecture == "arm64" ? "linux-arm64" : "linux-x64";
        string mapPath = Path.Combine(projectDir, "obj", "Debug", "net10.0", rid, "cosmos", "coverage-map.txt");

        if (!File.Exists(mapPath))
        {
            Console.WriteLine($"[Coverage] Coverage map not found at: {mapPath}");
            Console.WriteLine($"[Coverage] {results.CoverageHitMethodIds.Count} methods hit (no method names available)");
            return;
        }

        // Parse coverage map
        var methodMap = new Dictionary<int, (string Assembly, string Type, string Method)>();
        foreach (var line in File.ReadAllLines(mapPath))
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('\t');
            if (parts.Length >= 4 && int.TryParse(parts[0], out int id))
            {
                methodMap[id] = (parts[1], parts[2], parts[3]);
            }
        }

        int totalMethods = methodMap.Count;
        int hitMethods = results.CoverageHitMethodIds.Count;
        double percentage = totalMethods > 0 ? (double)hitMethods / totalMethods * 100 : 0;

        Console.WriteLine($"[Coverage] {hitMethods}/{totalMethods} methods covered ({percentage:F1}%)");

        // Per-assembly breakdown
        var hitSet = new HashSet<int>(results.CoverageHitMethodIds.Select(id => (int)id));
        var assemblyStats = methodMap
            .GroupBy(kv => kv.Value.Assembly)
            .Select(g => new
            {
                Assembly = g.Key,
                Total = g.Count(),
                Hit = g.Count(kv => hitSet.Contains(kv.Key))
            })
            .OrderByDescending(a => a.Total);

        foreach (var asm in assemblyStats)
        {
            double asmPct = asm.Total > 0 ? (double)asm.Hit / asm.Total * 100 : 0;
            Console.WriteLine($"[Coverage]   {asm.Assembly}: {asm.Hit}/{asm.Total} ({asmPct:F1}%)");
        }

        // Write coverage results JSON for CI
        string coverageOutputPath = Path.Combine(
            Path.GetDirectoryName(_config.XmlOutputPath ?? projectDir) ?? ".",
            $"coverage-{_config.Architecture}.json");

        try
        {
            using var writer = new StreamWriter(coverageOutputPath);
            writer.WriteLine("{");
            writer.WriteLine($"  \"suite\": \"{results.SuiteName}\",");
            writer.WriteLine($"  \"architecture\": \"{_config.Architecture}\",");
            writer.WriteLine($"  \"totalMethods\": {totalMethods},");
            writer.WriteLine($"  \"hitMethods\": {hitMethods},");
            writer.WriteLine($"  \"percentage\": {percentage:F1},");
            writer.WriteLine("  \"assemblies\": [");

            var asmList = assemblyStats.ToList();
            for (int i = 0; i < asmList.Count; i++)
            {
                var asm = asmList[i];
                double asmPct = asm.Total > 0 ? (double)asm.Hit / asm.Total * 100 : 0;
                string comma = i < asmList.Count - 1 ? "," : "";
                writer.WriteLine($"    {{ \"name\": \"{asm.Assembly}\", \"total\": {asm.Total}, \"hit\": {asm.Hit}, \"percentage\": {asmPct:F1} }}{comma}");
            }

            writer.WriteLine("  ]");
            writer.WriteLine("}");

            Console.WriteLine($"[Coverage] Report written to: {coverageOutputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coverage] Warning: Could not write coverage JSON: {ex.Message}");
        }
    }

    private void CleanupBuildArtifacts(string isoPath)
    {
        try
        {
            if (File.Exists(isoPath))
            {
                File.Delete(isoPath);
            }

            var outputDir = Path.GetDirectoryName(isoPath);
            if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Engine] Warning: Failed to cleanup: {ex.Message}");
        }
    }
}
