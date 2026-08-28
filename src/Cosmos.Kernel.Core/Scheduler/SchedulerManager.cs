using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
[Experimental(Experimentals.SchedulerSeamDiagId)]
public static class SchedulerManager
{

    private static IScheduler? s_currentScheduler;
    private static PerCpuState[]? s_cpuStates;
    private static uint s_cpuCount;
    private static SpinLock s_globalLock;
    private static bool s_enabled;
    private static uint s_nextThreadId;

    // Global thread registry: tracks ALL live threads across all states
    // (Running, Ready, Blocked, Sleeping). Used by GC to scan all thread stacks.
    // Allocated once at init to avoid heap allocations during GC.
    private static Thread?[]? s_allThreads;
    private static int s_allThreadCount;

    // Cumulative TotalRuntime of exited non-idle threads. Live-thread runtime
    // disappears from s_allThreads on UnregisterThread, so we move it here to
    // keep GetBusyCpuTimeNs monotonic across thread lifecycle.
    private static ulong s_exitedNonIdleRuntimeNs;

    /// <summary>
    /// Default time slice in nanoseconds (10ms).
    /// </summary>
    public const ulong DefaultQuantumNs = 10_000_000;

    /// <summary>
    /// Nanoseconds per millisecond, used to convert sleep timeouts to timestamp units.
    /// Public so other kernel components (e.g. timer drivers) can share the unit conversion.
    /// </summary>
    public const ulong NanosecondsPerMillisecond = 1_000_000UL;

    /// <summary>Timer ticks between debug-live snapshot refreshes (~100ms at 100Hz).</summary>
    private const uint SnapshotRefreshTickInterval = 10;

    /// <summary>Number of initial timer ticks that are always logged to serial.</summary>
    private const uint InitialTickLogCount = 10;

    /// <summary>After the initial ticks, log every Nth timer tick to avoid flooding serial output.</summary>
    private const uint TickLogInterval = 50;

    /// <summary>
    /// Whether scheduler support is compiled into this kernel
    /// (the <c>CosmosEnableScheduler</c> feature switch). Internal: the ring
    /// already publishes this fact as <c>KernelFeatures.Scheduler</c> and
    /// <c>SchedulerInfo.IsSupported</c>, so a policy author reads it there.
    /// </summary>
    internal static bool IsEnabled => CosmosFeatures.SchedulerEnabled;

    /// <summary>
    /// Whether the boot path has built the per-CPU state, which is what
    /// makes the rest of this class usable. Blocking primitives (and
    /// drivers built on them) must check this before touching
    /// <see cref="GetCpuState"/>: with the scheduler feature compiled out,
    /// or before its library initializer runs, there is exactly one
    /// execution context, so callers fall back to spin/polled paths
    /// instead of blocking.
    /// </summary>
    public static bool IsReady => IsEnabled && s_cpuStates != null;

    private static void ThrowIfDisabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Scheduler support is disabled. Set CosmosEnableScheduler=true in your csproj to enable it.");
        }
    }

    // ========== Initialization ==========

    internal static void Initialize(uint cpuCount)
    {
        ThrowIfDisabled();

        s_cpuCount = cpuCount;
        s_cpuStates = new PerCpuState[cpuCount];

        for (uint i = 0; i < cpuCount; i++)
        {
            s_cpuStates[i] = new PerCpuState { CpuId = i };
        }

        // Pre-allocate thread registry
        s_allThreads = new Thread?[Thread.MaxThreadCount];
        s_allThreadCount = 0;

        Cosmos.Kernel.Core.Runtime.DebugLiveSnapshot.Initialize();
        Cosmos.Kernel.Core.Runtime.DebugLiveGCSnapshot.Initialize();
        Cosmos.Kernel.Core.Runtime.DebugLiveMemorySnapshot.Initialize();
    }

    /// <summary>
    /// Installs a scheduling policy. The previous scheduler, if any, is
    /// shut down on every CPU (<see cref="IScheduler.ShutdownCpu"/>) before
    /// the new one is initialized (<see cref="IScheduler.InitializeCpu"/>).
    /// </summary>
    /// <param name="scheduler">Scheduler to install.</param>
    public static void SetScheduler(IScheduler scheduler)
    {
        ThrowIfCpuStateNotInitialized();

        s_globalLock.Acquire();
        try
        {
            if (s_currentScheduler != null)
            {
                for (uint i = 0; i < s_cpuCount; i++)
                {
                    s_currentScheduler.ShutdownCpu(s_cpuStates[i]);
                }
            }

            s_currentScheduler = scheduler;

            for (uint i = 0; i < s_cpuCount; i++)
            {
                scheduler.InitializeCpu(s_cpuStates[i]);
            }
        }
        finally
        {
            s_globalLock.Release();
        }
    }

    // ========== Accessors ==========

    /// <summary>
    /// The installed scheduler, or <see langword="null"/> before
    /// <see cref="SetScheduler"/> has run.
    /// </summary>
    public static IScheduler? Current => s_currentScheduler;

    /// <summary>
    /// Number of CPUs the scheduler manages.
    /// </summary>
    internal static uint CpuCount => s_cpuCount;

    /// <summary>
    /// Returns the scheduling state of a CPU, or <see langword="null"/>
    /// when <see cref="IsReady"/> is false.
    /// </summary>
    /// <param name="cpuId">
    /// CPU to look up. The count is on the ring as
    /// <c>SchedulerInfo.CpuCount</c>; a policy normally takes the state it
    /// needs from its hook parameters instead.
    /// </param>
    /// <exception cref="IndexOutOfRangeException">
    /// <paramref name="cpuId"/> is not a managed CPU and the scheduler is
    /// ready. Before it is ready this returns null for any value.
    /// </exception>
    public static PerCpuState? GetCpuState(uint cpuId) => s_cpuStates?[cpuId];

    /// <summary>
    /// Returns the per-CPU state array, or <see langword="null"/> before
    /// initialization.
    /// </summary>
    internal static PerCpuState[]? GetAllCpuStates() => s_cpuStates;

    /// <summary>
    /// Sets up the idle thread for a CPU. Should only be called during initialization.
    /// </summary>
    internal static void SetupIdleThread(uint cpuId, Thread idleThread)
    {
        ThrowIfCpuStateNotInitialized();

        var state = s_cpuStates[cpuId];
        state.IdleThread = idleThread;
        state.CurrentThread = idleThread;
        RegisterThread(idleThread);
    }

    /// <summary>
    /// Whether the scheduler is processing timer ticks and preempting
    /// threads. The boot path arms it once the manager, the policy and the
    /// idle threads are all wired, so the first tick cannot race a
    /// half-built scheduler. Surfaced on the ring as
    /// <c>SchedulerInfo.IsRunning</c>.
    /// </summary>
    internal static bool IsRunning
    {
        get => s_enabled;
        set => s_enabled = value;
    }

    /// <summary>
    /// Allocates a new unique thread ID.
    /// </summary>
    internal static uint AllocateThreadId() => s_nextThreadId++;

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
    internal static void InvokeCurrentThreadStart(IntPtr parameter)
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
                Serial.WriteString(ex.Message);
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
    internal static Thread?[]? Threads => s_allThreads;

    /// <summary>
    /// Returns the number of registered threads. Safe to call from GC.
    /// </summary>
    internal static int ThreadCount => s_allThreadCount;

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
    internal static ulong GetBusyCpuTimeNs()
    {
        Thread?[]? threads = s_allThreads;
        if (threads == null)
        {
            return 0;
        }

        ulong sum = s_exitedNonIdleRuntimeNs;
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

    internal static nint OnThreadExitCallback
    {
        get;
        set
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
    internal static void RegisterThread(Thread thread)
    {
        if (s_allThreads == null)
        {
            return;
        }

        // Idempotent: idle-thread setup goes through both CreateThread and
        // SetupIdleThread, both of which call here. Avoid duplicate slots.
        for (int i = 0; i < s_allThreads.Length; i++)
        {
            if (s_allThreads[i] == thread)
            {
                return;
            }
        }

        for (int i = 0; i < s_allThreads.Length; i++)
        {
            if (s_allThreads[i] == null)
            {
                s_allThreads[i] = thread;
                s_allThreadCount++;
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
    internal static void UnregisterThread(Thread thread)
    {
        if (s_allThreads == null)
        {
            return;
        }

        for (int i = 0; i < s_allThreads.Length; i++)
        {
            if (s_allThreads[i] == thread)
            {
                if ((thread.Flags & ThreadFlags.IdleThread) == 0)
                {
                    s_exitedNonIdleRuntimeNs += thread.TotalRuntime;
                }
                s_allThreads[i] = null;
                s_allThreadCount--;
                return;
            }
        }
    }

    [MemberNotNull(nameof(s_cpuStates))]
    private static void ThrowIfCpuStateNotInitialized()
    {
        if (s_cpuStates is null)
        {
            throw new Exception($"{nameof(SchedulerManager)} not initialized");
        }
    }

    [MemberNotNull(nameof(s_currentScheduler))]
    private static void ThrowIfSchedulerNotSet()
    {
        if (s_currentScheduler is null)
        {
            throw new Exception($"{nameof(SchedulerManager)}{nameof(s_currentScheduler)} not initialized");
        }
    }


    // ========== Thread Operations ==========

    internal static void CreateThread(uint cpuId, Thread thread)
    {
        ThrowIfDisabled();
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        Serial.WriteString("[SCHED] CreateThread: entering\n");
        RegisterThread(thread);
        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            var state = s_cpuStates[cpuId];
            s_currentScheduler.OnThreadCreate(state, thread);
        }
        Serial.WriteString("[SCHED] CreateThread: done\n");
    }

    internal static void ReadyThread(uint cpuId, Thread thread)
    {
        ThrowIfDisabled();
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            var state = s_cpuStates[cpuId];

            // Only set to Ready if not a new thread (Created).
            // New threads stay Created until they actually start running.
            // This allows ScheduleFromInterrupt to detect first-time execution.
            if (thread.State != ThreadState.Created)
            {
                thread.State = ThreadState.Ready;
            }

            s_currentScheduler.OnThreadReady(state, thread);

            // Ask the next hardware-IRQ exit to reschedule: when this wake
            // comes from an ISR (InterruptEvent.Signal), the woken thread
            // would otherwise sit in the run queue until the next timer tick.
            state._needReschedule = true;

            Serial.WriteString("[SCHED] Thread ");
            Serial.WriteNumber(thread.Id);
            Serial.WriteString(" is now ready, RSP=");
            Serial.WriteHexWithPrefix((ulong)thread.StackPointer);
            Serial.WriteString("\n");
        }
    }

    internal static void BlockThread(uint cpuId, Thread thread)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = s_cpuStates[cpuId];

            thread.State = ThreadState.Blocked;
            s_currentScheduler.OnThreadBlocked(state, thread);

            // Ask the next IRQ exit to switch away (same as ReadyThread): a
            // blocked current thread otherwise keeps re-entering its halt
            // loop until the quantum tick preempts it — or forever when the
            // periodic tick is not running.
            state._needReschedule = true;

            Serial.WriteString("[SCHED] BlockThread id=");
            Serial.WriteNumber(thread.Id);
            Serial.WriteString("\n");
        }
    }

    internal static void ExitThread(uint cpuId, Thread thread)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        // Is Highly likely that the running thread have acquired some state on it's managed counter part (even if it wasn't started from a managed thread).
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
            Serial.WriteString("[SCHED] ExitThread: callback returned for thread ");
            Serial.WriteNumber(thread.Id);
            Serial.WriteString("\n");
        }

        Serial.WriteString("[SCHED] ExitThread: entering DisableInterruptsScope for thread ");
        Serial.WriteNumber(thread.Id);
        Serial.WriteString("\n");

        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = s_cpuStates[cpuId];

            // Return TLAB and track unused bytes before unregistering
            if (GarbageCollector.IsEnabled)
            {
                unsafe
                {
                    ulong unused = (ulong)(thread._allocContext.AllocLimit - thread._allocContext.AllocPtr);
                    GarbageCollector.AddDeadThreadNonAllocBytes(unused);
                    GarbageCollector.ReturnAllocContext(ref thread._allocContext);
                }
            }

            thread.State = ThreadState.Dead;
            s_currentScheduler.OnThreadExit(state, thread);
            UnregisterThread(thread);
            Serial.WriteString("[SCHED] ExitThread: OnThreadExit done\n");
        }
    }

    internal static void YieldThread(uint cpuId, Thread thread)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        using (CPU.InternalCpu.DisableInterruptsScope())
        {
            PerCpuState state = s_cpuStates[cpuId];

            s_currentScheduler.OnThreadYield(state, thread);
        }
    }

    /// <summary>
    /// Puts a thread to sleep with a timeout.
    /// The thread may be woken up either by the timeout expires or when signaled.
    /// </summary>
    /// <param name="cpuId">CPU ID of the thread.</param>
    /// <param name="thread">Thread to sleep.</param>
    /// <param name="timeoutMs">Timeout in milliseconds. 0 means indefinite sleep (until signaled).</param>
    internal static void Sleep(uint cpuId, Thread thread, uint timeoutMs)
    {
        MarkSleeping(cpuId, thread, timeoutMs);

        // Only park the CPU while still Sleeping: if a wake already landed between
        // scope-dispose and this point, halting would sleep past it.
        if (thread.State == ThreadState.Sleeping)
        {
            InternalCpu.Halt();
        }
    }

    /// <summary>
    /// Marks a thread Sleeping with a wake deadline without halting — for callers that must
    /// make the state change atomic with their own IRQ-off section (ConditionVariable.WaitTimeout)
    /// and park afterwards under a state guard.
    /// </summary>
    /// <param name="cpuId">CPU ID of the thread.</param>
    /// <param name="thread">Thread to sleep.</param>
    /// <param name="timeoutMs">Timeout in milliseconds. 0 means indefinite sleep (until signaled).</param>
    internal static void MarkSleeping(uint cpuId, Thread thread, uint timeoutMs)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        using (InternalCpu.DisableInterruptsScope())
        {
            PerCpuState cpuState = s_cpuStates[cpuId];

            ulong timestamp = GetTimestamp();
            // WakeupTime is compared against GetTimestamp() — Stopwatch ticks —
            // so the offset must be in ticks too. Adding nanoseconds stretched
            // timeouts 16x on ARM64 (62.5 MHz generic timer) and shrank them
            // on multi-GHz x64 TSCs.
            ulong ticksPerMs = (ulong)Stopwatch.Frequency / 1000;
            thread.WakeupTime = timestamp + timeoutMs * ticksPerMs;

            s_currentScheduler.OnThreadBlocked(cpuState, thread);
            thread.State = ThreadState.Sleeping;
        }
    }

    /// <summary>
    /// Puts the current thread to sleep with a timeout.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds. 0 means indefinite sleep.</param>
    internal static void Sleep(uint timeoutMs)
    {
        Thread? currentThread = GetCpuState(GetCurrentCpuId())?.CurrentThread;
        if (currentThread != null)
        {
            Sleep(currentThread.CpuId, currentThread, timeoutMs);
        }
    }

    // ========== Scheduling ==========

    internal static bool OnTick(uint cpuId, ulong elapsedNs)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        var state = s_cpuStates[cpuId];
        return s_currentScheduler.OnTick(state, state.CurrentThread, elapsedNs);
    }

    internal static void Schedule(uint cpuId)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        var state = s_cpuStates[cpuId];
        state._lock.Acquire();

        var prev = state.CurrentThread;
        var next = s_currentScheduler.PickNext(state) ?? state.IdleThread;

        if (next == null)
        {
            state._lock.Release();
            return;
        }

        if (next != prev)
        {
            state.CurrentThread = next;
            next.State = ThreadState.Running;
            next.LastScheduledAt = GetTimestamp();

            state._lock.Release();
            DoContextSwitch(prev, next);
        }
        else
        {
            state._lock.Release();
        }
    }

    /// <summary>
    /// Changes a thread's priority through the installed scheduler
    /// (<see cref="IScheduler.SetPriority"/>). Interpretation is
    /// scheduler-specific.
    /// </summary>
    /// <param name="cpuId">CPU whose state guards the update.</param>
    /// <param name="thread">Thread to reprioritize.</param>
    /// <param name="priority">New priority value.</param>
    public static void SetPriority(uint cpuId, Thread thread, long priority)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        var state = s_cpuStates[cpuId];
        state._lock.Acquire();
        try
        {
            s_currentScheduler.SetPriority(state, thread, priority);
        }
        finally
        {
            state._lock.Release();
        }
    }

    /// <summary>
    /// Returns a thread's priority as reported by the installed scheduler
    /// (<see cref="IScheduler.GetPriority"/>).
    /// </summary>
    /// <param name="thread">Thread to query.</param>
    public static long GetPriority(Thread thread)
    {
        ThrowIfSchedulerNotSet();

        return s_currentScheduler.GetPriority(thread);
    }

    // ========== Load Balancing ==========

    /// <summary>
    /// Asks the installed scheduler to pick the best CPU for a new or
    /// migrating thread (<see cref="IScheduler.SelectCpu"/>).
    /// </summary>
    /// <param name="thread">Thread being placed.</param>
    /// <param name="currentCpu">CPU the thread is currently on.</param>
    internal static uint SelectCpu(Thread thread, uint currentCpu)
    {
        ThrowIfSchedulerNotSet();

        return s_currentScheduler.SelectCpu(thread, currentCpu, s_cpuCount);
    }

    /// <summary>
    /// Masks maskable interrupts on the current CPU until the returned
    /// scope is disposed. Schedulers use this around entry points the
    /// manager does not already guard: their run-queue diagnostics hooks,
    /// <see cref="IScheduler.InitializeCpu"/>, <see cref="IScheduler.ShutdownCpu"/>
    /// and <see cref="IScheduler.SetPriority"/> (all three hold a spinlock
    /// only), and any tuning setters the policy exposes. The tick hooks and
    /// the thread-lifecycle hooks are already called with interrupts masked.
    /// </summary>
    public static InterruptMaskScope MaskInterrupts() => new(InternalCpu.DisableInterruptsScope());

    /// <summary>
    /// Gives the installed scheduler a load-balancing opportunity for one
    /// CPU (<see cref="IScheduler.Balance"/>).
    /// </summary>
    /// <param name="cpuId">CPU to balance.</param>
    internal static void Balance(uint cpuId)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        var state = s_cpuStates[cpuId];
        s_currentScheduler.Balance(state, s_cpuStates);
    }

    // ========== Timer Interrupt Handling ==========

    // Debug counter to avoid flooding serial output
    private static uint s_tickCount;

    /// <summary>
    /// Called from timer interrupt handler to process scheduling.
    /// This is the main entry point for preemptive scheduling.
    /// </summary>
    /// <param name="cpuId">Current CPU ID.</param>
    /// <param name="currentRsp">Current RSP from IRQ context (pointer to saved context).</param>
    /// <param name="elapsedNs">Nanoseconds since last tick.</param>
    internal static void OnTimerInterrupt(uint cpuId, nuint currentRsp, ulong elapsedNs)
    {
        s_tickCount++;

        // Refresh the debug-live snapshot every 10 ticks (~100ms at 100Hz)
        // so the host-side QMP poller sees fresh thread state without
        // pausing the kernel.
        if ((s_tickCount % SnapshotRefreshTickInterval) == 0)
        {
            Cosmos.Kernel.Core.Runtime.DebugLiveSnapshot.Update();
            Cosmos.Kernel.Core.Runtime.DebugLiveGCSnapshot.Update();
            Cosmos.Kernel.Core.Runtime.DebugLiveMemorySnapshot.Update();
        }

        // Log first 10 ticks and then every 50 ticks
        if (s_tickCount <= InitialTickLogCount || s_tickCount % TickLogInterval == 0)
        {
            Serial.WriteString("[SCHED] Tick ");
            Serial.WriteNumber(s_tickCount);
            Serial.WriteString(" enabled=");
            Serial.WriteString(s_enabled ? "1" : "0");
            Serial.WriteString("\n");
        }

        if (!s_enabled || s_currentScheduler == null || s_cpuStates == null)
        {
            return;
        }

        if (cpuId >= s_cpuCount)
        {
            return;
        }

        var state = s_cpuStates[cpuId];
        if (state.CurrentThread == null)
        {
            return;
        }

        // Check and wake up sleeping threads whose timeout has expired
        CheckSleepingThreads(elapsedNs);

        // Update timing and check if preemption needed
        bool needsReschedule = s_currentScheduler.OnTick(state, state.CurrentThread, elapsedNs);

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
        if (s_allThreads == null)
        {
            return;
        }

        ulong currentTime = GetTimestamp();

        for (int i = 0; i < s_allThreads.Length; i++)
        {
            Thread? thread = s_allThreads[i];
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
    /// Runs a pending reschedule request on hardware-IRQ exit. ReadyThread
    /// sets the request when it wakes a thread (typically an ISR-side
    /// <see cref="InterruptEvent.Signal"/>); device-IRQ exit doesn't
    /// otherwise reschedule — only the timer tick does — so a woken waiter
    /// would sit in the run queue for up to a full quantum. No-op when the
    /// timer path already staged a context switch for this interrupt: a
    /// second ScheduleFromInterrupt would save this frame's stack pointer
    /// into a thread whose real context lives elsewhere.
    /// </summary>
    /// <param name="cpuId">Current CPU ID.</param>
    /// <param name="currentRsp">Current RSP (pointer to saved context on stack).</param>
    internal static void ReschedulePendingFromIrq(uint cpuId, nuint currentRsp)
    {
        if (!s_enabled || s_currentScheduler == null || s_cpuStates == null || cpuId >= s_cpuCount)
        {
            return;
        }

        PerCpuState state = s_cpuStates[cpuId];
        if (!state._needReschedule)
        {
            return;
        }

        state._needReschedule = false;

        if (ContextSwitchNative.GetContextSwitchSp() != 0)
        {
            return;
        }

        ScheduleFromInterrupt(cpuId, currentRsp);
    }

    /// <summary>
    /// Performs scheduling from within an interrupt context.
    /// Picks next thread and sets up context switch if needed.
    /// </summary>
    /// <param name="cpuId">Current CPU ID.</param>
    /// <param name="currentRsp">Current RSP (pointer to saved context on stack).</param>
    internal static void ScheduleFromInterrupt(uint cpuId, nuint currentRsp)
    {
        ThrowIfCpuStateNotInitialized();
        ThrowIfSchedulerNotSet();

        var state = s_cpuStates[cpuId];

        // No lock needed - interrupts are already disabled in interrupt context
        var prev = state.CurrentThread;
        var next = s_currentScheduler.PickNext(state) ?? state.IdleThread;

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
                    s_currentScheduler.OnThreadYield(state, prev);
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

    private static void DoContextSwitch(Thread? prev, Thread? next)
    {
        // This is for non-interrupt context switches (e.g., voluntary yield)
        // Not fully implemented - use ScheduleFromInterrupt for preemptive switching
        if (next == null)
        {
            return;
        }

        prev?.State = ThreadState.Ready;

        next.State = ThreadState.Running;
        ContextSwitchNative.SetContextSwitchSp(next.StackPointer);
    }

    private static ulong GetTimestamp()
    {
        return (ulong)Stopwatch.GetTimestamp();
    }
}
