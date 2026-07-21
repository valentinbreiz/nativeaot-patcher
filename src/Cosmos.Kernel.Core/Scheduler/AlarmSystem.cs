// This code is licensed under MIT license (see LICENSE for details)

using System.Diagnostics;
using Cosmos.Kernel.Core.IO;
using SysThread = System.Threading.Thread;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Runs callbacks after a delay on a dedicated kernel thread. Unlike
/// interrupt-context software timers (TimerManager.Schedule), alarm callbacks
/// run in thread context and may block, allocate, and use scheduler
/// primitives. Requires the scheduler; resolution is bounded by the scheduler
/// tick.
/// </summary>
public static class AlarmSystem
{
    /// <summary>
    /// The method invoked when an alarm fires.
    /// </summary>
    public delegate void AlarmDelegate();

    /// <summary>
    /// A pending alarm entry.
    /// </summary>
    public struct Alarm
    {
        /// <summary>Unique alarm identifier, usable with <see cref="Remove"/>.</summary>
        public ulong Id { get; init; }

        /// <summary>Absolute deadline in Stopwatch timestamp ticks.</summary>
        public ulong Due { get; init; }

        /// <summary>Reload period in Stopwatch timestamp ticks; 0 for one-shot alarms.</summary>
        public ulong PeriodTicks { get; init; }

        /// <summary>The method invoked when the alarm fires.</summary>
        public AlarmDelegate Delegate { get; init; }
    }

    /// <summary>Wait used when no alarm is pending. Add signals the alarm thread early, so this is only a heartbeat.</summary>
    private const uint IdleWaitMs = 1000;

    private static readonly List<Alarm> s_alarms = new();
    private static readonly Mutex s_mutex = new();
    private static readonly ConditionVariable s_alarmsChanged = new();
    private static SysThread? s_thread;
    private static ulong s_nextId = 1;

    /// <summary>
    /// Schedules a one-shot alarm.
    /// </summary>
    /// <param name="delay">Delay before the alarm fires.</param>
    /// <param name="alarm">Method to invoke when the alarm fires.</param>
    /// <returns>The alarm ID, or 0 if the alarm could not be scheduled.</returns>
    public static ulong Add(TimeSpan delay, AlarmDelegate alarm)
    {
        return AddCore(delay, recurring: false, alarm);
    }

    /// <summary>
    /// Schedules a recurring alarm. The period restarts when the callback
    /// fires, so it must be longer than the callback's execution time.
    /// </summary>
    /// <param name="period">Period between firings; at least 1 ms.</param>
    /// <param name="alarm">Method to invoke each period.</param>
    /// <returns>The alarm ID, or 0 if the alarm could not be scheduled.</returns>
    public static ulong AddRecurring(TimeSpan period, AlarmDelegate alarm)
    {
        return AddCore(period, recurring: true, alarm);
    }

    /// <summary>
    /// Removes a pending alarm.
    /// </summary>
    /// <param name="id">ID returned by <see cref="Add"/> or <see cref="AddRecurring"/>.</param>
    /// <returns>True if the alarm was pending and has been removed.</returns>
    public static bool Remove(ulong id)
    {
        s_mutex.Acquire();

        for (int i = 0; i < s_alarms.Count; i++)
        {
            if (s_alarms[i].Id == id)
            {
                s_alarms.RemoveAt(i);
                s_mutex.Release();
                return true;
            }
        }

        s_mutex.Release();
        return false;
    }

    private static ulong AddCore(TimeSpan delay, bool recurring, AlarmDelegate alarm)
    {
        if (alarm == null)
        {
            return 0;
        }

        if (!SchedulerManager.Enabled)
        {
            Serial.WriteString("[AlarmSystem] ERROR: scheduler is not running, alarm not scheduled\n");
            return 0;
        }

        ulong delayTicks = ToStopwatchTicks(delay);
        if (recurring && delayTicks == 0)
        {
            Serial.WriteString("[AlarmSystem] ERROR: recurring alarm period must be at least 1 ms\n");
            return 0;
        }

        s_mutex.Acquire();

        EnsureStartedLocked();

        ulong id = s_nextId++;
        InsertSortedLocked(new Alarm
        {
            Id = id,
            Due = (ulong)Stopwatch.GetTimestamp() + delayTicks,
            PeriodTicks = recurring ? delayTicks : 0,
            Delegate = alarm
        });

        s_mutex.Release();

        // Wake the alarm thread so it recomputes its next deadline.
        s_alarmsChanged.Signal();
        return id;
    }

    /// <summary>
    /// Starts the alarm thread on first use. Caller must hold <see cref="s_mutex"/>.
    /// </summary>
    private static void EnsureStartedLocked()
    {
        if (s_thread != null)
        {
            return;
        }

        s_thread = new SysThread(DoTick);
        s_thread.Start();
    }

    /// <summary>
    /// Alarm thread main loop: fires due alarms, then sleeps until the next
    /// deadline or until <see cref="AddCore"/> signals a change.
    /// </summary>
    private static void DoTick()
    {
        Serial.WriteString("[AlarmSystem] Alarm thread started\n");

        s_mutex.Acquire();

        while (true)
        {
            ulong now = (ulong)Stopwatch.GetTimestamp();

            while (s_alarms.Count > 0 && s_alarms[0].Due <= now)
            {
                Alarm alarm = s_alarms[0];
                s_alarms.RemoveAt(0);

                if (alarm.PeriodTicks != 0)
                {
                    // Re-arm from now rather than from Due so a late wake-up
                    // doesn't cause a catch-up burst.
                    InsertSortedLocked(alarm with { Due = now + alarm.PeriodTicks });
                }

                // Fire outside the lock: the callback may block or call Add/Remove.
                s_mutex.Release();
                try
                {
                    alarm.Delegate();
                }
                catch (Exception)
                {
                    Serial.WriteString("[AlarmSystem] ERROR: alarm callback threw an exception\n");
                }

                s_mutex.Acquire();
                now = (ulong)Stopwatch.GetTimestamp();
            }

            uint waitMs = IdleWaitMs;
            if (s_alarms.Count > 0)
            {
                waitMs = TicksToWaitMs(s_alarms[0].Due - now);
            }

            // WaitTimeout releases the mutex while parked and re-acquires it
            // before returning. Insertions happen under the same mutex, so a
            // new earlier alarm either lands before the deadline computation
            // above or finds this thread parked and signals it — no lost
            // wake-ups.
            s_alarmsChanged.WaitTimeout(s_mutex, waitMs);
        }
    }

    /// <summary>
    /// Inserts an alarm keeping the list sorted by <see cref="Alarm.Due"/>.
    /// Caller must hold <see cref="s_mutex"/>.
    /// </summary>
    private static void InsertSortedLocked(Alarm alarm)
    {
        int index = s_alarms.Count;
        while (index > 0 && s_alarms[index - 1].Due > alarm.Due)
        {
            index--;
        }

        s_alarms.Insert(index, alarm);
    }

    private static ulong ToStopwatchTicks(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return 0;
        }

        return (ulong)delay.TotalMilliseconds * ((ulong)Stopwatch.Frequency / 1000);
    }

    private static uint TicksToWaitMs(ulong ticks)
    {
        ulong ticksPerMs = (ulong)Stopwatch.Frequency / 1000;
        if (ticksPerMs == 0)
        {
            return 1;
        }

        ulong ms = ticks / ticksPerMs + 1;
        return ms > IdleWaitMs ? IdleWaitMs : (uint)ms;
    }
}
