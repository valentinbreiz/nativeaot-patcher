using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cosmos.Tools.Launcher;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// QEMU host for x86-64 architecture. Argument construction lives in
/// <see cref="QemuLauncher"/> so this stays in sync with `cosmos run`.
/// </summary>
public class QemuX64Host : IQemuHost
{
    public string Architecture => "x64";

    private readonly string? _qemuBinaryOverride;
    private readonly int _memoryMb;

    public QemuX64Host(string? qemuBinary = null, int memoryMb = 512)
    {
        _qemuBinaryOverride = qemuBinary;
        _memoryMb = memoryMb;
    }

    public async Task<QemuRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 30, bool showDisplay = false, bool enableNetworkTesting = false)
    {
        if (!File.Exists(isoPath))
        {
            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"ISO file not found: {isoPath}"
            };
        }

        // Ensure UART log directory exists
        var logDir = Path.GetDirectoryName(uartLogPath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        // Delete existing UART log
        if (File.Exists(uartLogPath))
        {
            File.Delete(uartLogPath);
        }

        QemuLaunchPlan plan = await QemuLauncher.BuildAsync(new QemuLaunchOptions
        {
            Architecture = "x64",
            IsoPath = isoPath,
            MemoryMb = _memoryMb,
            Headless = !showDisplay,
            SerialOutputFile = uartLogPath,
            EnableNetworkTesting = enableNetworkTesting,
            AllowGuestShutdown = true
        });
        var startInfo = QemuLauncher.ToProcessStartInfo(plan);
        if (_qemuBinaryOverride is not null)
        {
            startInfo.FileName = _qemuBinaryOverride;
        }

        using var process = new Process { StartInfo = startInfo };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        // Only create test servers for network tests
        UdpTestServer? udpServer = null;
        TcpTestServer? tcpServer = null;
        if (enableNetworkTesting)
        {
            udpServer = new UdpTestServer();
            tcpServer = new TcpTestServer();
        }

        bool testSuiteCompleted = false;

        try
        {
            // Start test servers for network tests
            udpServer?.Start();
            tcpServer?.Start();

            process.Start();

            // Capture stderr asynchronously for diagnostics
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Monitor UART log for the suite-end marker or a stall after a test
            // was reached, while waiting for QEMU to exit on its own.
            var monitorTask = MonitorUartLogAsync(uartLogPath, cts.Token);
            var processTask = process.WaitForExitAsync(cts.Token);

            var completedTask = await Task.WhenAny(monitorTask, processTask);

            if (completedTask == monitorTask)
            {
                UartMonitorOutcome outcome = await monitorTask;
                testSuiteCompleted = outcome == UartMonitorOutcome.EndMarkerSeen;
                // Either EndMarkerSeen or Stalled — kill QEMU now. Stalled means
                // a destructive op (e.g. Power.Shutdown) hung after pre-emitting
                // its Pass marker; the engine will see the markers in the UART
                // log and roll on to the next boot.
                if (!process.HasExited)
                {
                    await Task.Delay(200);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            else if (!process.HasExited)
            {
                // Process task completed (process exited on its own — guest reboot/shutdown)
                await processTask;
            }

            // Give UART log a moment to flush
            await Task.Delay(100);

            // Stop test servers if running
            if (udpServer != null)
            {
                await udpServer.StopAsync();
            }

            if (tcpServer != null)
            {
                await tcpServer.StopAsync();
            }

            // Log stderr for diagnostics
            string stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine($"[QEMU stderr] {stderr.Trim()}");
            }

            // Read UART log
            string uartLog = string.Empty;
            if (File.Exists(uartLogPath))
            {
                uartLog = await File.ReadAllTextAsync(uartLogPath, Encoding.Latin1);
            }

            return new QemuRunResult
            {
                ExitCode = testSuiteCompleted ? 0 : process.ExitCode,
                UartLog = uartLog,
                TimedOut = false,
                SuiteMarkerSeen = testSuiteCompleted
            };
        }
        catch (OperationCanceledException)
        {
            // Timeout - kill QEMU
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            // Give UART log a moment to flush
            await Task.Delay(100);

            // Stop test servers if running
            if (udpServer != null)
            {
                await udpServer.StopAsync();
            }

            if (tcpServer != null)
            {
                await tcpServer.StopAsync();
            }

            // Read whatever UART output we got
            string uartLog = string.Empty;
            if (File.Exists(uartLogPath))
            {
                uartLog = await File.ReadAllTextAsync(uartLogPath, Encoding.Latin1);
            }

            return new QemuRunResult
            {
                ExitCode = -1,
                UartLog = uartLog,
                TimedOut = true,
                ErrorMessage = $"QEMU timed out after {timeoutSeconds}s"
            };
        }
        catch (Exception ex)
        {
            // Stop test servers on error if running
            if (udpServer != null)
            {
                await udpServer.StopAsync();
            }

            if (tcpServer != null)
            {
                await tcpServer.StopAsync();
            }

            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"Failed to run QEMU: {ex.Message}"
            };
        }
    }

    // End marker: 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
    private static readonly byte[] TestEndMarker = { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

    // Test runner protocol: 0x19740807 magic LE + command byte. Command 102 = TestPass.
    // Used to detect "kernel reached at least one test" so we can declare a stall
    // when UART goes silent — handles destructive ops (e.g. Power.Shutdown's LAI
    // panic) that hang instead of cleanly exiting QEMU.
    private static readonly byte[] TestPassMarker = { 0x07, 0x08, 0x74, 0x19, 102 };
    private const int StallSecondsAfterTestPass = 10;

    /// <summary>
    /// Monitor UART log file for either the suite-end marker or a stall after a
    /// test was reached. Returns <see cref="UartMonitorOutcome.EndMarkerSeen"/>
    /// when the kernel cleanly finished, or <see cref="UartMonitorOutcome.Stalled"/>
    /// when a TestPass was observed and the UART has been quiet for
    /// <see cref="StallSecondsAfterTestPass"/> seconds (treated as "destructive
    /// op fired but didn't exit QEMU"). Returns <see cref="UartMonitorOutcome.NotFinished"/>
    /// only on cancellation.
    /// </summary>
    private static async Task<UartMonitorOutcome> MonitorUartLogAsync(string uartLogPath, CancellationToken cancellationToken)
    {
        long lastPosition = 0;
        int endMarkerIndex = 0;
        int testPassMarkerIndex = 0;
        bool sawTestPass = false;
        // Track time of the last protocol-frame magic — not just any UART byte.
        // After Power.Shutdown's LAI panic the scheduler keeps writing text to
        // UART, so a "no growth" check would never fire; "no protocol magic"
        // does, since the test framework emits no more frames once hung.
        DateTime lastMagicAt = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(uartLogPath))
                {
                    using var fs = new FileStream(uartLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length > lastPosition)
                    {
                        fs.Seek(lastPosition, SeekOrigin.Begin);
                        var buffer = new byte[fs.Length - lastPosition];
                        int bytesRead = await fs.ReadAsync(buffer, cancellationToken);
                        lastPosition += bytesRead;

                        for (int i = 0; i < bytesRead; i++)
                        {
                            byte b = buffer[i];

                            if (b == TestEndMarker[endMarkerIndex])
                            {
                                endMarkerIndex++;
                                if (endMarkerIndex == TestEndMarker.Length)
                                {
                                    return UartMonitorOutcome.EndMarkerSeen;
                                }
                            }
                            else
                            {
                                endMarkerIndex = (b == TestEndMarker[0]) ? 1 : 0;
                            }

                            // TestPass marker scan also reuses the magic prefix —
                            // when its 4-byte magic+cmd are matched, both flags
                            // get bumped: lastMagicAt and (once) sawTestPass.
                            if (b == TestPassMarker[testPassMarkerIndex])
                            {
                                testPassMarkerIndex++;
                                if (testPassMarkerIndex == 4)
                                {
                                    // Full magic 0x19740807 hit — kernel emitted a frame.
                                    lastMagicAt = DateTime.UtcNow;
                                }
                                if (testPassMarkerIndex == TestPassMarker.Length)
                                {
                                    sawTestPass = true;
                                    testPassMarkerIndex = 0;
                                }
                            }
                            else
                            {
                                testPassMarkerIndex = (b == TestPassMarker[0]) ? 1 : 0;
                            }
                        }
                    }

                    if (sawTestPass && (DateTime.UtcNow - lastMagicAt).TotalSeconds >= StallSecondsAfterTestPass)
                    {
                        return UartMonitorOutcome.Stalled;
                    }
                }
            }
            catch (IOException)
            {
                // File might be locked, try again
            }

            await Task.Delay(100, cancellationToken);
        }

        return UartMonitorOutcome.NotFinished;
    }
}
