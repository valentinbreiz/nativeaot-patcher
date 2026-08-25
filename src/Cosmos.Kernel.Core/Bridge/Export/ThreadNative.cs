using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Bridge;

/// <summary>
/// Bridge functions for and Helpers for Threading.
/// </summary>
public static class ThreadNative
{
    /// <summary>
    /// Stable native entry point used as the initial RIP/PC for freshly scheduled
    /// threads. The architecture-specific context-switch assembly returns (via iretq
    /// on x64 / eret on ARM64) into <see cref="EntryPointStub"/>, which then calls
    /// the scheduler's managed entry implementation. Thread-start registration lives
    /// in <see cref="SchedulerManager.RegisterThreadStart"/>.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void EntryPointStub(IntPtr parameter)
    {
        SchedulerManager.InvokeCurrentThreadStart(parameter);
    }

    private static ulong s_threadExInfo;

    [UnmanagedCallersOnly(EntryPoint = "__Cosmos_GetThreadExInfo")]
    internal unsafe static IntPtr GetThreadExInfo()
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            Scheduler.Thread? current = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId())?.CurrentThread;
            if (current != null)
            {
                return (nint)Unsafe.AsPointer(ref current.GetExtInfo());
            }
            else
            {
                return IntPtr.Zero;
            }
        }
        else
        {
            return (nint)Unsafe.AsPointer(ref s_threadExInfo);
        }
    }
}
