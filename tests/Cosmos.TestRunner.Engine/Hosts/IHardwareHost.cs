using System;
using System.Threading.Tasks;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// Interface for real hardware hosts that can run test kernels.
/// Unlike IQemuHost, these communicate with physical test boards via network/serial.
/// </summary>
public interface IHardwareHost
{
    /// <summary>
    /// Architecture this host targets (e.g., "arm64" for Raspberry Pi 4B)
    /// </summary>
    string Architecture { get; }

    /// <summary>
    /// Friendly name of the hardware (e.g., "Raspberry Pi 4B")
    /// </summary>
    string HardwareName { get; }

    /// <summary>
    /// Check if the hardware test board is connected and ready
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Send a kernel ISO to the test board and run tests
    /// </summary>
    /// <param name="isoPath">Path to the bootable ISO</param>
    /// <param name="uartLogPath">Path to write UART log output</param>
    /// <param name="timeoutSeconds">Maximum time to run (hardware typically needs longer)</param>
    /// <returns>Result containing UART log and status</returns>
    Task<HardwareRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 120);
}

/// <summary>
/// Result of running a kernel on real hardware
/// </summary>
public record HardwareRunResult
{
    /// <summary>
    /// Whether the test completed successfully (board responded, tests ran)
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// UART log content collected from the kernel
    /// </summary>
    public string UartLog { get; init; } = string.Empty;

    /// <summary>
    /// Whether the test timed out
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// Error message if something went wrong
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Time taken to flash and boot the kernel
    /// </summary>
    public TimeSpan BootTime { get; init; }

    /// <summary>
    /// Time taken to run all tests
    /// </summary>
    public TimeSpan TestTime { get; init; }
}
