// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.System.Timer;

/// <summary>
/// Runs callbacks after a delay on a dedicated kernel thread. Alarm callbacks
/// run in thread context, so they may block, allocate and use scheduler
/// primitives, unlike the interrupt-context callbacks of
/// <see cref="TimerManager.Schedule"/>. Requires the scheduler; resolution is
/// bounded by the scheduler tick.
/// </summary>
public static class AlarmManager
{
    /// <summary>
    /// Schedules a callback to run once after the specified delay, in thread
    /// context.
    /// </summary>
    /// <param name="delay">Delay before the alarm fires.</param>
    /// <param name="callback">Method to invoke when the delay expires.</param>
    /// <returns>The alarm ID to pass to <see cref="Cancel"/>, or 0 when the alarm could not be scheduled because the scheduler is not running.</returns>
    public static ulong Schedule(TimeSpan delay, Action callback)
    {
        return AlarmSystem.Add(delay, callback);
    }

    /// <summary>
    /// Schedules a callback to run repeatedly with the specified period, in
    /// thread context. The period restarts when the callback returns, so it
    /// must be longer than the callback takes to run.
    /// </summary>
    /// <param name="period">Period between firings; at least 1 ms.</param>
    /// <param name="callback">Method to invoke each period.</param>
    /// <returns>The alarm ID to pass to <see cref="Cancel"/>, or 0 when the alarm could not be scheduled because the scheduler is not running or the period is under 1 ms.</returns>
    public static ulong ScheduleRecurring(TimeSpan period, Action callback)
    {
        return AlarmSystem.AddRecurring(period, callback);
    }

    /// <summary>
    /// Cancels a pending alarm.
    /// </summary>
    /// <param name="id">ID returned by <see cref="Schedule"/> or <see cref="ScheduleRecurring"/>.</param>
    /// <returns>True when the alarm was pending and has been cancelled; false when it had already fired or the ID is unknown.</returns>
    public static bool Cancel(ulong id)
    {
        return AlarmSystem.Remove(id);
    }
}
