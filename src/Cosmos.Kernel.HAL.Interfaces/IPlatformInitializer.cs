// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Power;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Interfaces;

/// <summary>
/// Interface for platform-specific HAL initialization.
/// Implemented by HAL.X64 and HAL.ARM64.
/// </summary>
public interface IPlatformInitializer
{
    string PlatformName { get; }
    PlatformArchitecture Architecture { get; }
    IPortIO CreatePortIO();
    ICpuOps CreateCpuOps();
    IPowerOps CreatePowerOps();

    /// <summary>
    /// Creates the platform-specific interrupt controller.
    /// </summary>
    IInterruptController CreateInterruptController();

    /// <summary>
    /// Maps PCI configuration space memory before device enumeration.
    /// ARM64 maps ECAM as device memory; x64 uses port I/O (no mapping needed).
    /// </summary>
    /// <param name="ecamBase">Physical ECAM base address from ACPI MCFG.</param>
    void PreparePciMapping(ulong ecamBase);

    /// <summary>
    /// Initializes platform-specific hardware (PCI, ACPI, APIC, GIC, etc.).
    /// Called after HAL and interrupt manager are initialized.
    /// </summary>
    void InitializeHardware();

    /// <summary>
    /// Creates and initializes the platform timer device.
    /// </summary>
    ITimerDevice CreateTimer();

    /// <summary>
    /// Gets keyboard devices available on this platform.
    /// </summary>
    IKeyboardDevice[] GetKeyboardDevices();

    /// <summary>
    /// Gets mouse devices available on this platform.
    /// </summary>
    IMouseDevice[] GetMouseDevices();

    /// <summary>
    /// Gets network devices available on this platform.
    /// Returns null if no network device found.
    /// </summary>
    INetworkDevice? GetNetworkDevice();

    /// <summary>
    /// Gets the number of CPUs detected on this platform.
    /// </summary>
    uint GetCpuCount();

    /// <summary>
    /// Starts the platform timer for preemptive scheduling.
    /// Called after all initialization is complete.
    /// </summary>
    /// <param name="quantumMs">Scheduler time quantum in milliseconds.</param>
    void StartSchedulerTimer(uint quantumMs);
}
