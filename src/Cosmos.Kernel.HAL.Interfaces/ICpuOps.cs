namespace Cosmos.Kernel.HAL.Interfaces;

/// <summary>
/// Essential CPU operations for multi-architecture support
/// </summary>
public interface ICpuOps
{
    /// <summary>
    /// Halt CPU
    /// </summary>
    void Halt();

    /// <summary>
    /// Disable interrupts (x64: CLI, ARM64: MSR DAIF)
    /// </summary>
    void DisableInterrupts();

    /// <summary>
    /// Enable interrupts (x64: STI, ARM64: MSR DAIF)
    /// </summary>
    void EnableInterrupts();
}
