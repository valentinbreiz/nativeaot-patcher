using System.Collections.Generic;
using Cosmos.Kernel.Core.Scheduler;
using Thread = Cosmos.Kernel.Core.Scheduler.Thread;

namespace Cosmos.Kernel.Tests.Threading;

/// <summary>
/// Round-Robin scheduling policy implemented the way a user kernel would:
/// entirely over the public scheduler seam (<see cref="IScheduler"/>,
/// <see cref="SchedulerManager"/>, the <see cref="SchedulerExtensible.SchedulerData"/>
/// slots), with no access to Cosmos.Kernel.Core internals. It follows the
/// Round-Robin sketch in docs/articles/dev/scheduler-plugging.md: a FIFO run
/// queue per CPU, a remaining-quantum counter per thread, fixed-quantum
/// preemption in <see cref="OnTick"/>, and <see cref="PickNext"/> dequeuing
/// the head.
/// </summary>
public sealed class RoundRobinScheduler : IScheduler
{
    /// <summary>
    /// Fixed time slice per turn: two scheduler ticks, so the charge in
    /// <see cref="OnTick"/> has to survive an intermediate tick instead of
    /// expiring trivially on every one.
    /// </summary>
    public const ulong QuantumNs = 2 * SchedulerManager.DefaultQuantumNs;

    public string Name => "RoundRobin";

    // ========== Lifecycle ==========

    public void InitializeCpu(PerCpuState cpuState)
    {
        cpuState.SchedulerData = new RoundRobinCpuData();
    }

    public void ShutdownCpu(PerCpuState cpuState)
    {
        cpuState.SchedulerData = null;
    }

    // ========== Thread Lifecycle ==========

    public void OnThreadCreate(PerCpuState cpuState, Thread thread)
    {
        thread.SchedulerData = new RoundRobinThreadData { RemainingNs = QuantumNs };
    }

    public void OnThreadReady(PerCpuState cpuState, Thread thread)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData == null)
        {
            return;
        }

        // Nothing survives a park: a woken thread starts a fresh slice.
        ThreadDataOf(thread)?.RemainingNs = QuantumNs;

        EnqueueTail(cpuData, thread);
    }

    public void OnThreadBlocked(PerCpuState cpuState, Thread thread)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData == null)
        {
            return;
        }

        RemoveFromQueue(cpuData, thread);
    }

    public void OnThreadYield(PerCpuState cpuState, Thread thread)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData == null)
        {
            return;
        }

        ThreadDataOf(thread)?.RemainingNs = QuantumNs;

        EnqueueTail(cpuData, thread);
    }

    public void OnThreadExit(PerCpuState cpuState, Thread thread)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData != null)
        {
            RemoveFromQueue(cpuData, thread);
        }

        thread.SchedulerData = null;
    }

    // ========== Scheduling Decisions ==========

    public Thread? PickNext(PerCpuState cpuState)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData == null || cpuData.RunQueue.Count == 0)
        {
            return null;
        }

        Thread head = cpuData.RunQueue[0];
        cpuData.RunQueue.RemoveAt(0);
        return head;
    }

    public void OnPickFailed(PerCpuState cpuState, Thread thread)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData == null)
        {
            return;
        }

        // The pick was not honored: put the thread back at the head so the
        // FIFO order it had already earned is preserved.
        if (!QueueHolds(cpuData, thread))
        {
            cpuData.RunQueue.Insert(0, thread);
        }
    }

    public bool OnTick(PerCpuState cpuState, Thread current, ulong elapsedNs)
    {
        RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
        if (cpuData == null || current == null)
        {
            return false;
        }

        current.TotalRuntime += elapsedNs;

        RoundRobinThreadData? threadData = ThreadDataOf(current);
        if (threadData == null)
        {
            // Foreign bookkeeping (a thread created under the previously
            // installed policy) or a thread that already exited: rotate it at
            // tick granularity whenever another thread is waiting.
            return cpuData.RunQueue.Count > 0;
        }

        threadData.RemainingNs = elapsedNs >= threadData.RemainingNs
            ? 0
            : threadData.RemainingNs - elapsedNs;

        if (threadData.RemainingNs > 0)
        {
            return false;
        }

        if (cpuData.RunQueue.Count == 0)
        {
            // Sole runnable thread: grant a fresh slice in place instead of
            // bouncing through the idle thread and back.
            threadData.RemainingNs = QuantumNs;
            return false;
        }

        return true;
    }

    // ========== Load Balancing (dormant until SMP) ==========

    public uint SelectCpu(Thread thread, uint currentCpu, uint cpuCount)
    {
        // One FIFO per CPU and no load metric worth consulting: keep the
        // thread where it is, which also honors ThreadFlags.Pinned.
        return currentCpu;
    }

    public void OnThreadMigrate(Thread thread, PerCpuState fromState, PerCpuState toState)
    {
        RoundRobinCpuData? fromData = CpuDataOf(fromState);
        RoundRobinCpuData? toData = CpuDataOf(toState);
        if (fromData == null || toData == null)
        {
            return;
        }

        RemoveFromQueue(fromData, thread);
        EnqueueTail(toData, thread);
    }

    public void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates)
    {
        // FIFO order carries no cross-CPU invariant to rebalance; placement
        // stays wherever SelectCpu (which honors Pinned) put the thread.
    }

    // ========== Dynamic Reconfiguration ==========

    public void SetPriority(PerCpuState cpuState, Thread thread, long priority)
    {
        // Round-Robin has no priorities: every thread gets the same fixed
        // quantum, so the request is deliberately ignored.
    }

    public long GetPriority(Thread thread)
    {
        return 0;
    }

    // ========== Diagnostics ==========

    public int GetRunQueueCount(PerCpuState cpuState)
    {
        // The SchedulerInfo facade calls the diagnostics hooks from thread
        // context; guard them against the tick ourselves, per the plugging guide.
        using (SchedulerManager.MaskInterrupts())
        {
            RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
            return cpuData?.RunQueue.Count ?? 0;
        }
    }

    public Thread? GetRunQueueThread(PerCpuState cpuState, int index)
    {
        using (SchedulerManager.MaskInterrupts())
        {
            RoundRobinCpuData? cpuData = CpuDataOf(cpuState);
            if (cpuData == null || index < 0 || index >= cpuData.RunQueue.Count)
            {
                return null;
            }

            return cpuData.RunQueue[index];
        }
    }

    // ========== Private Helpers ==========

    // GetSchedulerData<T> casts, so on a policy that gets installed on a live
    // kernel it would throw in interrupt context for threads still carrying
    // the previous policy's bookkeeping (the boot main thread keeps its
    // StrideThreadData across the swap). Read the slots with 'as' instead:
    // foreign data degrades to "absent", which every hook already tolerates.
    private static RoundRobinCpuData? CpuDataOf(PerCpuState cpuState)
    {
        return cpuState.SchedulerData as RoundRobinCpuData;
    }

    private static RoundRobinThreadData? ThreadDataOf(Thread thread)
    {
        return thread.SchedulerData as RoundRobinThreadData;
    }

    private static bool QueueHolds(RoundRobinCpuData cpuData, Thread thread)
    {
        for (int i = 0; i < cpuData.RunQueue.Count; i++)
        {
            if (ReferenceEquals(cpuData.RunQueue[i], thread))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnqueueTail(RoundRobinCpuData cpuData, Thread thread)
    {
        // Presence guard: ReadyThread can fire for a thread the interrupt
        // exit will also re-queue (the idle thread's block/resurrect churn in
        // Mutex.Acquire), and a double entry would grant double turns.
        if (!QueueHolds(cpuData, thread))
        {
            cpuData.RunQueue.Add(thread);
        }
    }

    private static void RemoveFromQueue(RoundRobinCpuData cpuData, Thread thread)
    {
        // List<T>.Remove/Contains route through EqualityComparer<T>.Default,
        // which needs runtime helpers the kernel does not provide; scan with
        // ReferenceEquals and use RemoveAt.
        for (int i = 0; i < cpuData.RunQueue.Count; i++)
        {
            if (ReferenceEquals(cpuData.RunQueue[i], thread))
            {
                cpuData.RunQueue.RemoveAt(i);
                return;
            }
        }
    }
}

/// <summary>
/// Per-CPU Round-Robin bookkeeping: the FIFO run queue, pre-sized to the
/// registry limit so the scheduler paths never grow it (no allocation in
/// interrupt context).
/// </summary>
public sealed class RoundRobinCpuData
{
    public List<Thread> RunQueue { get; } = new(Thread.MaxThreadCount);
}

/// <summary>
/// Per-thread Round-Robin bookkeeping: what is left of the current time slice.
/// </summary>
public sealed class RoundRobinThreadData
{
    public ulong RemainingNs { get; set; }
}
