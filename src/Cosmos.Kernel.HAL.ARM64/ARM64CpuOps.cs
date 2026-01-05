using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL.Interfaces;
using Cosmos.Kernel.Core.CPU;

namespace Cosmos.Kernel.HAL.ARM64;

public partial class ARM64CpuOps : ICpuOps
{
    public void Halt() => InternalCpu.Halt();

    public void DisableInterrupts() => InternalCpu.DisableInterrupts();

    public void EnableInterrupts() => InternalCpu.EnableInterrupts();
}
