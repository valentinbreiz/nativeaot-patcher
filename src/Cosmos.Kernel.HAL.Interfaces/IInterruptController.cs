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
    bool HandleFatalException(ulong interrupt, ulong cpuFlags);
}
