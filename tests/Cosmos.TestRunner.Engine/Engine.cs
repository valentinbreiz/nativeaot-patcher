using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cosmos.TestRunner.Engine.Hosts;
using Cosmos.TestRunner.Engine.OutputHandlers;
using Cosmos.TestRunner.Engine.Protocol;

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

        // Get suite name from project path (use GetFileName, not GetFileNameWithoutExtension,
        // because the path is a directory like "Cosmos.Kernel.Tests.HelloWorld" and
        // GetFileNameWithoutExtension would strip ".HelloWorld" as an extension)
        string suiteName = Path.GetFileName(_config.KernelProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

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

    // Cap on the number of boots a single suite can ask for. Any test that
    // triggers a guest reboot/shutdown (Power.Reboot, Power.Shutdown) ends
    // its boot without emitting the suite-end marker; the engine then
    // re-launches the kernel, advancing `skip=N` on the Limine cmdline so
    // the kernel knows which destructive test already fired.
    private const int MaxBoots = 4;

    private async Task<QemuRunResult> LaunchAndMonitorAsync(string isoPath)
    {
        // Setup UART log path
        string baseUartLogPath = _config.UartLogPath;
        if (string.IsNullOrEmpty(baseUartLogPath))
        {
            baseUartLogPath = Path.Combine(
                Path.GetDirectoryName(isoPath) ?? ".",
                "uart.log"
            );
        }

        // Detect if this is a network test kernel
        bool enableNetworkTesting = _config.KernelProjectPath.Contains("Network", StringComparison.OrdinalIgnoreCase);

        var combinedLog = new StringBuilder();
        QemuRunResult? lastResult = null;

        for (int boot = 0; boot < MaxBoots; boot++)
        {
            string bootIsoPath = await PrepareBootIsoAsync(isoPath, boot);
            string bootLogPath = boot == 0 ? baseUartLogPath : $"{baseUartLogPath}.boot{boot}";

            if (boot > 0)
            {
                Console.WriteLine($"[Engine] Re-launching kernel for boot #{boot} (skip={boot})");
            }

            QemuRunResult result = await _qemuHost.RunKernelAsync(
                bootIsoPath, bootLogPath, _config.TimeoutSeconds, _config.ShouldShowDisplay, enableNetworkTesting);

            combinedLog.Append(result.UartLog);
            lastResult = result;

            // Suite finished cleanly (kernel emitted the end marker).
            if (result.SuiteMarkerSeen)
            {
                break;
            }

            // No suite-end marker: either the boot reached a destructive test
            // (RunDestructive — Power.Reboot/Shutdown) and the guest exited /
            // hung on purpose, or the kernel crashed mid-suite. The two are
            // distinguished by the TestDestructiveReached sentinel emitted by
            // RunDestructive immediately before invoking the destructive
            // action. Without that marker, treat this boot as a real failure
            // and let the suite fail — re-launching would just mask the bug.
            if (!UartLogShowsDestructiveProgress(result.UartLog))
            {
                break;
            }

            string exitReason = result.TimedOut ? "timed out" : "guest exited";
            Console.WriteLine($"[Engine] Boot #{boot} {exitReason} after a destructive test was reached — re-launching.");
        }

        return new QemuRunResult
        {
            ExitCode = lastResult?.ExitCode ?? -1,
            UartLog = combinedLog.ToString(),
            TimedOut = lastResult?.TimedOut ?? false,
            ErrorMessage = lastResult?.ErrorMessage ?? string.Empty,
            SuiteMarkerSeen = lastResult?.SuiteMarkerSeen ?? false
        };
    }

    /// <summary>
    /// Returns true if the per-boot UART log contains at least one
    /// TestDestructiveReached frame from the binary protocol (magic 0x19740807
    /// + command 108). Used to distinguish "destructive test was reached, then
    /// the kernel exited/hung as expected" (continue to next boot, advancing
    /// skip=N) from "the kernel crashed or hung in a non-destructive test"
    /// (real failure — bail out and let the suite fail).
    /// </summary>
    private static bool UartLogShowsDestructiveProgress(string uartLog)
    {
        if (string.IsNullOrEmpty(uartLog))
        {
            return false;
        }
        // Magic 0x19740807 little-endian = bytes 07 08 74 19, then command byte.
        // Command 108 = TestDestructiveReached (emitted only by RunDestructive).
        byte[] needle = { 0x07, 0x08, 0x74, 0x19, 108 };
        byte[] haystack = System.Text.Encoding.Latin1.GetBytes(uartLog);
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the ISO path to use for boot <paramref name="bootIndex"/>. Boot 0
    /// uses the as-built ISO unchanged (its limine.conf template already has
    /// <c>skip=0</c> or no skip token). Subsequent boots clone the ISO and
    /// rewrite /boot/limine/limine.conf so <c>cmdline: skip=N</c> matches the boot
    /// index.
    /// </summary>
    private async Task<string> PrepareBootIsoAsync(string baseIsoPath, int bootIndex)
    {
        if (bootIndex == 0)
        {
            return baseIsoPath;
        }

        string bootIsoPath = $"{baseIsoPath}.boot{bootIndex}.iso";
        File.Copy(baseIsoPath, bootIsoPath, overwrite: true);

        // Read the original limine.conf from the kernel project's Bootloader
        // directory and rewrite the skip= value. We avoid extracting from the
        // ISO (no need to run xorriso twice) since the source is right there.
        string sourceLimineConf = Path.Combine(_config.KernelProjectPath, "Bootloader", "limine.conf");
        string template = await File.ReadAllTextAsync(sourceLimineConf);
        string patched = Regex.IsMatch(template, @"skip=\d+")
            ? Regex.Replace(template, @"skip=\d+", $"skip={bootIndex}")
            : template + $"\n    cmdline: skip={bootIndex}\n";

        string patchedConfPath = Path.Combine(
            Path.GetDirectoryName(bootIsoPath) ?? ".",
            $"limine.boot{bootIndex}.conf");
        await File.WriteAllTextAsync(patchedConfPath, patched);

        // Splice the patched limine.conf into the cloned ISO. `-boot_image any
        // keep` preserves the existing Limine boot record so the ISO stays
        // bootable; `-map` replaces /boot/limine/limine.conf in place.
        var psi = new ProcessStartInfo
        {
            FileName = "xorriso",
            ArgumentList =
            {
                "-boot_image", "any", "keep",
                "-dev", bootIsoPath,
                "-map", patchedConfPath, "/boot/limine/limine.conf",
                "-commit_eject", "all"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start xorriso");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            string stderr = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"xorriso failed (exit {proc.ExitCode}): {stderr}");
        }

        return bootIsoPath;
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
        string? mapPath = FindCoverageMap();

        if (mapPath == null)
        {
            Console.WriteLine($"[Coverage] Coverage map not found");
            Console.WriteLine($"[Coverage] {results.CoverageHitMethodIds.Count} methods hit (no method names available)");
            return;
        }

        Console.WriteLine($"[Coverage] Using coverage map: {mapPath}");

        // Parse coverage map — one ID may map to multiple methods (plug aliases share target ID)
        var allMethods = new List<(int Id, string Assembly, string Type, string Method)>();
        foreach (var line in File.ReadAllLines(mapPath))
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length >= 4 && int.TryParse(parts[0], out int id))
            {
                allMethods.Add((id, parts[1], parts[2], parts[3]));
            }
        }

        int totalMethods = allMethods.Count;
        var hitSet = new HashSet<int>(results.CoverageHitMethodIds.Select(id => (int)id));
        int hitMethods = allMethods.Count(m => hitSet.Contains(m.Id));
        double percentage = totalMethods > 0 ? (double)hitMethods / totalMethods * 100 : 0;

        Console.WriteLine($"[Coverage] {hitMethods}/{totalMethods} methods covered ({percentage:F1}%)");

        // Per-assembly breakdown
        var assemblyStats = allMethods
            .GroupBy(m => m.Assembly)
            .Select(g => new
            {
                Assembly = g.Key,
                Total = g.Count(),
                Hit = g.Count(m => hitSet.Contains(m.Id))
            })
            .OrderByDescending(a => a.Total);

        foreach (var asm in assemblyStats)
        {
            double asmPct = asm.Total > 0 ? (double)asm.Hit / asm.Total * 100 : 0;
            Console.WriteLine($"[Coverage]   {asm.Assembly}: {asm.Hit}/{asm.Total} ({asmPct:F1}%)");
        }

        // Write coverage results JSON for CI (includes per-method hit data for cross-suite aggregation)
        string coverageOutputPath = Path.Combine(
            Path.GetDirectoryName(_config.XmlOutputPath ?? _config.KernelProjectPath) ?? ".",
            $"coverage-{_config.Architecture}.json");

        try
        {
            // Build per-assembly method lists (all methods + which were hit)
            var assemblyMethods = allMethods
                .GroupBy(m => m.Assembly)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(m => new
                    {
                        Key = $"{m.Type}::{m.Method}",
                        Hit = hitSet.Contains(m.Id)
                    }).ToList()
                );

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

                // Collect hit method signatures for this assembly
                var methods = assemblyMethods.GetValueOrDefault(asm.Assembly);
                var hitMethodNames = methods?
                    .Where(m => m.Hit)
                    .Select(m => m.Key)
                    .ToList() ?? [];
                var allMethodNames = methods?
                    .Select(m => m.Key)
                    .ToList() ?? [];

                writer.WriteLine($"    {{");
                writer.WriteLine($"      \"name\": \"{EscapeJson(asm.Assembly)}\",");
                writer.WriteLine($"      \"total\": {asm.Total},");
                writer.WriteLine($"      \"hit\": {asm.Hit},");
                writer.WriteLine($"      \"percentage\": {asmPct:F1},");
                writer.WriteLine($"      \"methods\": [");
                for (int j = 0; j < allMethodNames.Count; j++)
                {
                    string mComma = j < allMethodNames.Count - 1 ? "," : "";
                    bool mHit = hitMethodNames.Contains(allMethodNames[j]);
                    writer.WriteLine($"        {{ \"name\": \"{EscapeJson(allMethodNames[j])}\", \"hit\": {(mHit ? "true" : "false")} }}{mComma}");
                }
                writer.WriteLine($"      ]");
                writer.WriteLine($"    }}{comma}");
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

    /// <summary>
    /// Search for coverage-map.txt in standard and artifacts output layouts.
    /// </summary>
    private string? FindCoverageMap()
    {
        string projectDir = _config.KernelProjectPath;
        string projectName = Path.GetFileName(projectDir);
        string rid = _config.Architecture == "arm64" ? "linux-arm64" : "linux-x64";

        // Candidate paths: standard obj layout and UseArtifactsOutput layout
        string[] candidates =
        [
            Path.Combine(projectDir, "obj", "Debug", "net10.0", rid, "cosmos", "coverage-map.txt"),
            Path.Combine(projectDir, "..", "..", "artifacts", "obj", projectName, $"debug_{rid}", "cosmos", "coverage-map.txt"),
        ];

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Fallback: recursive search from project directory upward
        var dir = new DirectoryInfo(projectDir);
        while (dir != null)
        {
            var artifactsDir = Path.Combine(dir.FullName, "artifacts", "obj");
            if (Directory.Exists(artifactsDir))
            {
                var found = Directory.GetFiles(artifactsDir, "coverage-map.txt", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    return found[0];
                }
            }
            dir = dir.Parent;
        }

        return null;
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

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
