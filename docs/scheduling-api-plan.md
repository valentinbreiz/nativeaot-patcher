# Scheduling API Design Plan

## Overview

This document outlines the design for a **fully generic pluggable scheduling API** inspired by the Ekiben framework. The API is scheduler-agnostic - specific algorithms like stride scheduling extend the base structures with their own data using **C# classes and object references** (no unsafe pointers in the public API).

## References

- [Ekiben Paper](https://arxiv.org/pdf/2306.15076) - Linux scheduler framework with clean trait-based API
- [Stride Scheduling Paper](https://web.eecs.umich.edu/~mosharaf/Readings/Stride.pdf) - Deterministic proportional-share scheduling
- GitHub Issue #212 - EPIC: Implement per-core multithreading

---

## Part 1: Core Data Structures (Generic)

All core structures are **classes** (reference types) for clean C# semantics.

### 1.1 Enums

```csharp
public enum ThreadState : byte
{
    Created,        // Just created, not yet scheduled
    Ready,          // Can be scheduled
    Running,        // Currently executing on a CPU
    Blocked,        // Waiting for I/O, lock, etc.
    Sleeping,       // Timed wait
    Dead            // Terminated, awaiting cleanup
}

[Flags]
public enum ThreadFlags : ushort
{
    None = 0,
    KernelThread = 1 << 0,      // Kernel-mode thread
    IdleThread = 1 << 1,        // Per-CPU idle thread
    Pinned = 1 << 2,            // Cannot migrate to other CPUs
    // Bits 8-15 reserved for scheduler-specific flags
}
```

### 1.2 SchedulerExtensible Base Class

Base class providing the extension point for scheduler-specific data:

```csharp
/// <summary>
/// Base class for objects that can hold scheduler-specific extension data.
/// </summary>
public abstract class SchedulerExtensible
{
    /// <summary>
    /// Scheduler-specific data. Each scheduler defines its own class
    /// and stores an instance here.
    /// </summary>
    public object SchedulerData { get; set; }

    /// <summary>
    /// Type-safe accessor for extension data.
    /// </summary>
    public T GetSchedulerData<T>() where T : class => (T)SchedulerData;
}
```

### 1.3 Thread Class

```csharp
/// <summary>
/// Thread Control Block - generic base for all schedulers.
/// </summary>
public class Thread : SchedulerExtensible
{
    // ===== Identity =====
    public uint Id { get; set; }
    public uint CpuId { get; set; }              // Current/preferred CPU

    // ===== State =====
    public ThreadState State { get; set; }
    public ThreadFlags Flags { get; set; }

    // ===== Context (architecture-specific values) =====
    public nuint StackPointer { get; set; }      // Saved RSP/SP
    public nuint InstructionPointer { get; set; }// Saved RIP/PC
    public nuint StackBase { get; set; }         // Bottom of stack allocation
    public nuint StackSize { get; set; }         // Size of stack

    // ===== Generic Timing (useful for any scheduler) =====
    public ulong CreatedAt { get; set; }         // Timestamp when created
    public ulong TotalRuntime { get; set; }      // Nanoseconds of CPU time used
    public ulong LastScheduledAt { get; set; }   // Timestamp of last schedule
    public ulong WakeupTime { get; set; }        // Target time for sleeping threads (0 = not sleeping)
}
```

### 1.4 PerCpuState Class

```csharp
/// <summary>
/// Per-CPU scheduling state - generic base for all schedulers.
/// </summary>
public class PerCpuState : SchedulerExtensible
{
    // ===== Identity =====
    public uint CpuId { get; set; }

    // ===== Current Execution =====
    public Thread CurrentThread { get; set; }    // Currently running thread
    public Thread IdleThread { get; set; }       // Per-CPU idle thread (always runnable)

    // ===== Timing =====
    public ulong LastTickAt { get; set; }        // Timestamp of last timer tick

    // ===== Synchronization =====
    public SpinLock Lock;                        // Per-CPU lock
}
```

### 1.5 Extension Classes for Schedulers

Each scheduler defines its own extension **classes**:

```csharp
// ============ Stride Scheduler Extensions ============

public class StrideThreadData
{
    public ulong Tickets { get; set; }       // Resource weight
    public ulong Stride { get; set; }        // stride1 / tickets
    public long Pass { get; set; }           // Virtual time position
    public long Remain { get; set; }         // Remaining stride on block

    // Interactive process detection
    public ulong LastWakeup { get; set; }    // For interactive detection
    public uint SleepCount { get; set; }     // I/O pattern tracking
    public bool IsInteractive { get; set; }  // Detected as interactive
    public bool IsBoosted { get; set; }      // Currently priority boosted
}

public class StrideCpuData
{
    public ulong TotalTickets { get; set; }  // Sum of tickets in run queue
    public ulong GlobalPass { get; set; }    // Global virtual time
    public ulong LastPassUpdate { get; set; }// Timestamp of last update

    // Run queue sorted by Pass value
    public List<Thread> RunQueue { get; } = new();
}

// ============ Round Robin Extensions ============

public class RoundRobinThreadData
{
    public ulong TimeSliceRemaining { get; set; }
    public uint Priority { get; set; }       // 0-31 priority levels
}

public class RoundRobinCpuData
{
    public Queue<Thread>[] PriorityQueues { get; }

    public RoundRobinCpuData()
    {
        PriorityQueues = new Queue<Thread>[32];
        for (int i = 0; i < 32; i++)
            PriorityQueues[i] = new Queue<Thread>();
    }
}

// ============ CFS Extensions ============

public class CFSThreadData
{
    public ulong VirtualRuntime { get; set; }
    public ulong Weight { get; set; }
}

public class CFSCpuData
{
    public ulong MinVRuntime { get; set; }
    // Threads sorted by vruntime
    public SortedList<ulong, Thread> Timeline { get; } = new();
}
```

---

## Part 2: IScheduler Interface (Fully Generic)

The interface contains **no scheduler-specific concepts** like tickets, stride, or priority - those are implementation details.

### 2.1 Core Interface

```csharp
/// <summary>
/// Interface for pluggable scheduling algorithms.
/// Completely generic - no algorithm-specific concepts.
/// Inspired by Ekiben's EkibenScheduler trait.
/// </summary>
public interface IScheduler
{
    // ========== Identity ==========

    /// <summary>
    /// Unique name for this scheduler (e.g., "Stride", "RoundRobin", "CFS").
    /// </summary>
    string Name { get; }

    // ========== Lifecycle ==========

    /// <summary>
    /// Initialize scheduler for a specific CPU.
    /// Create and assign PerCpuState.SchedulerData here.
    /// </summary>
    void InitializeCpu(PerCpuState cpuState);

    /// <summary>
    /// Cleanup when scheduler is being replaced or shutdown.
    /// </summary>
    void ShutdownCpu(PerCpuState cpuState);

    // ========== Thread Lifecycle ==========

    /// <summary>
    /// A new thread is being added to this CPU's management.
    /// Create and assign Thread.SchedulerData here.
    /// </summary>
    void OnThreadCreate(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread is ready to run (first time or after wakeup).
    /// Add to run queue.
    /// </summary>
    void OnThreadReady(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread has blocked (I/O, lock, sleep, etc.).
    /// Remove from run queue, save state for resume.
    /// </summary>
    void OnThreadBlocked(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread is terminating.
    /// Cleanup Thread.SchedulerData.
    /// </summary>
    void OnThreadExit(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread voluntarily yields remaining time slice.
    /// </summary>
    void OnThreadYield(PerCpuState cpuState, Thread thread);

    // ========== Scheduling Decisions ==========

    /// <summary>
    /// Pick the next thread to run.
    /// Returns null if no runnable threads (run idle).
    /// </summary>
    Thread PickNext(PerCpuState cpuState);

    /// <summary>
    /// Called when picked thread couldn't be scheduled (e.g., already migrated).
    /// </summary>
    void OnPickFailed(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Timer tick - update accounting, check for preemption.
    /// </summary>
    /// <param name="cpuState">CPU state</param>
    /// <param name="current">Currently running thread (may be null if idle)</param>
    /// <param name="elapsedNs">Nanoseconds since last tick</param>
    /// <returns>True if reschedule needed (preemption)</returns>
    bool OnTick(PerCpuState cpuState, Thread current, ulong elapsedNs);

    // ========== Load Balancing ==========

    /// <summary>
    /// Select best CPU for a new or migrating thread.
    /// </summary>
    uint SelectCpu(Thread thread, uint currentCpu, uint cpuCount);

    /// <summary>
    /// Thread is migrating between CPUs.
    /// Transfer/transform scheduler state as needed.
    /// </summary>
    void OnThreadMigrate(Thread thread, PerCpuState fromState, PerCpuState toState);

    /// <summary>
    /// Periodic load balancing opportunity.
    /// May steal work from other CPUs or push work away.
    /// </summary>
    void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates);

    // ========== Dynamic Reconfiguration ==========

    /// <summary>
    /// Thread's priority/weight is changing.
    /// Interpretation is scheduler-specific (tickets, nice value, etc.).
    /// </summary>
    void SetPriority(PerCpuState cpuState, Thread thread, long priority);

    /// <summary>
    /// Get thread's current priority/weight.
    /// </summary>
    long GetPriority(Thread thread);
}
```

### 2.2 Scheduler Manager (Generic)

```csharp
/// <summary>
/// Manages scheduler lifecycle and dispatches to current scheduler.
/// No scheduler-specific constants - those belong in implementations.
/// </summary>
public static class SchedulerManager
{
    private static IScheduler _currentScheduler;
    private static PerCpuState[] _cpuStates;
    private static uint _cpuCount;
    private static SpinLock _globalLock;

    // Generic constant only
    public const ulong DefaultQuantumNs = 10_000_000;  // 10ms default time slice

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
            // Shutdown old scheduler if any
            if (_currentScheduler != null)
            {
                for (uint i = 0; i < _cpuCount; i++)
                    _currentScheduler.ShutdownCpu(_cpuStates[i]);
            }

            _currentScheduler = scheduler;

            // Initialize new scheduler
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

    // ========== Scheduling Operations ==========

    public static void CreateThread(uint cpuId, Thread thread)
    {
        var state = _cpuStates[cpuId];
        state.Lock.Acquire();
        try { _currentScheduler.OnThreadCreate(state, thread); }
        finally { state.Lock.Release(); }
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
        finally { state.Lock.Release(); }
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
        finally { state.Lock.Release(); }
    }

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
            ContextSwitch(prev, next);  // Platform-specific assembly
        }
        else
        {
            state.Lock.Release();
        }
    }

    // Platform-specific (implemented separately)
    private static void ContextSwitch(Thread prev, Thread next) { /* assembly */ }
    private static ulong GetTimestamp() => 0; // rdtsc or similar
}
```

---

## Part 3: Stride Scheduler Implementation

Now the stride scheduler uses the generic API and manages its own extension data.

```csharp
public class StrideScheduler : IScheduler
{
    public string Name => "Stride";

    // Stride-specific constants
    public const ulong Stride1 = 1 << 20;           // Large constant for precision
    public const ulong DefaultTickets = 100;
    private const ulong InteractiveSleepRatio = 2;  // Sleep 2x more than run = interactive
    private const ulong WakeupBoostDecayNs = 5_000_000; // 5ms

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

        // Insert sorted by pass value
        InsertByPass(cpuData.RunQueue, thread);
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
        // Yielding = put back in queue at current pass position
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        InsertByPass(cpuData.RunQueue, thread);
    }

    // ========== Scheduling Decisions ==========

    public Thread PickNext(PerCpuState cpuState)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();

        if (cpuData.RunQueue.Count == 0)
            return null;

        // First thread has minimum pass value (list is sorted)
        var selected = cpuData.RunQueue[0];
        cpuData.RunQueue.RemoveAt(0);

        return selected;
    }

    public void OnPickFailed(PerCpuState cpuState, Thread thread)
    {
        // Re-add to queue
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        InsertByPass(cpuData.RunQueue, thread);
    }

    public bool OnTick(PerCpuState cpuState, Thread current, ulong elapsedNs)
    {
        if (current == null) return false;

        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = current.GetSchedulerData<StrideThreadData>();

        // Update runtime
        current.TotalRuntime += elapsedNs;

        // Advance pass based on actual time used
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
                return true; // Preempt
        }

        return elapsedNs >= quantum;
    }

    // ========== Load Balancing ==========

    public uint SelectCpu(Thread thread, uint currentCpu, uint cpuCount)
    {
        if ((thread.Flags & ThreadFlags.Pinned) != 0)
            return currentCpu;

        // Simple: find CPU with lowest ticket sum
        uint best = currentCpu;
        ulong bestLoad = GetCpuLoad(currentCpu);

        for (uint cpu = 0; cpu < cpuCount; cpu++)
        {
            if (cpu == currentCpu) continue;
            ulong load = GetCpuLoad(cpu);
            if (load < bestLoad * 80 / 100) // 20% threshold
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

        // Adjust pass relative to new CPU's global pass
        threadData.Pass = (long)toData.GlobalPass + threadData.Remain;

        InsertByPass(toData.RunQueue, thread);
        toData.TotalTickets += threadData.Tickets;
    }

    public void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates)
    {
        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        if (cpuData.RunQueue.Count > 0) return; // Only steal if idle

        // Find busiest CPU
        PerCpuState busiest = null;
        int maxCount = 0;

        foreach (var state in allCpuStates)
        {
            if (state == cpuState) continue;
            var data = state.GetSchedulerData<StrideCpuData>();
            if (data.RunQueue.Count > maxCount)
            {
                maxCount = data.RunQueue.Count;
                busiest = state;
            }
        }

        if (busiest == null || maxCount <= 1) return;

        // Steal lowest priority thread (last in queue)
        var busiestData = busiest.GetSchedulerData<StrideCpuData>();
        var victim = busiestData.RunQueue[^1];

        if ((victim.Flags & ThreadFlags.Pinned) == 0)
            OnThreadMigrate(victim, busiest, cpuState);
    }

    // ========== Dynamic Reconfiguration ==========

    public void SetPriority(PerCpuState cpuState, Thread thread, long priority)
    {
        if (priority <= 0) priority = 1;

        var cpuData = cpuState.GetSchedulerData<StrideCpuData>();
        var threadData = thread.GetSchedulerData<StrideThreadData>();

        UpdateGlobalPass(cpuData);

        ulong oldTickets = threadData.Tickets;
        ulong newTickets = (ulong)priority;
        ulong newStride = Stride1 / newTickets;

        // Scale pass to maintain fairness
        long remain = threadData.Pass - (long)cpuData.GlobalPass;
        remain = (remain * (long)newStride) / (long)threadData.Stride;
        threadData.Pass = (long)cpuData.GlobalPass + remain;

        cpuData.TotalTickets = cpuData.TotalTickets - oldTickets + newTickets;
        threadData.Tickets = newTickets;
        threadData.Stride = newStride;

        // Re-sort in queue
        if (thread.State == ThreadState.Ready)
        {
            cpuData.RunQueue.Remove(thread);
            InsertByPass(cpuData.RunQueue, thread);
        }
    }

    public long GetPriority(Thread thread)
    {
        return (long)thread.GetSchedulerData<StrideThreadData>().Tickets;
    }

    // ========== Private Helpers ==========

    private void UpdateGlobalPass(StrideCpuData cpuData)
    {
        if (cpuData.TotalTickets == 0) return;

        ulong now = GetTimestamp();
        ulong elapsed = now - cpuData.LastPassUpdate;
        ulong globalStride = Stride1 / cpuData.TotalTickets;
        cpuData.GlobalPass += (globalStride * elapsed) / SchedulerManager.DefaultQuantumNs;
        cpuData.LastPassUpdate = now;
    }

    private void InsertByPass(List<Thread> queue, Thread thread)
    {
        var threadData = thread.GetSchedulerData<StrideThreadData>();
        int index = 0;

        for (; index < queue.Count; index++)
        {
            var otherData = queue[index].GetSchedulerData<StrideThreadData>();
            if (threadData.Pass <= otherData.Pass)
                break;
        }

        queue.Insert(index, thread);
    }

    private ulong GetCpuLoad(uint cpuId)
    {
        var state = SchedulerManager.GetCpuState(cpuId);
        return state.GetSchedulerData<StrideCpuData>().TotalTickets;
    }

    private ulong GetTimestamp() => 0; // Platform-specific
}
```

---

## Part 4: Interactive Process Handling Strategy

### The Problem

Pure stride scheduling gives deterministic proportional sharing, but doesn't handle interactive processes well:

1. **Keyboard input example**: User presses a key, the keyboard driver thread wakes up. Under pure stride, it might have to wait many quanta if it fell behind while sleeping.

2. **Fairness vs. Responsiveness trade-off**: We want fairness for CPU-bound tasks but low latency for I/O-bound interactive tasks.

### The Solution: Multi-faceted Approach

#### 4.1 Wake-up Priority Boost

When a thread wakes from sleep/block:

```
if (detected_as_interactive):
    pass = global_pass - (stride / 2)  # Run soon, but not immediately
else:
    pass = max(saved_pass + remain, global_pass - threshold)  # CFS-style cap
```

#### 4.2 Interactive Detection

Track thread behavior to detect interactive patterns:

```
interactive_score = sleep_time / run_time

if (interactive_score > 2.0):
    mark_as_interactive()
```

#### 4.3 Boost Decay

The priority boost is temporary (5ms), preventing an "interactive" thread that suddenly becomes CPU-bound from starving others.

---

## Part 5: File Structure

```
src/Cosmos.Kernel.Core/
  Scheduler/
    ThreadState.cs             # ThreadState and ThreadFlags enums
    SchedulerExtensible.cs     # Base class for extension pattern
    Thread.cs                  # Thread class
    PerCpuState.cs             # Per-CPU state class
    SpinLock.cs                # Simple spinlock for SMP
    IScheduler.cs              # Interface definition
    SchedulerManager.cs        # Global scheduler management

  Scheduler/Stride/
    StrideThreadData.cs        # Per-thread extension data
    StrideCpuData.cs           # Per-CPU extension data
    StrideScheduler.cs         # Stride scheduling implementation

  Scheduler/RoundRobin/        # (Future)
  Scheduler/CFS/               # (Future)
```

---

## Part 6: Implementation Order

1. **Phase 1: Foundation** ✅
   - [x] Create `ThreadState.cs` enums
   - [x] Create `SchedulerExtensible` base class
   - [x] Create `Thread` class
   - [x] Create `PerCpuState` class
   - [x] Create `SpinLock` struct
   - [x] Create `IScheduler` interface
   - [x] Create `SchedulerManager` static class

2. **Phase 2: Stride Scheduler**
   - [ ] Create `StrideThreadData` and `StrideCpuData`
   - [ ] Implement `StrideScheduler`
   - [ ] Test with basic thread creation

3. **Phase 3: Context Switch**
   - [ ] Assembly context switch for x64
   - [ ] Assembly context switch for ARM64
   - [ ] Hook into timer interrupt

4. **Phase 4: Integration**
   - [ ] Initialize scheduler from kernel main
   - [ ] Create idle thread per CPU
   - [ ] Integrate with existing PIT/LAPIC timer

5. **Phase 5: Polish**
   - [ ] Load balancing
   - [ ] Thread migration
   - [ ] Scheduler hot-swap

---

## Summary

This design provides:

1. **Fully generic API** - `IScheduler` has no algorithm-specific concepts
2. **Clean C# semantics** - Classes with object references, no unsafe pointers in API
3. **Extension pattern** - `SchedulerData` property allows any scheduler to attach its own data
4. **Stride scheduling** - Deterministic proportional sharing as first implementation
5. **Interactive responsiveness** - Priority boost on wake-up with automatic detection
6. **Easy extensibility** - Add RR, CFS, or other algorithms by implementing `IScheduler`
