// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Timer;

/// <summary>
/// Manages system timers.
/// </summary>
public static class TimerManager
{
    /// <summary>Nanoseconds in one millisecond.</summary>
    private const ulong NanosecondsPerMillisecond = 1_000_000;

    private static ITimerDevice? _timer;
    private static bool _initialized;

    /// <summary>
    /// Gets whether the timer manager is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes the timer manager.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
    }

    /// <summary>
    /// Registers a timer device with the manager.
    /// </summary>
    public static void RegisterTimer(ITimerDevice timer)
    {
        if (timer == null)
        {
            return;
        }

        _timer = timer;
    }

    /// <summary>
    /// Gets the current timer frequency in Hz.
    /// </summary>
    public static uint Frequency => _timer?.Frequency ?? 0;

    /// <summary>
    /// Sets the timer frequency in Hz.
    /// </summary>
    public static void SetFrequency(uint frequency)
    {
        _timer?.SetFrequency(frequency);
    }

    /// <summary>
    /// Blocks for the specified number of milliseconds.
    /// </summary>
    /// <param name="ms">Milliseconds to wait.</param>
    public static void Wait(uint ms)
    {
        _timer?.Wait(ms);
    }

    /// <summary>
    /// Schedules a callback to run once after the specified delay. The callback
    /// runs in interrupt context and must not block; use
    /// <see cref="Core.Scheduler.AlarmSystem"/> for callbacks that need thread context.
    /// </summary>
    /// <param name="callback">Method to invoke when the delay expires.</param>
    /// <param name="delayMs">Delay in milliseconds.</param>
    /// <returns>The scheduled timer, or null if no timer device is registered.</returns>
    public static SoftwareTimer? Schedule(Action callback, uint delayMs)
    {
        return ScheduleCore(callback, delayMs, recurring: false);
    }

    /// <summary>
    /// Schedules a callback to run repeatedly with the specified period. The
    /// callback runs in interrupt context and must not block; use
    /// <see cref="AlarmSystem"/> for callbacks that need thread context.
    /// </summary>
    /// <param name="callback">Method to invoke each period.</param>
    /// <param name="periodMs">Period in milliseconds.</param>
    /// <returns>The scheduled timer, or null if no timer device is registered.</returns>
    public static SoftwareTimer? ScheduleRecurring(Action callback, uint periodMs)
    {
        return ScheduleCore(callback, periodMs, recurring: true);
    }

    /// <summary>
    /// Cancels a timer returned by <see cref="Schedule"/> or <see cref="ScheduleRecurring"/>.
    /// </summary>
    /// <param name="timer">Timer to cancel.</param>
    public static void Cancel(SoftwareTimer? timer)
    {
        if (timer == null)
        {
            return;
        }

        _timer?.UnregisterTimer(timer);
    }

    private static SoftwareTimer? ScheduleCore(Action callback, uint ms, bool recurring)
    {
        if (_timer == null || callback == null)
        {
            return null;
        }

        SoftwareTimer timer = new(callback, ms * NanosecondsPerMillisecond, recurring);
        _timer.RegisterTimer(timer);
        return timer;
    }

    /// <summary>
    /// Gets the registered timer device.
    /// </summary>
    public static ITimerDevice? Timer => _timer;
}
