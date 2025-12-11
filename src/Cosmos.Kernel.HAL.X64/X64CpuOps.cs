using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.X64;

public partial class X64CpuOps : ICpuOps
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
        // NOP instruction will be inlined by compiler
    }

    public void MemoryBarrier() => NativeMemoryBarrier();
}
