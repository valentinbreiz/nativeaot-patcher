using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using SysThread = System.Threading.Thread;

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

    // Global thread registry: tracks ALL live threads across all states
    // (Running, Ready, Blocked, Sleeping). Used by GC to scan all thread stacks.
    // Allocated once at init to avoid heap allocations during GC.
    private static Thread?[]? _allThreads;
    private static int _allThreadCount;

    // Cumulative TotalRuntime of exited non-idle threads. Live-thread runtime
    // disappears from _allThreads on UnregisterThread, so we move it here to
    // keep GetBusyCpuTimeNs monotonic across thread lifecycle.
    private static ulong _exitedNonIdleRuntimeNs;

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
        {
            throw new InvalidOperationException("Scheduler support is disabled. Set CosmosEnableScheduler=true in your csproj to enable it.");
        }
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

        // Pre-allocate thread registry
        _allThreads = new Thread?[Thread.MaxThreadCount];
        _allThreadCount = 0;
    }

    public static void SetScheduler(IScheduler scheduler)
    {
        _globalLock.Acquire();
        try
        {
            if (_currentScheduler != null)
            {
                for (uint i = 0; i < _cpuCount; i++)
                {
                    _currentScheduler.ShutdownCpu(_cpuStates[i]);
                }
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
        RegisterThread(idleThread);
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

    // ========== Thread Entry Dispatch ==========
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "StartThread")]
    private static extern void StartThread(SysThread aThis, IntPtr parameter);

    /// <summary>
    /// <para>
    /// Entry body for newly scheduled threads. Called from
    /// <see cref="Cosmos.Kernel.Core.Bridge.ThreadNative.EntryPointStub"/>,
    /// whose address is passed as the initial RIP / PC to the context-switch
    /// assembly by whoever creates the thread (e.g. ThreadPlug).
    /// </para>
    ///
    /// This method handles exceptions, marks the thread as exited, and halts. The scheduler will
    /// never re-pick a halted thread; the halt loop is a safety net in case
    /// the exit path ever races with a context switch.
    /// </summary>
    /// <param name="parameter">Generic parameter of the Thread Start, it is decoded based on the <see cref="ThreadFlags"/> set in the thread.</param>
    public static void InvokeCurrentThreadStart(IntPtr parameter)
    {
        PerCpuState? cpuState = GetCpuState(GetCurrentCpuId());
        Thread? currentThread = cpuState?.CurrentThread;

        if (currentThread == null)
        {
            Panic.Halt("No current thread in InvokeCurrentThreadStart");
        }

        uint threadId = currentThread.Id;
        Serial.WriteString("[SCHED] Running thread ");
        Serial.WriteNumber(threadId);
        Serial.WriteString("\n");

        int exitCode = 0;
        if (parameter != IntPtr.Zero)
        {
            try
            {
                Serial.WriteString("[SCHED] Invoking thread entry\n");

                // Evaluate flags, if ThreadFlags.Managed is set then this thread comes from a managed thread,
                // if not then we assume it's a gc handle holding a delegate.
                if ((currentThread.Flags & ThreadFlags.Managed) != 0)
                {
                    StartThread(null!, parameter);
                }
                else
                {
                    var handle = GCHandle<Action>.FromIntPtr(parameter);
                    Action start = handle.Target;
                    handle.Dispose();
                    start();
                }
                Serial.WriteString("[SCHED] Thread entry completed\n");
            }
            catch (Exception ex)
            {
                exitCode = 1;
                // Re-query thread ID — locals may be clobbered across the catch funclet.
                PerCpuState? exCpuState = GetCpuState(GetCurrentCpuId());
                uint exThreadId = exCpuState?.CurrentThread?.Id ?? 0;
                Serial.WriteString("[SCHED] Thread ");
                Serial.WriteNumber(exThreadId);
                Serial.WriteString(" threw exception: ");
                Serial.WriteString(ex.Message ?? "Unknown error");
                Serial.WriteString("\n");
            }
        }
        else
        {
            Serial.WriteString("[SCHED] No entry delegate on thread ");
            Serial.WriteNumber(threadId);
            Serial.WriteString("\n");
        }

        // Re-query current thread for exit — locals may be corrupted after the catch funclet.
        PerCpuState? exitCpuState = GetCpuState(GetCurrentCpuId());
        Thread? exitThread = exitCpuState?.CurrentThread;
        uint exitThreadId = exitThread?.Id ?? 0;

        Serial.WriteString("[SCHED] Thread ");
        Serial.WriteNumber(exitThreadId);
        Serial.WriteString(" exiting with code ");
        Serial.WriteNumber((uint)exitCode);
        Serial.WriteString("\n");

        if (exitThread != null)
        {
            ExitThread(GetCurrentCpuId(), exitThread);
        }

        // Halt forever — scheduler should not pick this thread again.
        while (true)
        {
            InternalCpu.Halt();
        }
    }

    // ========== Thread Registry (for GC stack scanning) ==========

    /// <summary>
    /// Returns the thread registry array. Safe to call from GC (no allocations).
    /// </summary>
    public static Thread?[]? Threads => _allThreads;

    /// <summary>
    /// Returns the number of registered threads. Safe to call from GC.
    /// </summary>
    public static int ThreadCount => _allThreadCount;

    /// <summary>
    /// Returns the CPU ID currently executing this code path. Single-CPU today.
    /// TODO(SMP): replace with x86_64 GS-relative per-CPU storage or ARM64 MPIDR_EL1
    /// affinity read once application processors are brought online.
    /// </summary>
    public static uint GetCurrentCpuId() => 0;

    /// <summary>
    /// Sum of TotalRuntime across all non-idle threads, in nanoseconds.
    /// One timer tick is charged to exactly one current thread per CPU, so this sum
    /// over a wall-clock window equals total busy CPU time for that window.
    /// Lock-free: registry slots are atomic and ulong reads are atomic on x64/ARM64;
    /// worst case is observing a stale value from an in-progress tick.
    /// </summary>
    public static ulong GetBusyCpuTimeNs()
    {
        Thread?[]? threads = _allThreads;
        if (threads == null)
        {
            return 0;
        }

        ulong sum = _exitedNonIdleRuntimeNs;
        for (int i = 0; i < threads.Length; i++)
        {
            Thread? t = threads[i];
            if (t == null)
            {
                continue;
            }
            if ((t.Flags & ThreadFlags.IdleThread) != 0)
            {
                continue;
            }
            sum += t.TotalRuntime;
        }
        return sum;
    }

    public static nint OnThreadExitCallback
    {
        get;
        internal set
        {
            Serial.WriteString("[SCHED] Setting thread exit callback: ");
            Serial.WriteHexWithPrefix((ulong)value);
            Serial.WriteString("\n");
            field = value;
        }
    }

    /// <summary>
    /// Registers a thread in the global registry. Called during thread creation.
    /// </summary>
    public static void RegisterThread(Thread thread)
    {
        if (_allThreads == null)
        {
            return;
        }

        for (int i = 0; i < _allThreads.Length; i++)
        {
            if (_allThreads[i] == null)
            {
                _allThreads[i] = thread;
                _allThreadCount++;
                return;
            }
        }

        Serial.WriteString("[SCHED] WARNING: Thread registry full, cannot register thread ");
        Serial.WriteNumber(thread.Id);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Unregisters a thread from the global registry. Called during thread exit.
    /// </summary>
    public static void UnregisterThread(Thread thread)
    {
        if (_allThreads == null)
        {
            return;
        }

        for (int i = 0; i < _allThreads.Length; i++)
        {
            if (_allThreads[i] == thread)
            {
                if ((thread.Flags & ThreadFlags.IdleThread) == 0)
                {
                    _exitedNonIdleRuntimeNs += thread.TotalRuntime;
                }
                _allThreads[i] = null;
                _allThreadCount--;
                return;
            }
        }
    }

    // ========== Thread Operations ==========

    public static void CreateThread(uint cpuId, Thread thread)
    {
        ThrowIfDisabled();

        Serial.WriteString("[SCHED] CreateThread: entering\n");
        RegisterThread(thread);
        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            var state = _cpuStates[cpuId];
            _currentScheduler.OnThreadCreate(state, thread);
        }
        Serial.WriteString("[SCHED] CreateThread: done\n");
    }

    public static void ReadyThread(uint cpuId, Thread thread)
    {
        ThrowIfDisabled();

        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            var state = _cpuStates[cpuId];

            // Only set to Ready if not a new thread (Created).
            // New threads stay Created until they actually start running.
            // This allows ScheduleFromInterrupt to detect first-time execution.
            if (thread.State != ThreadState.Created)
            {
                thread.State = ThreadState.Ready;
            }

            _currentScheduler.OnThreadReady(state, thread);

            Serial.WriteString("[SCHED] Thread ");
            Serial.WriteNumber(thread.Id);
            Serial.WriteString(" is now ready, RSP=");
            Serial.WriteHexWithPrefix((ulong)thread.StackPointer);
            Serial.WriteString("\n");
        }
    }

    public static void BlockThread(uint cpuId, Thread thread)
    {
        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = _cpuStates[cpuId];

            thread.State = ThreadState.Blocked;
            _currentScheduler.OnThreadBlocked(state, thread);
        }
    }

    public static void ExitThread(uint cpuId, Thread thread)
    {
        // Is Hightly likely that the running thread have adquire some state on it's managed counter part (even if it wasn't started from a managed thread).
        // Here we call the OnThreadExit Callback for the managed thread so it may be cleaned.
        nint managedCallback = OnThreadExitCallback;
        if (managedCallback != IntPtr.Zero)
        {
            Serial.WriteString("[ThreadPlug] Invoking managed thread exit callback for thread ");
            Serial.WriteNumber(thread.Id);
            Serial.WriteString("\n");
            unsafe
            {
                var callback = (delegate* unmanaged<void>)managedCallback;
                callback();
            }

        }

        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = _cpuStates[cpuId];

            // Return TLAB and track unused bytes before unregistering
            if (GarbageCollector.IsEnabled)
            {
                unsafe
                {
                    ulong unused = (ulong)(thread.AllocContext.AllocLimit - thread.AllocContext.AllocPtr);
                    GarbageCollector.AddDeadThreadNonAllocBytes(unused);
                    GarbageCollector.ReturnAllocContext(ref thread.AllocContext);
                }
            }

            thread.State = ThreadState.Dead;
            _currentScheduler.OnThreadExit(state, thread);
            UnregisterThread(thread);
            Serial.WriteString("[SCHED] ExitThread: OnThreadExit done\n");
        }
    }

    public static void YieldThread(uint cpuId, Thread thread)
    {
        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = _cpuStates[cpuId];

            _currentScheduler.OnThreadYield(state, thread);
        }
    }

    /// <summary>
    /// Puts a thread to sleep with a timeout.
    /// The thread may be woken up either by the timeout expires or when signaled.
    /// </summary>
    /// <param name="cpuId">CPU ID of the thread.</param>
    /// <param name="thread">Thread to sleep.</param>
    /// <param name="timeoutMs">Timeout in milliseconds. 0 means indefinite sleep (until signaled).</param>
    public static void Sleep(uint cpuId, Thread thread, uint timeoutMs)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            PerCpuState cpuState = _cpuStates[cpuId];

            ulong timestamp = GetTimestamp();
            ulong num = timeoutMs * 1_000_000UL;
            thread.WakeupTime = timestamp + num;

            _currentScheduler.OnThreadBlocked(cpuState, thread);
            thread.State = ThreadState.Sleeping;
        }

        InternalCpu.Halt();
    }

    /// <summary>
    /// Puts the current thread to sleep with a timeout.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds. 0 means indefinite sleep.</param>
    public static void Sleep(uint timeoutMs)
    {
        Thread? currentThread = GetCpuState(GetCurrentCpuId()).CurrentThread;
        if (currentThread != null)
        {
            Sleep(currentThread.CpuId, currentThread, timeoutMs);
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
        {
            return;
        }

        if (cpuId >= _cpuCount)
        {
            return;
        }

        var state = _cpuStates[cpuId];
        if (state == null || state.CurrentThread == null)
        {
            return;
        }

        // Check and wake up sleeping threads whose timeout has expired
        CheckSleepingThreads(elapsedNs);

        // Update timing and check if preemption needed
        bool needsReschedule = _currentScheduler.OnTick(state, state.CurrentThread, elapsedNs);

        if (needsReschedule)
        {
            ScheduleFromInterrupt(cpuId, currentRsp);
        }
    }

    /// <summary>
    /// Checks all sleeping threads and wakes those whose wakeup time has expired.
    /// Called from timer interrupt handler to implement timed waits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckSleepingThreads(ulong elapsedNs)
    {
        if (_allThreads == null)
        {
            return;
        }

        ulong currentTime = GetTimestamp();

        for (int i = 0; i < _allThreads.Length; i++)
        {
            Thread? thread = _allThreads[i];
            if (thread == null || thread.State != ThreadState.Sleeping)
            {
                continue;
            }

            // Check if wakeup time has been reached
            if (currentTime >= thread.WakeupTime)
            {
                Serial.WriteString("[SCHED] Waking sleeping thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" (time expired)\n");

                // Wake the thread by marking it as ready
                thread.WakeupTime = 0;
                ReadyThread(thread.CpuId, thread);
            }
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

        // No lock needed - interrupts are already disabled in interrupt context
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
                {
                    prev.State = ThreadState.Ready;
                }

                // Put previous thread back in run queue if still runnable
                if (prev.State == ThreadState.Ready)
                {
                    _currentScheduler.OnThreadYield(state, prev);
                }
            }

            // Switch to next thread
            state.CurrentThread = next;

            // Check if this is a NEW thread (never run before) or RESUMED
            bool isNewThread = next.State == ThreadState.Created;

            next.State = ThreadState.Running;
            next.LastScheduledAt = GetTimestamp();

            // Request context switch - set new thread flag and target RSP
            ContextSwitchNative.SetContextSwitchNewThread(isNewThread ? 1 : 0);
            ContextSwitchNative.SetContextSwitchSp(next.StackPointer);
        }
    }

    // ========== Platform-specific ==========

    private static void DoContextSwitch(Thread prev, Thread next)
    {
        // This is for non-interrupt context switches (e.g., voluntary yield)
        // Not fully implemented - use ScheduleFromInterrupt for preemptive switching
        if (next == null)
        {
            return;
        }

        if (prev != null)
        {
            prev.State = ThreadState.Ready;
        }

        next.State = ThreadState.Running;
        ContextSwitchNative.SetContextSwitchSp(next.StackPointer);
    }

    private static ulong GetTimestamp()
    {
        return (ulong)Stopwatch.GetTimestamp();
    }
}
