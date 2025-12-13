namespace Cosmos.Kernel.HAL.Interfaces;

/// <summary>
/// Essential CPU operations for multi-architecture support
/// </summary>
public interface ICpuOps
{
    void Halt();
    void Nop();
    void MemoryBarrier();

    /// <summary>
    /// Disable interrupts (x64: CLI, ARM64: MSR DAIF)
    /// </summary>
    void DisableInterrupts();

    /// <summary>
    /// Enable interrupts (x64: STI, ARM64: MSR DAIF)
    /// </summary>
    void EnableInterrupts();
}
