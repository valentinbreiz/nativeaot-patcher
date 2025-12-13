using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Devices.Clock;
#endif

namespace Cosmos.Kernel.Plugs.System;

/// <summary>
/// Plug for System.DateTime to provide current time functionality.
/// Uses RTC for initial boot time and TSC for elapsed time tracking.
/// </summary>
[Plug(typeof(DateTime))]
public static partial class DateTimePlug
{
#if ARCH_X64
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    [PlugMember("get_UtcNow")]
    public static DateTime get_UtcNow()
    {
        if (RTC.Instance == null)
        {
            // RTC not initialized, return epoch
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        long ticks = RTC.Instance.GetCurrentTicks();
        if (ticks <= 0)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Gets the current local date and time.
    /// Note: Currently returns UTC time as timezone support is not implemented.
    /// </summary>
    [PlugMember("get_Now")]
    public static DateTime get_Now()
    {
        if (RTC.Instance == null)
        {
            // RTC not initialized, return epoch
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
        }
        // For now, return UTC time (no timezone support)
        long ticks = RTC.Instance.GetCurrentTicks();
        if (ticks <= 0)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
        }
        return new DateTime(ticks, DateTimeKind.Local);
    }

    /// <summary>
    /// Gets the current date (time component is 00:00:00).
    /// </summary>
    [PlugMember("get_Today")]
    public static DateTime get_Today()
    {
        return get_Now().Date;
    }
#else
    // ARM64 fallback - return fixed time until ARM64 timer is implemented
    private static long s_fakeTicks = 637134336000000000L; // 2020-01-01 00:00:00

    [PlugMember("get_UtcNow")]
    public static DateTime get_UtcNow()
    {
        s_fakeTicks += 10000; // Increment by 1ms each call
        return new DateTime(s_fakeTicks, DateTimeKind.Utc);
    }

    [PlugMember("get_Now")]
    public static DateTime get_Now()
    {
        return get_UtcNow();
    }

    [PlugMember("get_Today")]
    public static DateTime get_Today()
    {
        return get_Now().Date;
    }
#endif
}
