using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// QEMU host for ARM64/AArch64 architecture
/// </summary>
public class QemuARM64Host : IQemuHost
{
    public string Architecture => "arm64";

    private readonly string _qemuBinary;
    private readonly string _uefiFirmwarePath;
    private readonly int _memoryMb;

    public QemuARM64Host(
        string qemuBinary = "qemu-system-aarch64",
        string uefiFirmwarePath = "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd",
        int memoryMb = 512)
    {
        _qemuBinary = qemuBinary;
        _uefiFirmwarePath = uefiFirmwarePath;
        _memoryMb = memoryMb;
    }

    public async Task<QemuRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 30, bool showDisplay = false)
    {
        if (!File.Exists(isoPath))
        {
            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"ISO file not found: {isoPath}"
            };
        }

        if (!File.Exists(_uefiFirmwarePath))
        {
            return new QemuRunResult
            {
                ExitCode = -1,
                ErrorMessage = $"UEFI firmware not found: {_uefiFirmwarePath}. Install qemu-efi-aarch64 package."
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
        // ARM64 virt machine doesn't support -vga std, use ramfb device instead
        string displayArgs = showDisplay
            ? $"-device ramfb -display gtk -serial file:\"{uartLogPath}\""
            : $"-serial file:\"{uartLogPath}\" -nographic";

        var startInfo = new ProcessStartInfo
        {
            FileName = _qemuBinary,
            Arguments = $"-M virt -cpu cortex-a72 -m {_memoryMb}M " +
                       $"-bios \"{_uefiFirmwarePath}\" " +
                       $"-cdrom \"{isoPath}\" " +
                       $"-boot d -no-reboot " +
                       $"{displayArgs}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        using var process = new Process { StartInfo = startInfo };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        bool testSuiteCompleted = false;

        try
        {
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

            // Read UART log
            string uartLog = string.Empty;
            if (File.Exists(uartLogPath))
            {
                uartLog = await File.ReadAllTextAsync(uartLogPath);
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

            // Read whatever UART output we got
            string uartLog = string.Empty;
            if (File.Exists(uartLogPath))
            {
                uartLog = await File.ReadAllTextAsync(uartLogPath);
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
