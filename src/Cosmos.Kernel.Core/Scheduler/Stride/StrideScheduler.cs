using System;
using System.Diagnostics;

namespace Cosmos.Kernel.Core.Scheduler.Stride;

/// <summary>
/// Stride scheduler with interactive process support.
/// </summary>
public class StrideScheduler : IScheduler
{
    public string Name => "Stride";

    /// <summary>
    /// Large constant for stride precision.
    /// </summary>
    public const ulong Stride1 = 1 << 20;

    /// <summary>
    /// Default tickets for new threads.
    /// </summary>
    public const ulong DefaultTickets = 100;

    /// <summary>
    /// Sleep 2x more than run = interactive.
    /// </summary>
    private const ulong InteractiveSleepRatio = 2;

    /// <summary>
    /// Priority boost decays after 5ms.
    /// </summary>
    private const ulong WakeupBoostDecayNs = 5_000_000;

    // ========== Lifecycle ==========

    public void InitializeCpu(PerCpuState cpuState)
    {
        cpuState.SchedulerData = new StrideCpuData();
    }

    public void ShutdownCpu(PerCpuState cpuState)
    {
        cpuState.SchedulerData = null;
    }

    // ========== Thread Lifecycle ==========

    public void OnThreadCreate(PerCpuState cpuState, Thread thread)
    {
        var data = new StrideThreadData
        {
            Tickets = DefaultTickets,
            Stride = Stride1 / DefaultTickets,
            Pass = 0,
            Remain = 0
        };
        thread.SchedulerData = data;
    }

    public void OnThreadReady(PerCpuState cpuState, Thread thread)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = thread.GetSchedulerData<StrideThreadData>();

        UpdateGlobalPass(cpuData);

        ulong now = GetTimestamp();
        bool wasBlocked = thread.State == ThreadState.Blocked;

        if (wasBlocked)
        {
            ulong sleepDuration = now - threadData.LastWakeup;

            // Detect interactive behavior
            if (sleepDuration > 0 && thread.TotalRuntime > 0)
            {
                if (sleepDuration > thread.TotalRuntime * InteractiveSleepRatio)
                    threadData.IsInteractive = true;
            }

            // Apply priority boost for interactive threads
            if (threadData.IsInteractive)
            {
                threadData.Pass = (long)cpuData.GlobalPass - (long)(threadData.Stride / 2);
                threadData.IsBoosted = true;
            }
            else
            {
                // CFS-style cap to prevent starvation
                long minPass = (long)cpuData.GlobalPass - (long)(Stride1 * 2);
                long newPass = (long)cpuData.GlobalPass + threadData.Remain;
                threadData.Pass = Math.Max(newPass, minPass);
            }

            threadData.LastWakeup = now;
        }
        else
        {
            // New thread - start at global pass
            threadData.Pass = (long)cpuData.GlobalPass;
        }

        InsertByPass(cpuData, thread);
        cpuData.TotalTickets += threadData.Tickets;
    }

    public void OnThreadBlocked(PerCpuState cpuState, Thread thread)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = thread.GetSchedulerData<StrideThreadData>();

        UpdateGlobalPass(cpuData);

        threadData.Remain = threadData.Pass - (long)cpuData.GlobalPass;
        threadData.SleepCount++;

        cpuData.RunQueue.Remove(thread);
        cpuData.TotalTickets -= threadData.Tickets;
    }

    public void OnThreadExit(PerCpuState cpuState, Thread thread)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = thread.GetSchedulerData<StrideThreadData>();

        cpuData.RunQueue.Remove(thread);
        cpuData.TotalTickets -= threadData.Tickets;
        thread.SchedulerData = null;
    }

    public void OnThreadYield(PerCpuState cpuState, Thread thread)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        InsertByPass(cpuData, thread);
    }

    // ========== Scheduling Decisions ==========

    public Thread PickNext(PerCpuState cpuState)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();

        if (cpuData.RunQueue.Count == 0)
            return null;

        var selected = cpuData.RunQueue[0];
        cpuData.RunQueue.RemoveAt(0);

        return selected;
    }

    public void OnPickFailed(PerCpuState cpuState, Thread thread)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        InsertByPass(cpuData, thread);
    }

    public bool OnTick(PerCpuState cpuState, Thread current, ulong elapsedNs)
    {
        if (current == null)
            return false;

        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = current.GetSchedulerData<StrideThreadData>();

        current.TotalRuntime += elapsedNs;

        ulong quantum = SchedulerManager.DefaultQuantumNs;
        threadData.Pass += (long)((threadData.Stride * elapsedNs) / quantum);

        // Decay priority boost
        if (threadData.IsBoosted)
        {
            ulong timeSinceWake = GetTimestamp() - threadData.LastWakeup;
            if (timeSinceWake > WakeupBoostDecayNs)
                threadData.IsBoosted = false;
        }

        UpdateGlobalPass(cpuData);

        // Check for preemption
        if (cpuData.RunQueue.Count > 0)
        {
            var nextData = cpuData.RunQueue[0].GetSchedulerData<StrideThreadData>();
            if (nextData.Pass < threadData.Pass)
                return true;
        }

        return elapsedNs >= quantum;
    }

    // ========== Load Balancing ==========

    public uint SelectCpu(Thread thread, uint currentCpu, uint cpuCount)
    {
        if ((thread.Flags & ThreadFlags.Pinned) != 0)
            return currentCpu;

        uint best = currentCpu;
        ulong bestLoad = GetCpuLoad(currentCpu);

        for (uint cpu = 0; cpu < cpuCount; cpu++)
        {
            if (cpu == currentCpu)
                continue;

            ulong load = GetCpuLoad(cpu);
            if (load < bestLoad * 80 / 100)
            {
                best = cpu;
                bestLoad = load;
            }
        }

        return best;
    }

    public void OnThreadMigrate(Thread thread, PerCpuState fromState, PerCpuState toState)
    {
        var fromData = fromState.GetSchedulerData<StrideCpuData>();
        var toData = toState.GetSchedulerData<StrideCpuData>();
        var threadData = thread.GetSchedulerData<StrideThreadData>();

        fromData.RunQueue.Remove(thread);
        fromData.TotalTickets -= threadData.Tickets;

        threadData.Pass = (long)toData.GlobalPass + threadData.Remain;

        InsertByPass(toData, thread);
        toData.TotalTickets += threadData.Tickets;
    }

    public void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        if (cpuData.RunQueue.Count > 0)
            return;

        PerCpuState busiest = null;
        int maxCount = 0;

        foreach (var state in allCpuStates)
        {
            if (state == cpuState)
                continue;

            var data = state.GetSchedulerData<StrideCpuData>();
            if (data.RunQueue.Count > maxCount)
            {
                maxCount = data.RunQueue.Count;
                busiest = state;
            }
        }

        if (busiest == null || maxCount <= 1)
            return;

        var busiestData = busiest.GetSchedulerData<StrideCpuData>();
        var victim = busiestData.RunQueue[busiestData.RunQueue.Count - 1];

        if ((victim.Flags & ThreadFlags.Pinned) == 0)
            OnThreadMigrate(victim, busiest, cpuState);
    }

    // ========== Dynamic Reconfiguration ==========

    public void SetPriority(PerCpuState cpuState, Thread thread, long priority)
    {
        if (priority <= 0)
            priority = 1;

        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = thread.GetSchedulerData<StrideThreadData>();

        UpdateGlobalPass(cpuData);

        ulong oldTickets = threadData.Tickets;
        ulong newTickets = (ulong)priority;
        ulong newStride = Stride1 / newTickets;

        long remain = threadData.Pass - (long)cpuData.GlobalPass;
        remain = (remain * (long)newStride) / (long)threadData.Stride;
        threadData.Pass = (long)cpuData.GlobalPass + remain;

        cpuData.TotalTickets = cpuData.TotalTickets - oldTickets + newTickets;
        threadData.Tickets = newTickets;
        threadData.Stride = newStride;

        if (thread.State == ThreadState.Ready)
        {
            cpuData.RunQueue.Remove(thread);
            InsertByPass(cpuData, thread);
        }
    }

    public long GetPriority(Thread thread)
    {
        return (long)thread.GetSchedulerData<StrideThreadData>().Tickets;
    }

    // ========== Private Helpers ==========

    private void UpdateGlobalPass(StrideCpuData cpuData)
    {
        if (cpuData.TotalTickets == 0)
            return;

        ulong now = GetTimestamp();
        ulong elapsed = now - cpuData.LastPassUpdate;
        ulong globalStride = Stride1 / cpuData.TotalTickets;
        cpuData.GlobalPass += (globalStride * elapsed) / SchedulerManager.DefaultQuantumNs;
        cpuData.LastPassUpdate = now;
    }

    private void InsertByPass(StrideCpuData cpuData, Thread thread)
    {
        var threadData = thread.GetSchedulerData<StrideThreadData>();
        int index = 0;

        for (; index < cpuData.RunQueue.Count; index++)
        {
            var otherData = cpuData.RunQueue[index].GetSchedulerData<StrideThreadData>();
            if (threadData.Pass <= otherData.Pass)
                break;
        }

        cpuData.RunQueue.Insert(index, thread);
    }

    private ulong GetCpuLoad(uint cpuId)
    {
        var state = SchedulerManager.GetCpuState(cpuId);
        return state.GetSchedulerData<StrideCpuData>().TotalTickets;
    }

    private ulong GetTimestamp()
    {
        return (ulong)Stopwatch.GetTimestamp();
    }
}
