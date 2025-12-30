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
            ContextSwitch(prev, next);
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

    // ========== Platform-specific (to be implemented) ==========

    private static void ContextSwitch(Thread prev, Thread next)
    {
        // TODO: Implement via assembly
        // Save prev context (registers, stack pointer)
        // Restore next context
    }

    private static ulong GetTimestamp()
    {
        // TODO: Use rdtsc or platform timer
        return 0;
    }
}
