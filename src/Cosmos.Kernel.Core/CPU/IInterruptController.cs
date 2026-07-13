// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.CPU;

/// <summary>
/// Platform-specific interrupt controller. Implementations live in
/// Cosmos.Kernel.Core.X64 (APIC/IDT) and Cosmos.Kernel.Core.ARM64
/// (GIC/Exception Vectors). Each owns the arch-specific dispatch path:
/// EOI timing, ack semantics, fatal-fault handling, and any extra
/// handler tables (e.g. GICv3 LPIs) all live in the implementation.
/// </summary>
public interface IInterruptController
{
    /// <summary>
    /// Initialize the interrupt system (IDT for x64, exception vectors for ARM64).
    /// </summary>
    void Initialize();

    /// <summary>
    /// Route a hardware IRQ to a specific vector.
    /// </summary>
    void RouteIrq(byte irqNo, byte vector, bool startMasked);

    /// <summary>
    /// Check if the interrupt controller is initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Dispatch an interrupt delivered by the arch's assembly stub. The
    /// implementation is responsible for acknowledging (where applicable),
    /// looking up and invoking the registered handler, signalling EOI, and
    /// handling fatal CPU exceptions.
    /// </summary>
    void Dispatch(ref IRQContext ctx);
}
