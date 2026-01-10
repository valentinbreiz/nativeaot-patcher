// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Interfaces;

/// <summary>
/// Interface for platform-specific interrupt controller.
/// Implemented by HAL.X64 (APIC/IDT) and HAL.ARM64 (GIC/Exception Vectors).
/// </summary>
public interface IInterruptController
{
    /// <summary>
    /// Initialize the interrupt system (IDT for x64, exception vectors for ARM64).
    /// </summary>
    void Initialize();

    /// <summary>
    /// Send End-Of-Interrupt signal to the controller.
    /// </summary>
    void SendEOI();

    /// <summary>
    /// Route a hardware IRQ to a specific vector.
    /// </summary>
    void RouteIrq(byte irqNo, byte vector, bool startMasked);

    /// <summary>
    /// Check if the interrupt controller is initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Handle fatal exception (arch-specific behavior).
    /// Returns true if handled (halts), false to continue.
    /// </summary>
    /// <param name="interrupt">Interrupt/exception number</param>
    /// <param name="cpuFlags">Error code (x64) or ESR (ARM64)</param>
    /// <param name="faultAddress">CR2 (x64) or FAR (ARM64) - page fault address</param>
    bool HandleFatalException(ulong interrupt, ulong cpuFlags, ulong faultAddress);

    /// <summary>
    /// Acknowledges the current interrupt and returns its ID.
    /// On ARM64 (GIC), this reads the interrupt acknowledge register and returns the actual interrupt ID.
    /// On x64, this returns uint.MaxValue (not used - x64 uses vector from IDT directly).
    /// </summary>
    /// <returns>Interrupt ID (0-1019 on ARM64 GIC), or uint.MaxValue if not applicable.</returns>
    uint AcknowledgeInterrupt();
}
