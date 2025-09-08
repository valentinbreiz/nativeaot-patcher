namespace Cosmos.Kernel.HAL;

/// <summary>
/// Essential CPU operations for multi-architecture support
/// </summary>
public interface ICpuOps
{
    void Halt();
    void Nop();
    void MemoryBarrier();
}
