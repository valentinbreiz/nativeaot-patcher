using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// QEMU host for x86-64 architecture
/// </summary>
public class QemuX64Host : IQemuHost
{
    public string Architecture => "x64";

    private readonly string _qemuBinary;
    private readonly int _memoryMb;

    public QemuX64Host(string qemuBinary = "qemu-system-x86_64", int memoryMb = 512)
    {
        _qemuBinary = qemuBinary;
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

        // Build QEMU arguments
        // Note: Always write UART to file for parsing, display mode only affects GUI
        // Use -display none instead of -nographic to avoid conflicts with -serial file:
        string displayArgs = showDisplay
            ? $"-display gtk -vga std -serial file:\"{uartLogPath}\""
            : $"-display none -serial file:\"{uartLogPath}\"";

        // Network configuration: E1000E device with user-mode networking
        // Guest IP: 10.0.2.15, Gateway: 10.0.2.2
        // UDP Port 5555: UdpTestServer binds to receive kernel's outgoing packets (no hostfwd needed)
        // UDP Port 5556: hostfwd forwards test runner packets to kernel
        // TCP Port 5557: kernel connects to host (no hostfwd needed, outgoing from guest)
        // TCP Port 5558: hostfwd forwards test runner packets to kernel's listening socket
        string networkArgs = "-netdev user,id=net0,hostfwd=udp::5556-:5556,hostfwd=tcp::5558-:5558 -device e1000e,netdev=net0";

        var startInfo = new ProcessStartInfo
        {
            FileName = _qemuBinary,
            Arguments = $"-cdrom \"{isoPath}\" -m {_memoryMb}M -boot d -no-reboot {displayArgs} {networkArgs}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

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

            // Monitor UART log for TestSuiteEnd while waiting for process
            var monitorTask = MonitorUartLogForTestEndAsync(uartLogPath, cts.Token);
            var processTask = process.WaitForExitAsync(cts.Token);

            // Wait for either test completion or process exit
            var completedTask = await Task.WhenAny(monitorTask, processTask);

            if (completedTask == monitorTask && await monitorTask)
            {
                // Test suite completed - kill QEMU
                testSuiteCompleted = true;
                if (!process.HasExited)
                {
                    // Give a brief moment for final UART flush
                    await Task.Delay(200);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            else if (!process.HasExited)
            {
                // Process task completed (process exited on its own)
                await processTask;
            }

            // Give UART log a moment to flush
            await Task.Delay(100);

            // Stop test servers if running
            if (udpServer != null)
                await udpServer.StopAsync();
            if (tcpServer != null)
                await tcpServer.StopAsync();

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
                TimedOut = false
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
                await udpServer.StopAsync();
            if (tcpServer != null)
                await tcpServer.StopAsync();

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
                await udpServer.StopAsync();
            if (tcpServer != null)
                await tcpServer.StopAsync();

            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"Failed to run QEMU: {ex.Message}"
            };
        }
    }

    // End marker: 0xDE 0xAD 0xBE 0xEF 0xCA 0xFE 0xBA 0xBE
    private static readonly byte[] TestEndMarker = { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

    /// <summary>
    /// Monitor UART log file for test suite end marker
    /// </summary>
    private static async Task<bool> MonitorUartLogForTestEndAsync(string uartLogPath, CancellationToken cancellationToken)
    {
        long lastPosition = 0;
        int markerIndex = 0;

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

                        // Look for end marker sequence
                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (buffer[i] == TestEndMarker[markerIndex])
                            {
                                markerIndex++;
                                if (markerIndex == TestEndMarker.Length)
                                {
                                    return true;
                                }
                            }
                            else
                            {
                                markerIndex = 0;
                                // Check if current byte starts the marker
                                if (buffer[i] == TestEndMarker[0])
                                {
                                    markerIndex = 1;
                                }
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
                // File might be locked, try again
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }
}
