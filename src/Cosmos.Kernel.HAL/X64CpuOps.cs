using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL;

public class X64CpuOps : ICpuOps
{
    [DllImport("*", EntryPoint = "_native_cpu_halt")]
    private static extern void NativeHalt();

    [DllImport("*", EntryPoint = "_native_cpu_memory_barrier")]
    private static extern void NativeMemoryBarrier();

    public void Halt() => NativeHalt();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Nop()
    {
        // NOP instruction will be inlined by compiler
    }

    public void MemoryBarrier() => NativeMemoryBarrier();
}
