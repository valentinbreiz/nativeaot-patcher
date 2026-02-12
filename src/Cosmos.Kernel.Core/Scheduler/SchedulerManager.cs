using System.Diagnostics;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Manages scheduler lifecycle and dispatches to current scheduler.
/// </summary>
public static class SchedulerManager
{
    private static IScheduler? _currentScheduler;
    private static PerCpuState[]? _cpuStates;
    private static uint _cpuCount;
    private static SpinLock _globalLock;
    private static bool _enabled;
    private static uint _nextThreadId;

    /// <summary>
    /// Default time slice in nanoseconds (10ms).
    /// </summary>
    public const ulong DefaultQuantumNs = 10_000_000;

    /// <summary>
    /// Whether scheduler support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.SchedulerEnabled;

    private static void ThrowIfDisabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Scheduler support is disabled. Set CosmosEnableScheduler=true in your csproj to enable it.");
    }

    // ========== Initialization ==========

    public static void Initialize(uint cpuCount)
    {
        ThrowIfDisabled();

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
            {
                scheduler.InitializeCpu(_cpuStates[i]);
            }

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
    /// Sets up the idle thread for a CPU. Should only be called during initialization.
    /// </summary>
    public static void SetupIdleThread(uint cpuId, Thread idleThread)
    {
        var state = _cpuStates[cpuId];
        state.IdleThread = idleThread;
        state.CurrentThread = idleThread;
    }

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
        ThrowIfDisabled();

        Serial.WriteString("[SCHED] CreateThread: entering\n");
        var state = _cpuStates[cpuId];
        Serial.WriteString("[SCHED] CreateThread: acquiring lock\n");
        state.Lock.Acquire();
        Serial.WriteString("[SCHED] CreateThread: lock acquired\n");
        try
        {
            _currentScheduler.OnThreadCreate(state, thread);
        }
        finally
        {
            state.Lock.Release();
        }
        Serial.WriteString("[SCHED] CreateThread: done\n");
    }

    public static void ReadyThread(uint cpuId, Thread thread)
    {
        ThrowIfDisabled();

        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try
        {
            // Only set to Ready if not a new thread (Created).
            // New threads stay Created until they actually start running.
            // This allows ScheduleFromInterrupt to detect first-time execution.
            if (thread.State != ThreadState.Created)
                thread.State = ThreadState.Ready;
            _currentScheduler.OnThreadReady(state, thread);

            Serial.WriteString("[SCHED] Thread ");
            Serial.WriteNumber(thread.Id);
            Serial.WriteString(" is now ready, RSP=");
            Serial.WriteHexWithPrefix((ulong)thread.StackPointer);
            Serial.WriteString("\n");
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public static void BlockThread(uint cpuId, Thread thread)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = _cpuStates[cpuId];

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
    }

    public static void ExitThread(uint cpuId, Thread thread)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = _cpuStates[cpuId];

            state.Lock.Acquire();
            try
            {
                thread.State = ThreadState.Dead;
                _currentScheduler.OnThreadExit(state, thread);
                Serial.WriteString("[SCHED] ExitThread: OnThreadExit done\n");
            }
            finally
            {
                state.Lock.Release();
            }
        }
    }

    public static void YieldThread(uint cpuId, Thread thread)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = _cpuStates[cpuId];

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

        if (next == null)
        {
            state.Lock.Release();
            return;
        }

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

    // Debug counter to avoid flooding serial output
    private static uint _tickCount;

    /// <summary>
    /// Called from timer interrupt handler to process scheduling.
    /// This is the main entry point for preemptive scheduling.
    /// </summary>
    /// <param name="cpuId">Current CPU ID.</param>
    /// <param name="currentRsp">Current RSP from IRQ context (pointer to saved context).</param>
    /// <param name="elapsedNs">Nanoseconds since last tick.</param>
    public static void OnTimerInterrupt(uint cpuId, nuint currentRsp, ulong elapsedNs)
    {
        _tickCount++;

        // Log first 10 ticks and then every 50 ticks
        if (_tickCount <= 10 || _tickCount % 50 == 0)
        {
            Serial.WriteString("[SCHED] Tick ");
            Serial.WriteNumber(_tickCount);
            Serial.WriteString(" enabled=");
            Serial.WriteString(_enabled ? "1" : "0");
            Serial.WriteString("\n");
        }

        if (!_enabled || _currentScheduler == null || _cpuStates == null)
            return;

        if (cpuId >= _cpuCount)
            return;

        var state = _cpuStates[cpuId];
        if (state == null || state.CurrentThread == null)
            return;

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
            // No thread to switch to - just continue with current
            // This happens when all threads have exited
            return;
        }

        if (next != prev)
        {
            /*
            Serial.WriteString("[SCHED] Context switch: thread ");
            Serial.WriteNumber(prev?.Id ?? 0);
            Serial.WriteString(" -> ");
            Serial.WriteNumber(next.Id);
            Serial.WriteString(" RSP=");
            Serial.WriteHexWithPrefix((ulong)next.StackPointer);
            Serial.WriteString("\n");
            */

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

            // Check if this is a NEW thread (never run before) or RESUMED
            bool isNewThread = next.State == ThreadState.Created;

            next.State = ThreadState.Running;
            next.LastScheduledAt = GetTimestamp();

            // Request context switch - set new thread flag and target RSP
            ContextSwitch.SetContextSwitchNewThread(isNewThread ? 1 : 0);
            ContextSwitch.SetContextSwitchRsp(next.StackPointer);
        }
    }

    // ========== Platform-specific ==========

    private static void DoContextSwitch(Thread prev, Thread next)
    {
        // This is for non-interrupt context switches (e.g., voluntary yield)
        // Not fully implemented - use ScheduleFromInterrupt for preemptive switching
        if (next == null)
            return;

        if (prev != null)
            prev.State = ThreadState.Ready;

        next.State = ThreadState.Running;
        ContextSwitch.SetContextSwitchRsp(next.StackPointer);
    }

    private static ulong GetTimestamp()
    {
        return (ulong)Stopwatch.GetTimestamp();
    }
}
