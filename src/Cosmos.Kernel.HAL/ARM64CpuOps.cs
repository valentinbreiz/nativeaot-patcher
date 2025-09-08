using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
namespace Cosmos.Kernel.HAL;

[PlatformSpecific(PlatformArchitecture.ARM64)]
public class ARM64CpuOps : ICpuOps
{
    [DllImport("*", EntryPoint = "_native_cpu_halt")]
    private static extern void NativeHalt();

    [DllImport("*", EntryPoint = "_native_cpu_memory_barrier")]
    private static extern void NativeMemoryBarrier();

    public void Halt() => NativeHalt();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Nop()
    {
        // NOP instruction
    }

    public void MemoryBarrier() => NativeMemoryBarrier();
}
