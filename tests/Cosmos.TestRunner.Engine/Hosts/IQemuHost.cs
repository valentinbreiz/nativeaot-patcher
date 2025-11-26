using System;
using System.Threading.Tasks;

namespace Cosmos.TestRunner.Engine;

/// <summary>
/// Interface for QEMU virtual machine hosts that can run test kernels
/// </summary>
public interface IQemuHost
{
    /// <summary>
    /// Architecture this host targets (x64, ARM64, etc.)
    /// </summary>
    string Architecture { get; }

    /// <summary>
    /// Run a kernel ISO in QEMU and capture UART output
    /// </summary>
    /// <param name="isoPath">Path to the bootable ISO</param>
    /// <param name="uartLogPath">Path to write UART log output</param>
    /// <param name="timeoutSeconds">Maximum time to run (default 30s)</param>
    /// <param name="showDisplay">Show QEMU display window (default false = headless)</param>
    /// <returns>Exit code and UART log content</returns>
    Task<QemuRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 30, bool showDisplay = false);
}

/// <summary>
/// Result of running a kernel in QEMU
/// </summary>
public record QemuRunResult
{
    public int ExitCode { get; init; }
    public string UartLog { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}
