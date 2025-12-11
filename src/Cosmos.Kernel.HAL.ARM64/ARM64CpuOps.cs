using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.ARM64;

public partial class ARM64CpuOps : ICpuOps
{
    [LibraryImport("*", EntryPoint = "_native_cpu_halt")]
    [SuppressGCTransition]
    private static partial void NativeHalt();

    [LibraryImport("*", EntryPoint = "_native_cpu_memory_barrier")]
    [SuppressGCTransition]
    private static partial void NativeMemoryBarrier();

    public void Halt() => NativeHalt();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Nop()
    {
        // NOP instruction
    }

    public void MemoryBarrier() => NativeMemoryBarrier();
}
