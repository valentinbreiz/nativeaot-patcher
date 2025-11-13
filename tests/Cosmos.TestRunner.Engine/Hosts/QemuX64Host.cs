using System;
using System.Diagnostics;
using System.IO;
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
        string displayArgs = showDisplay
            ? $"-display gtk -vga std -serial file:\"{uartLogPath}\""
            : $"-serial file:\"{uartLogPath}\" -nographic";

        var startInfo = new ProcessStartInfo
        {
            FileName = _qemuBinary,
            Arguments = $"-cdrom \"{isoPath}\" -m {_memoryMb}M -boot d -no-reboot {displayArgs}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        using var process = new Process { StartInfo = startInfo };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            process.Start();

            // Wait for process to exit or timeout
            await process.WaitForExitAsync(cts.Token);

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
                ExitCode = process.ExitCode,
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
}
