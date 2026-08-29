// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Timer;

/// <summary>
/// Manages system timers.
/// </summary>
public static class TimerManager
{
    /// <summary>Nanoseconds in one <see cref="TimeSpan"/> tick.</summary>
    private const ulong NanosecondsPerTick = 100;

    private static ITimerDevice? s_timer;

    /// <summary>
    /// Gets whether a timer device is registered. False when the timer is
    /// compiled out with CosmosEnableTimer=false, since every member of this
    /// class answers off that device.
    /// </summary>
    public static bool IsInitialized => s_timer != null;

    /// <summary>
    /// Registers a timer device with the manager.
    /// </summary>
    internal static void RegisterTimer(ITimerDevice timer)
    {
        if (timer == null)
        {
            return;
        }

        s_timer = timer;
    }

    /// <summary>
    /// Gets the current timer frequency in Hz.
    /// </summary>
    public static uint Frequency => s_timer?.Frequency ?? 0;

    /// <summary>
    /// Sets the timer frequency in Hz.
    /// </summary>
    public static void SetFrequency(uint frequency)
    {
        s_timer?.SetFrequency(frequency);
    }

    /// <summary>
    /// Blocks for the specified number of milliseconds.
    /// </summary>
    /// <param name="ms">Milliseconds to wait.</param>
    public static void Wait(uint ms)
    {
        s_timer?.Wait(ms);
    }

    /// <summary>
    /// Schedules a callback to run once after the specified delay. The callback
    /// runs in interrupt context and must not block; use
    /// <see cref="AlarmManager.Schedule"/> for callbacks that need thread
    /// context.
    /// </summary>
    /// <param name="callback">Method to invoke when the delay expires.</param>
    /// <param name="delay">
    /// Delay before the timer fires. The timer device's tick is the resolution,
    /// so a delay shorter than one tick, and a zero or negative delay, fire on
    /// the next tick.
    /// </param>
    /// <returns>The scheduled timer, or null if no timer device is registered.</returns>
    public static SoftwareTimer? Schedule(Action callback, TimeSpan delay)
    {
        return ScheduleCore(callback, ToNanoseconds(delay), recurring: false);
    }

    /// <summary>
    /// Schedules a callback to run repeatedly with the specified period. The
    /// callback runs in interrupt context and must not block; use
    /// <see cref="AlarmManager.ScheduleRecurring"/> for callbacks that need
    /// thread context.
    /// </summary>
    /// <param name="callback">Method to invoke each period.</param>
    /// <param name="period">
    /// Period between firings; must be positive. The timer device's tick is the
    /// resolution, so a period shorter than one tick fires on every tick.
    /// </param>
    /// <returns>
    /// The scheduled timer, or null if no timer device is registered or the
    /// period is not positive.
    /// </returns>
    public static SoftwareTimer? ScheduleRecurring(Action callback, TimeSpan period)
    {
        if (period <= TimeSpan.Zero)
        {
            return null;
        }

        return ScheduleCore(callback, ToNanoseconds(period), recurring: true);
    }

    /// <summary>
    /// Cancels a timer returned by <see cref="Schedule"/> or <see cref="ScheduleRecurring"/>.
    /// </summary>
    /// <param name="timer">Timer to cancel.</param>
    /// <returns>
    /// True when the timer was pending and has been cancelled; false when it is
    /// null, had already fired, was already cancelled, or no timer device is
    /// registered.
    /// </returns>
    public static bool Cancel(SoftwareTimer? timer)
    {
        if (timer == null || s_timer == null)
        {
            return false;
        }

        return s_timer.UnregisterTimer(timer);
    }

    private static SoftwareTimer? ScheduleCore(Action callback, ulong timeoutNs, bool recurring)
    {
        if (s_timer == null || callback == null)
        {
            return null;
        }

        SoftwareTimer timer = new(callback, timeoutNs, recurring);
        s_timer.RegisterTimer(timer);
        return timer;
    }

    /// <summary>
    /// Converts a duration to the nanoseconds a <see cref="SoftwareTimer"/>
    /// counts down. A non-positive duration becomes 0, which fires on the next
    /// device tick, and a duration too large to express in nanoseconds
    /// saturates rather than wrapping.
    /// </summary>
    private static ulong ToNanoseconds(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return 0;
        }

        // ulong.MaxValue nanoseconds is roughly 584 years; past that the
        // multiply below would wrap and produce a near-immediate timer.
        const long MaxTicks = (long)(ulong.MaxValue / NanosecondsPerTick);

        if (value.Ticks >= MaxTicks)
        {
            return ulong.MaxValue;
        }

        return (ulong)value.Ticks * NanosecondsPerTick;
    }

    /// <summary>
    /// Gets the registered timer device.
    /// </summary>
    internal static ITimerDevice? Timer => s_timer;
}
