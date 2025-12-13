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
    // ARM64 fallback - simple incrementing counter
    private static long s_counter;

    /// <summary>
    /// Gets the current timestamp (fallback counter for ARM64).
    /// </summary>
    [PlugMember]
    public static long GetTimestamp()
    {
        // Simple incrementing counter until ARM64 timer is implemented
        s_counter += 1000;
        return s_counter;
    }

    /// <summary>
    /// Gets the frequency of the timer in ticks per second.
    /// </summary>
    [PlugMember("get_Frequency")]
    public static long get_Frequency()
    {
        return 1_000_000;
    }

    /// <summary>
    /// Gets whether the timer is high resolution.
    /// </summary>
    [PlugMember("get_IsHighResolution")]
    public static bool get_IsHighResolution()
    {
        return false;
    }
#endif
}
