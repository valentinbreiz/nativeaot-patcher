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
    /// <param name="enableNetworkTesting">Enable UDP test server for network tests (default false)</param>
    /// <returns>Exit code and UART log content</returns>
    Task<QemuRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 30, bool showDisplay = false, bool enableNetworkTesting = false);
}

/// <summary>
/// Outcome of the UART log monitor task.
/// </summary>
public enum UartMonitorOutcome
{
    /// <summary>Cancelled before any decision could be made.</summary>
    NotFinished,
    /// <summary>Kernel emitted the suite-end marker (0xDEADBEEFCAFEBABE).</summary>
    EndMarkerSeen,
    /// <summary>UART went quiet after a TestPass — the kernel is hung after reaching a test.</summary>
    Stalled
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

    /// <summary>
    /// True if the kernel emitted the suite-end marker (0xDEADBEEFCAFEBABE)
    /// before QEMU exited. False means QEMU exited on its own (e.g. guest
    /// rebooted or shut down) — which the multi-boot loop treats as a cue
    /// to re-launch with the next <c>skip=N</c>.
    /// </summary>
    public bool SuiteMarkerSeen { get; init; }
}
