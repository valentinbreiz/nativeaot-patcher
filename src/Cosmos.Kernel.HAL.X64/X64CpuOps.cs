using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.X64;

[PlatformSpecific(PlatformArchitecture.X64)]
public class X64CpuOps : ICpuOps
{
    [DllImport("*", EntryPoint = "_native_cpu_halt")]
    private static extern void NativeHalt();

    public void Halt() => NativeHalt();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Nop()
    {
        // NOP instruction will be inlined by compiler
    }

    public void MemoryBarrier()
    {
        // Simple memory barrier - compiler won't reorder across this
    }
}
