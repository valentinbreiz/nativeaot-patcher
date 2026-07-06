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
    /// Maps a physical MMIO region so the HHDM-virtual alias is accessible
    /// with Device-memory attributes. Called by HAL device drivers (AHCI,
    /// NVMe, etc.) before touching their BARs. ARM64 installs a Device
    /// mapping in TTBR1 via <c>DeviceMapper.EnsureMapped</c>; x64's existing
    /// page tables already cover MMIO so it's a no-op.
    /// </summary>
    /// <param name="physBase">Physical base address of the MMIO region.</param>
    void EnsureMmioMapped(ulong physBase);

    /// <summary>
    /// Full data-synchronization barrier ordering prior normal-memory
    /// accesses against subsequent device MMIO accesses. DMA drivers call
    /// this between filling a descriptor/queue entry in RAM and ringing
    /// the device doorbell (and after observing a device-written flag
    /// before consuming the data it guards). ARM64 issues <c>dsb sy</c>;
    /// x64's total store order already provides this, so it's a no-op.
    /// </summary>
    void DmaBarrier();

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
