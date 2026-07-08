using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cosmos.Tools.Launcher;

namespace Cosmos.TestRunner.Engine;

/// <summary>
/// Interface for QEMU virtual machine hosts that can run test kernels
/// </summary>
public interface IQemuHost
{
    /// <summary>Default maximum time in seconds to let a kernel run in QEMU before timing out.</summary>
    public const int DefaultTimeoutSeconds = 30;

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
    /// <param name="disks">Per-profile disk attachments. AHCI entries share one <c>ich9-ahci</c> controller; NVMe entries each get their own <c>nvme</c> controller. Per-disk extra device options (e.g. <c>msix=off</c>) flow through.</param>
    /// <param name="machineOptions">Extra <c>-M</c> properties (e.g. <c>{"gic-version", "2"}</c> on ARM64). Caller is responsible for passing arch-appropriate keys.</param>
    /// <returns>Exit code and UART log content</returns>
    Task<QemuRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = DefaultTimeoutSeconds, bool showDisplay = false, bool enableNetworkTesting = false, IReadOnlyList<DiskAttachment>? disks = null, IReadOnlyDictionary<string, string>? machineOptions = null);
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
