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
        string displayArgs = showDisplay
            ? $"-display gtk -vga std -serial file:\"{uartLogPath}\""
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
