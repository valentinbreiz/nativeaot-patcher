using System.Diagnostics;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64;
#endif

namespace Cosmos.Kernel.Plugs.System.Diagnostics;

/// <summary>
/// Plug for System.Diagnostics.Stopwatch to provide timestamp functionality.
/// Uses TSC (Time Stamp Counter) on x64 for high-resolution timing.
/// </summary>
[Plug(typeof(Stopwatch))]
public static partial class StopwatchPlug
{
#if ARCH_X64
    [LibraryImport("*", EntryPoint = "_native_cpu_rdtsc")]
    [SuppressGCTransition]
    private static partial ulong NativeReadTSC();

    /// <summary>
    /// Gets the current timestamp using TSC.
    /// </summary>
    [PlugMember]
    public static long GetTimestamp()
    {
        return (long)NativeReadTSC();
    }

    /// <summary>
    /// Gets the TSC frequency in ticks per second.
    /// Called during Stopwatch class static initialization.
    /// </summary>
    [PlugMember]
    public static long GetFrequency()
    {
        return X64CpuOps.TscFrequency;
    }

    /// <summary>
    /// Gets the TSC frequency in ticks per second (field access plug).
    /// </summary>
    [PlugMember("get_Frequency")]
    public static long get_Frequency()
    {
        return X64CpuOps.TscFrequency;
    }

    /// <summary>
    /// Gets whether the timer is high resolution (TSC is high resolution).
    /// </summary>
    [PlugMember("get_IsHighResolution")]
    public static bool get_IsHighResolution()
    {
        return true;
    }

#else
    [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_counter")]
    [SuppressGCTransition]
    private static partial ulong NativeGetCounter();

    [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_frequency")]
    [SuppressGCTransition]
    private static partial ulong NativeGetFrequency();

    /// <summary>
    /// Gets the current timestamp using the ARM64 generic timer counter (cntpct_el0).
    /// </summary>
    [PlugMember]
    public static long GetTimestamp()
    {
        return (long)NativeGetCounter();
    }

    /// <summary>
    /// Gets the ARM64 generic timer frequency in ticks per second (cntfrq_el0).
    /// </summary>
    [PlugMember]
    public static long GetFrequency()
    {
        return (long)NativeGetFrequency();
    }

    /// <summary>
    /// Gets the ARM64 generic timer frequency in ticks per second (cntfrq_el0).
    /// </summary>
    [PlugMember("get_Frequency")]
    public static long get_Frequency()
    {
        return (long)NativeGetFrequency();
    }

    /// <summary>
    /// Gets whether the timer is high resolution.
    /// </summary>
    [PlugMember("get_IsHighResolution")]
    public static bool get_IsHighResolution()
    {
        return true;
    }
#endif
}
