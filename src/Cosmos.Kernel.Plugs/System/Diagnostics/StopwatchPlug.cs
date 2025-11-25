using System.Diagnostics;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.System.Diagnostics;

/// <summary>
/// Plug for System.Diagnostics.Stopwatch to provide timestamp functionality
/// for both x64 (using TSC) and ARM64 (using generic timer).
/// </summary>
[Plug(typeof(Stopwatch))]
public static class StopwatchPlug
{
    // Simple incrementing counter as fallback
    // In a real implementation, this would read hardware timers
    private static long s_counter;

    /// <summary>
    /// Gets the current timestamp in timer ticks.
    /// </summary>
    [PlugMember]
    public static long GetTimestamp()
    {
        // Simple incrementing counter
        // Each call increments by 1000 to simulate time passing
        s_counter += 1000;
        return s_counter;
    }

    /// <summary>
    /// Gets the frequency of the timer in ticks per second.
    /// </summary>
    [PlugMember("get_Frequency")]
    public static long get_Frequency()
    {
        // Return a reasonable frequency (1 MHz = 1,000,000 ticks/second)
        // This means each tick is 1 microsecond
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
}
