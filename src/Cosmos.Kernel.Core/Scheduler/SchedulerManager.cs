using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Manages scheduler lifecycle and dispatches to current scheduler.
/// </summary>
public static class SchedulerManager
{
    private static IScheduler _currentScheduler;
    private static PerCpuState[] _cpuStates;
    private static uint _cpuCount;
    private static SpinLock _globalLock;
    private static bool _enabled;
    private static uint _nextThreadId;

    /// <summary>
    /// Default time slice in nanoseconds (10ms).
    /// </summary>
    public const ulong DefaultQuantumNs = 10_000_000;

    // ========== Initialization ==========

    public static void Initialize(uint cpuCount)
    {
        _cpuCount = cpuCount;
        _cpuStates = new PerCpuState[cpuCount];

        for (uint i = 0; i < cpuCount; i++)
        {
            _cpuStates[i] = new PerCpuState { CpuId = i };
        }
    }

    public static void SetScheduler(IScheduler scheduler)
    {
        _globalLock.Acquire();
        try
        {
            if (_currentScheduler != null)
            {
                for (uint i = 0; i < _cpuCount; i++)
                    _currentScheduler.ShutdownCpu(_cpuStates[i]);
            }

            _currentScheduler = scheduler;

            for (uint i = 0; i < _cpuCount; i++)
                scheduler.InitializeCpu(_cpuStates[i]);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    // ========== Accessors ==========

    public static IScheduler Current => _currentScheduler;
    public static uint CpuCount => _cpuCount;
    public static PerCpuState GetCpuState(uint cpuId) => _cpuStates[cpuId];
    public static PerCpuState[] GetAllCpuStates() => _cpuStates;

    /// <summary>
    /// Whether the scheduler is enabled and processing timer ticks.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Allocates a new unique thread ID.
    /// </summary>
    public static uint AllocateThreadId() => _nextThreadId++;

    // ========== Thread Operations ==========

    public static void CreateThread(uint cpuId, Thread thread)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            _currentScheduler.OnThreadCreate(state, thread);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public static void ReadyThread(uint cpuId, Thread thread)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            thread.State = ThreadState.Ready;
            _currentScheduler.OnThreadReady(state, thread);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public static void BlockThread(uint cpuId, Thread thread)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            thread.State = ThreadState.Blocked;
            _currentScheduler.OnThreadBlocked(state, thread);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public static void ExitThread(uint cpuId, Thread thread)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            thread.State = ThreadState.Dead;
            _currentScheduler.OnThreadExit(state, thread);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public static void YieldThread(uint cpuId, Thread thread)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            _currentScheduler.OnThreadYield(state, thread);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    // ========== Scheduling ==========

    public static bool OnTick(uint cpuId, ulong elapsedNs)
    {
        var state = _cpuStates[cpuId];
        return _currentScheduler.OnTick(state, state.CurrentThread, elapsedNs);
    }

    public static void Schedule(uint cpuId)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();

        var prev = state.CurrentThread;
        var next = _currentScheduler.PickNext(state) ?? state.IdleThread;

        if (next != prev)
        {
            state.CurrentThread = next;
            next.State = ThreadState.Running;
            next.LastScheduledAt = GetTimestamp();

            state.Lock.Release();
            DoContextSwitch(prev, next);
        }
        else
        {
            state.Lock.Release();
        }
    }

    public static void SetPriority(uint cpuId, Thread thread, long priority)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            _currentScheduler.SetPriority(state, thread, priority);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public static long GetPriority(Thread thread)
    {
        return _currentScheduler.GetPriority(thread);
    }

    // ========== Load Balancing ==========

    public static uint SelectCpu(Thread thread, uint currentCpu)
    {
        return _currentScheduler.SelectCpu(thread, currentCpu, _cpuCount);
    }

    public static void Balance(uint cpuId)
    {
        var state = _cpuStates[cpuId];
        _currentScheduler.Balance(state, _cpuStates);
    }

    // ========== Timer Interrupt Handling ==========

    /// <summary>
    /// Called from timer interrupt handler to process scheduling.
    /// This is the main entry point for preemptive scheduling.
    /// </summary>
    /// <param name="cpuId">Current CPU ID.</param>
    /// <param name="currentRsp">Current RSP from IRQ context (pointer to saved context).</param>
    /// <param name="elapsedNs">Nanoseconds since last tick.</param>
    public static void OnTimerInterrupt(uint cpuId, nuint currentRsp, ulong elapsedNs)
    {
        if (!_enabled || _currentScheduler == null)
            return;

        var state = _cpuStates[cpuId];

        // Update timing and check if preemption needed
        bool needsReschedule = _currentScheduler.OnTick(state, state.CurrentThread, elapsedNs);

        if (needsReschedule)
        {
            ScheduleFromInterrupt(cpuId, currentRsp);
        }
    }

    /// <summary>
    /// Performs scheduling from within an interrupt context.
    /// Picks next thread and sets up context switch if needed.
    /// </summary>
    /// <param name="cpuId">Current CPU ID.</param>
    /// <param name="currentRsp">Current RSP (pointer to saved context on stack).</param>
    public static void ScheduleFromInterrupt(uint cpuId, nuint currentRsp)
    {
        var state = _cpuStates[cpuId];

        // No need for lock - we're in interrupt context
        var prev = state.CurrentThread;
        var next = _currentScheduler.PickNext(state) ?? state.IdleThread;

        if (next == null)
        {
            // No thread to run - shouldn't happen if idle thread exists
            return;
        }

        if (next != prev)
        {
            // Save current thread's stack pointer
            if (prev != null)
            {
                prev.StackPointer = currentRsp;
                if (prev.State == ThreadState.Running)
                    prev.State = ThreadState.Ready;

                // Put previous thread back in run queue if still runnable
                if (prev.State == ThreadState.Ready)
                    _currentScheduler.OnThreadYield(state, prev);
            }

            // Switch to next thread
            state.CurrentThread = next;
            next.State = ThreadState.Running;
            next.LastScheduledAt = GetTimestamp();

            // Request context switch - the IRQ stub will swap RSP
            ContextSwitch.SetContextSwitchRsp(next.StackPointer);
        }
    }

    // ========== Platform-specific ==========

    private static void DoContextSwitch(Thread prev, Thread next)
    {
        // This is for non-interrupt context switches (e.g., voluntary yield)
        // Not fully implemented - use ScheduleFromInterrupt for preemptive switching
        if (prev != null)
            prev.State = ThreadState.Ready;

        next.State = ThreadState.Running;
        ContextSwitch.SetContextSwitchRsp(next.StackPointer);
    }

    private static ulong GetTimestamp()
    {
        // TODO: Use rdtsc or platform timer
        return 0;
    }
}
