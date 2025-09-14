using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL;

public partial class ARM64CpuOps : ICpuOps
{
    [LibraryImport("*", EntryPoint = "_native_cpu_halt")]
    private static partial void NativeHalt();

    [LibraryImport("*", EntryPoint = "_native_cpu_memory_barrier")]
    private static partial void NativeMemoryBarrier();

    public void Halt() => NativeHalt();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Nop()
    {
        // NOP instruction
    }

    public void MemoryBarrier() => NativeMemoryBarrier();
}
