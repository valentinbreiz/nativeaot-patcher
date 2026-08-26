using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.Scheduler;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
using SchedThreadState = Cosmos.Kernel.Core.Scheduler.ThreadState;

namespace Cosmos.Kernel.System.Diagnostics;

/// <summary>
/// Read-only view of the kernel scheduler: feature and lifecycle state,
/// per-CPU thread tables, and the global thread registry, plus a request
/// to terminate a thread by ID. All reads are allocation-free and safe to
/// poll from a monitor loop; snapshots of threads that are being
/// rescheduled concurrently may be one tick stale.
/// </summary>
public static class SchedulerInfo
{
    /// <summary>
    /// Nanoseconds per millisecond, for converting the nanosecond figures
    /// reported here into milliseconds.
    /// </summary>
    public const ulong NanosecondsPerMillisecond = 1_000_000;

    /// <summary>
    /// Whether scheduler support is compiled into this kernel
    /// (the <c>CosmosEnableScheduler</c> feature switch).
    /// </summary>
    public static bool IsSupported => CosmosFeatures.SchedulerEnabled;

    /// <summary>
    /// Whether a scheduler has been installed and per-CPU state exists.
    /// </summary>
    public static bool IsInitialized => SchedulerManager.Current != null;

    /// <summary>
    /// Whether the scheduler is processing timer ticks and preempting
    /// threads.
    /// </summary>
    public static bool IsRunning => SchedulerManager.Enabled;

    /// <summary>
    /// Name of the installed scheduler, or <see langword="null"/> before
    /// initialization.
    /// </summary>
    public static string? SchedulerName => SchedulerManager.Current?.Name;

    /// <summary>
    /// Number of CPUs the scheduler manages.
    /// </summary>
    public static uint CpuCount => SchedulerManager.CpuCount;

    /// <summary>
    /// Length of the time slice handed to each thread, in nanoseconds.
    /// </summary>
    public static ulong QuantumNs => SchedulerManager.DefaultQuantumNs;

    /// <summary>
    /// Total busy CPU time in nanoseconds, summed over all CPUs and all
    /// non-idle threads since boot. Monotonic across thread exits; sample
    /// it over a wall-clock window to derive CPU utilization.
    /// </summary>
    public static ulong BusyCpuTimeNs => SchedulerManager.GetBusyCpuTimeNs();

    /// <summary>
    /// Number of live threads in the registry.
    /// </summary>
    public static int ThreadCount => SchedulerManager.ThreadCount;

    /// <summary>
    /// Number of slots in the thread registry. Slots can be empty; iterate
    /// from 0 to this value and probe each with <see cref="TryGetThread"/>.
    /// </summary>
    public static int ThreadSlotCount => SchedulerManager.Threads?.Length ?? 0;

    /// <summary>
    /// Snapshots the thread in the given registry slot.
    /// </summary>
    /// <param name="slot">Registry slot index, from 0 to <see cref="ThreadSlotCount"/> exclusive.</param>
    /// <param name="info">Snapshot of the thread occupying the slot.</param>
    /// <returns><see langword="false"/> when the slot is out of range or empty.</returns>
    public static bool TryGetThread(int slot, out KernelThreadInfo info)
    {
        SchedThread?[]? threads = SchedulerManager.Threads;
        if (threads == null || slot < 0 || slot >= threads.Length || threads[slot] is not SchedThread thread)
        {
            info = default;
            return false;
        }

        info = Snapshot(thread);
        return true;
    }

    /// <summary>
    /// Snapshots the thread currently running on a CPU.
    /// </summary>
    /// <param name="cpuId">CPU to inspect.</param>
    /// <param name="info">Snapshot of the running thread.</param>
    /// <returns><see langword="false"/> when the CPU ID is out of range or no thread is current.</returns>
    public static bool TryGetCurrentThread(uint cpuId, out KernelThreadInfo info)
    {
        SchedThread? thread = cpuId < SchedulerManager.CpuCount
            ? SchedulerManager.GetCpuState(cpuId)?.CurrentThread
            : null;
        if (thread == null)
        {
            info = default;
            return false;
        }

        info = Snapshot(thread);
        return true;
    }

    /// <summary>
    /// Returns the number of threads waiting in a CPU's run queue.
    /// </summary>
    /// <param name="cpuId">CPU to inspect.</param>
    /// <returns>Queue length, or 0 when the CPU ID is out of range or the scheduler is not initialized.</returns>
    public static int GetRunQueueCount(uint cpuId)
    {
        IScheduler? scheduler = SchedulerManager.Current;
        PerCpuState? state = cpuId < SchedulerManager.CpuCount ? SchedulerManager.GetCpuState(cpuId) : null;
        return scheduler != null && state != null ? scheduler.GetRunQueueCount(state) : 0;
    }

    /// <summary>
    /// Snapshots a thread in a CPU's run queue by position.
    /// </summary>
    /// <param name="cpuId">CPU to inspect.</param>
    /// <param name="index">Queue position, from 0 to <see cref="GetRunQueueCount"/> exclusive.</param>
    /// <param name="info">Snapshot of the queued thread.</param>
    /// <returns><see langword="false"/> when the CPU ID or index is out of range.</returns>
    public static bool TryGetRunQueueThread(uint cpuId, int index, out KernelThreadInfo info)
    {
        IScheduler? scheduler = SchedulerManager.Current;
        PerCpuState? state = cpuId < SchedulerManager.CpuCount ? SchedulerManager.GetCpuState(cpuId) : null;
        SchedThread? thread = scheduler != null && state != null ? scheduler.GetRunQueueThread(state, index) : null;
        if (thread == null)
        {
            info = default;
            return false;
        }

        info = Snapshot(thread);
        return true;
    }

    /// <summary>
    /// Requests termination of a thread by ID. A thread waiting in a run
    /// queue is terminated immediately; a currently running thread is
    /// marked dead and reaped by the scheduler on its next reschedule.
    /// Idle threads are refused.
    /// </summary>
    /// <param name="threadId">ID of the thread to terminate.</param>
    /// <returns>What happened to the thread; see <see cref="ThreadKillResult"/>.</returns>
    public static ThreadKillResult RequestKill(uint threadId)
    {
        IScheduler? scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            return ThreadKillResult.NotFound;
        }

        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            PerCpuState? state = SchedulerManager.GetCpuState(cpuId);
            if (state == null)
            {
                continue;
            }

            SchedThread? current = state.CurrentThread;
            if (current?.Id == threadId)
            {
                if ((current.Flags & ThreadFlags.IdleThread) != 0)
                {
                    return ThreadKillResult.RefusedIdle;
                }

                current.State = SchedThreadState.Dead;
                return ThreadKillResult.MarkedForExit;
            }

            int count = scheduler.GetRunQueueCount(state);
            for (int i = 0; i < count; i++)
            {
                SchedThread? thread = scheduler.GetRunQueueThread(state, i);
                if (thread?.Id != threadId)
                {
                    continue;
                }

                if ((thread.Flags & ThreadFlags.IdleThread) != 0)
                {
                    return ThreadKillResult.RefusedIdle;
                }

                SchedulerManager.ExitThread(cpuId, thread);
                return ThreadKillResult.Killed;
            }
        }

        return ThreadKillResult.NotFound;
    }

    private static KernelThreadInfo Snapshot(SchedThread thread)
    {
        bool hasPriority = thread.SchedulerData != null;
        long priority = hasPriority ? SchedulerManager.Current?.GetPriority(thread) ?? 0 : 0;
        return new KernelThreadInfo(
            thread.Id,
            thread.CpuId,
            MapState(thread.State),
            (thread.Flags & ThreadFlags.IdleThread) != 0,
            (thread.Flags & ThreadFlags.Managed) != 0,
            thread.TotalRuntime,
            thread.StackSize,
            priority,
            hasPriority);
    }

    private static KernelThreadState MapState(SchedThreadState state) => state switch
    {
        SchedThreadState.Created => KernelThreadState.Created,
        SchedThreadState.Ready => KernelThreadState.Ready,
        SchedThreadState.Running => KernelThreadState.Running,
        SchedThreadState.Blocked => KernelThreadState.Blocked,
        SchedThreadState.Sleeping => KernelThreadState.Sleeping,
        _ => KernelThreadState.Dead,
    };
}
