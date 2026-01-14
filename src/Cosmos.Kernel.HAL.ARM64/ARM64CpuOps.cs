using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.ARM64;

public partial class ARM64CpuOps : ICpuOps
{
    public void Halt() => InternalCpu.Halt();

    public void DisableInterrupts() => InternalCpu.DisableInterrupts();

    public void EnableInterrupts() => InternalCpu.EnableInterrupts();
}
